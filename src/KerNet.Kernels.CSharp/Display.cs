namespace KerNet.Kernels.CSharp;

/// <summary>
/// Lets a C# cell push rich output explicitly, e.g. <c>Display.Html("&lt;b&gt;hi&lt;/b&gt;")</c>.
/// Backed by an <see cref="AsyncLocal{T}"/> so it works without threading a sink parameter through
/// user code — <see cref="CSharpKernel"/> sets it for the duration of one cell's execution.
/// </summary>
public static class Display
{
    internal static readonly AsyncLocal<Abstractions.IKernelOutputSink?> CurrentSink = new();

    public static void Html(string html) => CurrentSink.Value?.WriteHtml(html);

    public static void Text(string text) => CurrentSink.Value?.WriteText(text);
}
