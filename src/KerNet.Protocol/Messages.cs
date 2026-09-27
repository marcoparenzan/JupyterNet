namespace KerNet.Protocol;

/// <summary>One of the four language ids a KerNet notebook cell can be executed as.</summary>
public static class KernelIds
{
    public const string CSharp = "csharp";
    public const string PySharp = "pysharp";
    public const string Ontly = "ontly";
    public const string Ralf = "ralf";
}

/// <summary>A lightweight snapshot of one notebook cell, as the extension currently sees it.</summary>
public sealed record NotebookCellSnapshot(int Index, string Language, string Code);

/// <summary>
/// Parameters of an "execute" request: which kernel, the cell's source text, and — sent with
/// every request, not just "ralf" ones, so the host's notebook cache never goes stale — a
/// snapshot of every cell currently in the notebook. Only the "ralf" kernel's notebook tools
/// (<c>Notebook_*</c>) read <see cref="Cells"/>; other kernels ignore it.
/// </summary>
public sealed record ExecuteParams(string Kernel, string Code, IReadOnlyList<NotebookCellSnapshot>? Cells = null);

/// <summary>
/// A request sent by the extension to <c>KerNet.Host</c> on stdin, one JSON object per line.
/// <see cref="Method"/> is <c>"execute"</c> (with <see cref="Params"/> set) or <c>"shutdown"</c>.
/// </summary>
public sealed record HostRequest(string Id, string Method, ExecuteParams? Params = null);

/// <summary>
/// An event emitted by <c>KerNet.Host</c> on stdout, one JSON object per line, correlated to a
/// request by <see cref="ExecutionId"/> (the request's <see cref="HostRequest.Id"/>).
/// </summary>
public sealed record HostEvent(
    string Event,
    string ExecutionId,
    string? MimeType = null,
    string? Data = null,
    string? Message = null,
    string? StackTrace = null,
    string? Status = null,
    int? CellIndex = null,
    string? NewCode = null)
{
    public static HostEvent Output(string executionId, string mimeType, string data) =>
        new("output", executionId, MimeType: mimeType, Data: data);

    public static HostEvent Error(string executionId, string message, string? stackTrace = null) =>
        new("error", executionId, Message: message, StackTrace: stackTrace);

    public static HostEvent Complete(string executionId, bool ok) =>
        new("complete", executionId, Status: ok ? "ok" : "error");

    /// <summary>
    /// Asks the extension to overwrite another cell's source — how the "ralf" kernel's
    /// <c>Notebook_SetCellCode</c> tool reaches back into the editor, since only the extension can
    /// apply a <c>WorkspaceEdit</c> to a cell's document.
    /// </summary>
    public static HostEvent EditCell(string executionId, int cellIndex, string newCode) =>
        new("editCell", executionId, CellIndex: cellIndex, NewCode: newCode);
}

public static class MimeTypes
{
    public const string PlainText = "text/plain";
    public const string Html = "text/html";
}
