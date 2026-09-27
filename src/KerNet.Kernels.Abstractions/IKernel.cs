namespace KerNet.Kernels.Abstractions;

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
