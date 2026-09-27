namespace JupyterNet.Kernels.Abstractions;

/// <summary>
/// Where a kernel sends what a cell produced. A cell can write any number of text/HTML chunks
/// before completing; <see cref="WriteError"/> does not end the execution by itself — the host
/// still expects exactly one final completion, decided by whether the kernel throws or returns.
/// </summary>
public interface IKernelOutputSink
{
    void WriteText(string text);
    void WriteHtml(string html);
    void WriteError(string message, string? stackTrace = null);
}

/// <summary>
/// One notebook language. A kernel instance is stateful: it is created once per session (per
/// notebook) and reused across every cell of its language, so it is the natural place to keep
/// whatever "variables persist across cells" means for that language.
/// </summary>
public interface IKernel
{
    /// <summary>The language id this kernel handles — one of <see cref="Protocol.KernelIds"/>.</summary>
    string Id { get; }

    Task ExecuteAsync(string code, IKernelOutputSink sink, CancellationToken cancellationToken);
}

/// <summary>
/// The entire contract an external kernel plugin (PySharp/Ontly/Ralf — each living in its own
/// engine's repo, not JupyterNet's) exposes to JupyterNet.Host. One public, parameterless-
/// constructor class per plugin assembly implements this; the host finds it by scanning the
/// assembly's types, so there is nothing else to register or configure.
/// </summary>
public interface IKernelPlugin
{
    /// <summary>The kernel id this plugin serves — what a notebook cell's language must match.</summary>
    string KernelId { get; }

    /// <summary>
    /// Creates the kernel instance for one session. Called at most once per session, the first
    /// time a cell of <see cref="KernelId"/> actually executes (same laziness as a builtin kernel).
    /// </summary>
    IKernel CreateKernel(INotebookHost notebookHost);
}
