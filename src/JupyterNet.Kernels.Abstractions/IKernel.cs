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

    /// <summary>
    /// Emits a binary image (<c>image/png</c>, <c>image/jpeg</c>, ...). The default implementation
    /// falls back to an inline <c>&lt;img&gt;</c> data-URI through <see cref="WriteHtml"/>, so a sink written
    /// before images existed — or a kernel talking to one — keeps working; sinks that can carry real
    /// binary output (the NDJSON host sink) override it.
    /// </summary>
    void WriteImage(string mimeType, byte[] data)
        => WriteHtml($"<img src=\"data:{mimeType};base64,{Convert.ToBase64String(data)}\" />");
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
/// A kernel that lets a host inject a live .NET object into its context under a name, so notebook
/// cells can use it directly — the embedding story: an application creating its own
/// <c>NotebookSession</c> can hand a notebook a real object (a service, a config, live data)
/// instead of the notebook only ever seeing what it can construct itself. Optional: not every
/// kernel can support this (a kernel's host, e.g. <c>JupyterNet.Engine.NotebookSession</c>, should
/// treat a kernel that doesn't implement this as simply not supporting injection).
/// </summary>
public interface IVariableInjectable
{
    Task SetVariableAsync(string name, object? value, CancellationToken cancellationToken);
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
