using JupyterNet.Engine;
using JupyterNet.Kernels.Abstractions;
using JupyterNet.Protocol;

namespace JupyterNet.Tests;

public class ImageOutputTests
{
    private static readonly byte[] PngHeader = [0x89, 0x50, 0x4E, 0x47];

    /// <summary>A sink written before images existed: it only implements the original three methods.</summary>
    private sealed class LegacySink : IKernelOutputSink
    {
        public List<string> Htmls { get; } = [];
        public void WriteText(string text) { }
        public void WriteHtml(string html) => Htmls.Add(html);
        public void WriteError(string message, string? stackTrace = null) { }
    }

    [Fact]
    public void DefaultImplementationFallsBackToAnInlineDataUriImg()
    {
        IKernelOutputSink sink = new LegacySink();
        sink.WriteImage("image/png", PngHeader);

        var html = Assert.Single(((LegacySink)sink).Htmls);
        Assert.Equal($"<img src=\"data:image/png;base64,{Convert.ToBase64String(PngHeader)}\" />", html);
    }

    [Fact]
    public void BinaryOutputEventCarriesBase64AndItsEncoding()
    {
        var evt = HostEvent.OutputBinary("7", MimeTypes.Png, PngHeader);
        var json = System.Text.Json.JsonSerializer.Serialize(evt, NdjsonProtocol.Options);

        Assert.Contains("\"encoding\":\"base64\"", json);
        Assert.Contains("\"mimeType\":\"image/png\"", json);
        var back = System.Text.Json.JsonSerializer.Deserialize<HostEvent>(json, NdjsonProtocol.Options)!;
        Assert.Equal(PngHeader, Convert.FromBase64String(back.Data!));
    }

    [Fact]
    public async Task CSharpCellsCanDisplayAnImage()
    {
        var session = new NotebookSession();
        var sink = new TestSink();

        var ok = await session.ExecuteAsync("csharp", "Display.Image(new byte[] { 1, 2, 3 });", sink, default);

        Assert.True(ok);
        var (mime, data) = Assert.Single(sink.Images);
        Assert.Equal("image/png", mime);
        Assert.Equal(new byte[] { 1, 2, 3 }, data);
    }
}
