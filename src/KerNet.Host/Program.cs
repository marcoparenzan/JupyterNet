using System.Text;
using KerNet.Host;
using KerNet.Kernels.Abstractions;
using KerNet.Kernels.CSharp;
using KerNet.Kernels.Ontly;
using KerNet.Kernels.PySharp;
using KerNet.Kernels.Ralf;
using KerNet.Protocol;

var stdout = Console.Out;
var kernels = new Dictionary<string, IKernel>();
NotebookHostState? notebookState = null;

IKernel GetOrCreateKernel(string id) => kernels.TryGetValue(id, out var existing) ? existing : kernels[id] = id switch
{
    KernelIds.CSharp => new CSharpKernel(),
    KernelIds.PySharp => new PySharpKernel(),
    KernelIds.Ontly => new OntlyKernel(),
    KernelIds.Ralf => new RalfKernel(notebookState!),
    _ => throw new InvalidOperationException($"Unknown kernel '{id}'.")
};

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
        await Console.Error.WriteLineAsync($"KerNet.Host: malformed request ignored ({ex.Message})");
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
