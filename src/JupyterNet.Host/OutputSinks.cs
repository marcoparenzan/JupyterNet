using System.Text;
using JupyterNet.Kernels.Abstractions;
using JupyterNet.Protocol;

namespace JupyterNet.Host;

/// <summary>Writes a kernel's output as NDJSON events on the host's real stdout, tagged with the request id.</summary>
internal sealed class NdjsonOutputSink(TextWriter stdout, string executionId) : IKernelOutputSink
{
    /// <summary>Set once <see cref="WriteError"/> is called, so the host can send an "error" completion even when the kernel didn't throw.</summary>
    public bool HasError { get; private set; }

    public void WriteText(string text) =>
        NdjsonProtocol.WriteEvent(stdout, HostEvent.Output(executionId, MimeTypes.PlainText, text));

    public void WriteHtml(string html) =>
        NdjsonProtocol.WriteEvent(stdout, HostEvent.Output(executionId, MimeTypes.Html, html));

    public void WriteError(string message, string? stackTrace = null)
    {
        HasError = true;
        NdjsonProtocol.WriteEvent(stdout, HostEvent.Error(executionId, message, stackTrace));
    }
}

/// <summary>
/// Collects a kernel's output as plain text instead of emitting NDJSON — used when the "ralf"
/// kernel runs another cell via its Notebook_RunCell tool, so the result can go back to the agent
/// as a string rather than as protocol events meant for the editor.
/// </summary>
internal sealed class BufferingSink : IKernelOutputSink
{
    private readonly StringBuilder _text = new();

    public void WriteText(string text) => _text.AppendLine(text);
    public void WriteHtml(string html) => _text.AppendLine(html);

    public void WriteError(string message, string? stackTrace = null)
    {
        _text.AppendLine("ERROR: " + message);
        if (stackTrace is not null) _text.AppendLine(stackTrace);
    }

    public override string ToString() => _text.Length == 0 ? "(no output)" : _text.ToString();
}
