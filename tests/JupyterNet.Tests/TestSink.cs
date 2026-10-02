using JupyterNet.Kernels.Abstractions;

namespace JupyterNet.Tests;

/// <summary>A recording <see cref="IKernelOutputSink"/> for assertions — every kernel test uses this instead of a real protocol/console sink.</summary>
internal sealed class TestSink : IKernelOutputSink
{
    public List<string> Texts { get; } = [];
    public List<string> Htmls { get; } = [];
    public List<(string Message, string? StackTrace)> Errors { get; } = [];
    public List<(string MimeType, byte[] Data)> Images { get; } = [];

    public bool HasError => Errors.Count > 0;

    public void WriteText(string text) => Texts.Add(text);
    public void WriteHtml(string html) => Htmls.Add(html);
    public void WriteImage(string mimeType, byte[] data) => Images.Add((mimeType, data));
    public void WriteError(string message, string? stackTrace = null) => Errors.Add((message, stackTrace));
}
