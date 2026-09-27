using JupyterNet.Kernels.Abstractions;
using JupyterNet.Protocol;

namespace JupyterNet.Host;

/// <summary>Writes a kernel's output as NDJSON events on the host's real stdout, tagged with the request id.</summary>
internal sealed class NdjsonOutputSink(TextWriter stdout, string executionId) : IKernelOutputSink
{
    public void WriteText(string text) =>
        NdjsonProtocol.WriteEvent(stdout, HostEvent.Output(executionId, MimeTypes.PlainText, text));

    public void WriteHtml(string html) =>
        NdjsonProtocol.WriteEvent(stdout, HostEvent.Output(executionId, MimeTypes.Html, html));

    public void WriteError(string message, string? stackTrace = null) =>
        NdjsonProtocol.WriteEvent(stdout, HostEvent.Error(executionId, message, stackTrace));
}
