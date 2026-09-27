using KerNet.Kernels.Abstractions;
using KerNet.Protocol;

namespace KerNet.Host;

/// <summary>
/// The host's <see cref="INotebookHost"/> implementation: a cache of the notebook's cells (kept
/// fresh from every "execute" request's <see cref="ExecuteParams.Cells"/>) plus the two operations
/// the "ralf" kernel's tools need — running another cell through its own kernel, and asking the
/// extension to overwrite a cell's source.
/// </summary>
internal sealed class NotebookHostState(TextWriter stdout, Func<string, IKernel> getOrCreateKernel) : INotebookHost
{
    private IReadOnlyList<NotebookCellInfo> _cells = [];

    public IReadOnlyList<NotebookCellInfo> Cells => _cells;

    public void UpdateFrom(IReadOnlyList<NotebookCellSnapshot>? snapshot)
    {
        if (snapshot is null) return;
        _cells = snapshot.Select(c => new NotebookCellInfo(c.Index, c.Language, c.Code)).ToList();
    }

    public async Task<string> RunCellAsync(int index, CancellationToken cancellationToken)
    {
        var cell = _cells.FirstOrDefault(c => c.Index == index);
        if (cell is null) return $"No cell at index {index}.";

        var kernel = getOrCreateKernel(cell.Language);
        var sink = new BufferingSink();
        await kernel.ExecuteAsync(cell.Code, sink, cancellationToken);
        return sink.ToString();
    }

    public void RequestCellEdit(int index, string newCode) =>
        NdjsonProtocol.WriteEvent(stdout, HostEvent.EditCell(executionId: "", cellIndex: index, newCode: newCode));
}
