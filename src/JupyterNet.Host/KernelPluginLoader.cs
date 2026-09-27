using System.Reflection;
using JupyterNet.Kernels.Abstractions;

namespace JupyterNet.Host;

/// <summary>
/// Discovers and loads external kernel plugins (PySharp/Ontly/Ralf, each living in its own engine's
/// repo, not JupyterNet's). Search directories come from <c>JUPYTERNET_KERNEL_PATHS</c> (an OS
/// path-list, one directory per plugin — each expected to be that plugin project's own
/// <c>dotnet publish</c> output), falling back to scanning <c>&lt;host base dir&gt;/kernels/*</c> if
/// that variable isn't set.
///
/// Every plugin loads into the default load context via <see cref="Assembly.LoadFrom"/>, which —
/// specifically for this case — comes with .NET's own "LoadFrom context" dependency probing: an
/// assembly that can't otherwise be resolved is looked for beside the assembly that requested it,
/// so a plugin's own private dependencies (everything <c>dotnet publish</c> copied into its
/// folder) are found automatically with no code here.
///
/// An earlier version of this loader gave each plugin its own collectible
/// <see cref="System.Runtime.Loader.AssemblyLoadContext"/> for stronger version isolation between
/// plugins (relevant since e.g. the Ralf plugin drags in Azure SDK/Speech SDK versions). That
/// produced a reproducible failure instead: running a builtin kernel (C# *or* F#, both of which do
/// their own dynamic/JIT compilation) before a plugin's first use made that plugin's own
/// same-folder dependency resolution start failing with "operation is not legal in the current
/// state" — evidence of the two dynamic-compilation engines interacting with custom
/// AssemblyLoadContexts in a way not fully understood, not something to ship. A single shared load
/// context is the simpler, correct-first choice for what this actually is: three specific plugins
/// on one person's machine, not an untrusted multi-tenant plugin host — version conflicts between
/// them are a real but much smaller risk than a host that intermittently fails to load a kernel.
/// </summary>
internal static class KernelPluginLoader
{
    public static IReadOnlyDictionary<string, IKernelPlugin> DiscoverPlugins(TextWriter warnings)
    {
        var plugins = new Dictionary<string, IKernelPlugin>();
        foreach (var directory in ResolvePluginDirectories())
        {
            try
            {
                var plugin = LoadPlugin(directory);
                if (plugin is null)
                {
                    warnings.WriteLine($"JupyterNet.Host: no IKernelPlugin found in '{directory}', skipping.");
                    continue;
                }
                plugins[plugin.KernelId] = plugin;
            }
            catch (Exception ex)
            {
                warnings.WriteLine($"JupyterNet.Host: failed to load kernel plugin from '{directory}': {ex.Message}");
            }
        }
        return plugins;
    }

    private static IEnumerable<string> ResolvePluginDirectories()
    {
        var configured = Environment.GetEnvironmentVariable("JUPYTERNET_KERNEL_PATHS");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            foreach (var path in configured.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                if (Directory.Exists(path)) yield return path;
            yield break;
        }

        var defaultRoot = Path.Combine(AppContext.BaseDirectory, "kernels");
        if (!Directory.Exists(defaultRoot)) yield break;
        foreach (var directory in Directory.GetDirectories(defaultRoot)) yield return directory;
    }

    private static IKernelPlugin? LoadPlugin(string directory)
    {
        // A `dotnet publish` output has exactly one <name>.deps.json, next to its matching main
        // DLL; every other DLL in the folder is a dependency and has no deps.json of its own.
        var mainAssemblyPath = Directory.GetFiles(directory, "*.dll")
            .FirstOrDefault(dll => File.Exists(Path.ChangeExtension(dll, ".deps.json")));
        if (mainAssemblyPath is null) return null;

        var assembly = Assembly.LoadFrom(mainAssemblyPath);
        var pluginType = assembly.GetTypes().FirstOrDefault(t =>
            typeof(IKernelPlugin).IsAssignableFrom(t) && t is { IsAbstract: false, IsInterface: false });
        return pluginType is null ? null : (IKernelPlugin)Activator.CreateInstance(pluginType)!;
    }
}
