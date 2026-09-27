using System.Text;
using JupyterNet.Engine;
using JupyterNet.Host;
using JupyterNet.Kernels.Abstractions;
using JupyterNet.Protocol;

var stdout = Console.Out;
var session = new NotebookSession(pluginWarnings: Console.Error);
session.CellEditRequested += (index, newCode) =>
    NdjsonProtocol.WriteEvent(stdout, HostEvent.EditCell(executionId: "", cellIndex: index, newCode: newCode));

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

    if (request.Params.Cells is not null)
        session.UpdateCells(request.Params.Cells.Select(c => new NotebookCellInfo(c.Index, c.Language, c.Code)).ToList());

    var sink = new NdjsonOutputSink(stdout, request.Id);
    var ok = await session.ExecuteAsync(request.Params.Kernel, request.Params.Code, sink, CancellationToken.None);
    NdjsonProtocol.WriteEvent(stdout, HostEvent.Complete(request.Id, ok));
}
