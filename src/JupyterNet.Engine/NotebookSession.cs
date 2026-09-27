using JupyterNet.Kernels.Abstractions;
using JupyterNet.Kernels.CSharp;
using JupyterNet.Kernels.FSharp;
using JupyterNet.Kernels.PowerShell;

namespace JupyterNet.Engine;

/// <summary>
/// The reusable engine behind every JupyterNet host: owns the builtin (<c>csharp</c>/<c>fsharp</c>/
/// <c>powershell</c>) and discovered-plugin kernels for one notebook session, dispatches cell execution to them, and
/// is itself the <see cref="INotebookHost"/> a Ralf-style kernel's tools call against. Used by
/// <c>JupyterNet.Host</c> (wrapping it in the NDJSON protocol), the <c>jupyternet</c> CLI (wrapping
/// it in a headless run loop), and directly by any embedding application — see
/// <see cref="SetVariableAsync"/> for the embedding entry point.
/// </summary>
public sealed class NotebookSession : INotebookHost
{
    private readonly Dictionary<string, IKernel> _kernels = new();
    private readonly IReadOnlyDictionary<string, IKernelPlugin> _plugins;
    private IReadOnlyList<NotebookCellInfo> _cells = [];

    /// <summary>Raised when a kernel (Ralf's <c>Notebook_SetCellCode</c> tool, in practice) asks to overwrite a cell's source.
    /// JupyterNet.Host turns this into an <c>editCell</c> NDJSON event for the extension to apply; a headless host
    /// (the CLI, an embedding app) can instead apply it directly to its own in-memory <see cref="NotebookDocument"/>.</summary>
    public event Action<int, string>? CellEditRequested;

    /// <param name="pluginWarnings">Where plugin-discovery warnings go (a missing/broken kernel plugin directory) — defaults to discarding them.</param>
    /// <param name="kernelPathsOverride">Explicit plugin search directories, bypassing <c>JUPYTERNET_KERNEL_PATHS</c>/the `kernels/` fallback (the CLI's <c>--kernel-paths</c>).</param>
    public NotebookSession(TextWriter? pluginWarnings = null, IEnumerable<string>? kernelPathsOverride = null)
    {
        _plugins = KernelPluginLoader.DiscoverPlugins(pluginWarnings ?? TextWriter.Null, kernelPathsOverride);
    }

    public IReadOnlyList<NotebookCellInfo> Cells => _cells;

    /// <summary>Refreshes the session's notebook-wide cell cache — what Ralf's <c>Notebook_ListCells</c>/<c>GetCell</c>/<c>RunCell</c> tools see.</summary>
    public void UpdateCells(IReadOnlyList<NotebookCellInfo> cells) => _cells = cells;

    /// <summary>
    /// Gets (creating and caching on first use, exactly like a builtin kernel) the kernel for
    /// <paramref name="kernelId"/> — <c>csharp</c>/<c>fsharp</c>/<c>powershell</c> directly,
    /// anything else from a discovered plugin. Throws if the id matches neither; callers executing
    /// a cell should prefer <see cref="ExecuteAsync"/>, which turns that into a normal
    /// <see cref="IKernelOutputSink.WriteError"/>.
    /// </summary>
    public IKernel GetOrCreateKernel(string kernelId)
    {
        if (_kernels.TryGetValue(kernelId, out var existing)) return existing;
        IKernel created = kernelId switch
        {
            KernelIdsLocal.CSharp => new CSharpKernel(),
            KernelIdsLocal.FSharp => new FSharpKernel(),
            KernelIdsLocal.PowerShell => new PowerShellKernel(),
            _ when _plugins.TryGetValue(kernelId, out var plugin) => plugin.CreateKernel(this),
            _ => throw new InvalidOperationException($"Unknown kernel '{kernelId}'.")
        };
        return _kernels[kernelId] = created;
    }

    /// <summary>Runs one cell's code through its kernel. Returns false (after a <see cref="IKernelOutputSink.WriteError"/>) instead of throwing for an unknown kernel id or a kernel exception.</summary>
    public async Task<bool> ExecuteAsync(string kernelId, string code, IKernelOutputSink sink, CancellationToken cancellationToken)
    {
        var tracking = new ErrorTrackingSink(sink);
        try
        {
            var kernel = GetOrCreateKernel(kernelId);
            await kernel.ExecuteAsync(code, tracking, cancellationToken);
        }
        catch (Exception ex)
        {
            tracking.WriteError(ex.Message, ex.StackTrace);
        }
        return !tracking.HasError;
    }

    /// <summary>Runs the cell at <paramref name="index"/> from the session's own <see cref="Cells"/> cache.</summary>
    public Task<bool> ExecuteCellAsync(int index, IKernelOutputSink sink, CancellationToken cancellationToken)
    {
        var cell = _cells.FirstOrDefault(c => c.Index == index);
        if (cell is null)
        {
            sink.WriteError($"No cell at index {index}.");
            return Task.FromResult(false);
        }
        return ExecuteAsync(cell.Language, cell.Code, sink, cancellationToken);
    }

    /// <summary>
    /// The embedding entry point: injects a live .NET object into <paramref name="kernelId"/>'s
    /// context under <paramref name="name"/>, so notebook cells can use it directly. Throws
    /// <see cref="NotSupportedException"/> if that kernel doesn't implement
    /// <see cref="IVariableInjectable"/> — injection is an opt-in kernel capability, not every
    /// kernel can support it the same way (PySharp's own kernel has an equivalent native
    /// capability, <c>PyEngine.SetVariable</c>, not yet wired to this interface).
    /// </summary>
    public Task SetVariableAsync(string kernelId, string name, object? value, CancellationToken cancellationToken = default)
    {
        var kernel = GetOrCreateKernel(kernelId);
        if (kernel is not IVariableInjectable injectable)
            throw new NotSupportedException($"Kernel '{kernelId}' ({kernel.GetType().Name}) does not support variable injection.");
        return injectable.SetVariableAsync(name, value, cancellationToken);
    }

    async Task<string> INotebookHost.RunCellAsync(int index, CancellationToken cancellationToken)
    {
        var cell = _cells.FirstOrDefault(c => c.Index == index);
        if (cell is null) return $"No cell at index {index}.";

        var sink = new BufferingSink();
        await ExecuteAsync(cell.Language, cell.Code, sink, cancellationToken);
        return sink.ToString();
    }

    void INotebookHost.RequestCellEdit(int index, string newCode) => CellEditRequested?.Invoke(index, newCode);
}

/// <summary>Local copies of the builtin kernel ids, to avoid a ProjectReference on JupyterNet.Protocol just for a few string constants.</summary>
internal static class KernelIdsLocal
{
    public const string CSharp = "csharp";
    public const string FSharp = "fsharp";
    public const string PowerShell = "powershell";
}

/// <summary>Wraps a sink to track whether <see cref="IKernelOutputSink.WriteError"/> was ever called, regardless of which concrete sink a caller passed in.</summary>
internal sealed class ErrorTrackingSink(IKernelOutputSink inner) : IKernelOutputSink
{
    public bool HasError { get; private set; }
    public void WriteText(string text) => inner.WriteText(text);
    public void WriteHtml(string html) => inner.WriteHtml(html);
    public void WriteError(string message, string? stackTrace = null)
    {
        HasError = true;
        inner.WriteError(message, stackTrace);
    }
}

/// <summary>Collects a kernel's output as plain text instead of forwarding it live — backs <see cref="NotebookSession"/>'s <see cref="INotebookHost.RunCellAsync"/>, so a Ralf-style kernel's "run another cell" tool gets a string result rather than a stream of events.</summary>
internal sealed class BufferingSink : IKernelOutputSink
{
    private readonly System.Text.StringBuilder _text = new();

    public void WriteText(string text) => _text.AppendLine(text);
    public void WriteHtml(string html) => _text.AppendLine(html);

    public void WriteError(string message, string? stackTrace = null)
    {
        _text.AppendLine("ERROR: " + message);
        if (stackTrace is not null) _text.AppendLine(stackTrace);
    }

    public override string ToString() => _text.Length == 0 ? "(no output)" : _text.ToString();
}
