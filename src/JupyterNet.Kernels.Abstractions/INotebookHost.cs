namespace JupyterNet.Kernels.Abstractions;

/// <summary>A cell as the host currently knows it — refreshed from the extension on every execute request.</summary>
public sealed record NotebookCellInfo(int Index, string Language, string Code);

/// <summary>
/// What a kernel can ask the host for beyond its own cell. Only the "ralf" kernel uses this today,
/// to give the agent tools that inspect/run/edit the rest of the notebook — it is on
/// <see cref="Abstractions"/> rather than a Ralf-only project because it is a general
/// host&lt;-&gt;kernel contract, not something specific to RalfAI.
/// </summary>
public interface INotebookHost
{
    IReadOnlyList<NotebookCellInfo> Cells { get; }

    /// <summary>Runs another cell (by the index the extension last reported) through its own kernel and returns its captured output as text.</summary>
    Task<string> RunCellAsync(int index, CancellationToken cancellationToken);

    /// <summary>Asks the extension to overwrite a cell's source in the editor.</summary>
    void RequestCellEdit(int index, string newCode);
}
