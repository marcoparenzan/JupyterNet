using System.Text;
using JupyterNet.Host;
using JupyterNet.Kernels.Abstractions;
using JupyterNet.Kernels.CSharp;
using JupyterNet.Kernels.FSharp;
using JupyterNet.Protocol;

var stdout = Console.Out;
var kernels = new Dictionary<string, IKernel>();
NotebookHostState? notebookState = null;

// csharp/fsharp are builtin — the two ".NET languages" JupyterNet.Host ships with directly.
// Everything else (pysharp/ontly/ralf, by convention, though a plugin can declare any id) is
// discovered from JUPYTERNET_KERNEL_PATHS/`kernels/*` at startup — see KernelPluginLoader.
var plugins = KernelPluginLoader.DiscoverPlugins(Console.Error);

IKernel GetOrCreateKernel(string id)
{
    if (kernels.TryGetValue(id, out var existing)) return existing;
    IKernel created = id switch
    {
        KernelIds.CSharp => new CSharpKernel(),
        KernelIds.FSharp => new FSharpKernel(),
        _ when plugins.TryGetValue(id, out var plugin) => plugin.CreateKernel(notebookState!),
        _ => throw new InvalidOperationException($"Unknown kernel '{id}'.")
    };
    return kernels[id] = created;
}

notebookState = new NotebookHostState(stdout, GetOrCreateKernel);

using var stdin = new StreamReader(Console.OpenStandardInput(), Encoding.UTF8);
string? line;
while ((line = await stdin.ReadLineAsync()) is not null)
{
    if (line.Length == 0) continue;

    HostRequest? request;
    try
    {
        request = NdjsonProtocol.ParseRequest(line);
    }
    catch (Exception ex)
    {
        await Console.Error.WriteLineAsync($"JupyterNet.Host: malformed request ignored ({ex.Message})");
        continue;
    }

    if (request is null) continue;
    if (request.Method == "shutdown") break;
    if (request.Method != "execute" || request.Params is null) continue;

    notebookState.UpdateFrom(request.Params.Cells);
    var sink = new NdjsonOutputSink(stdout, request.Id);
    var ok = true;
    try
    {
        var kernel = GetOrCreateKernel(request.Params.Kernel);
        await kernel.ExecuteAsync(request.Params.Code, sink, CancellationToken.None);
    }
    catch (Exception ex)
    {
        ok = false;
        sink.WriteError(ex.Message, ex.StackTrace);
    }

    NdjsonProtocol.WriteEvent(stdout, HostEvent.Complete(request.Id, ok && !sink.HasError));
}
