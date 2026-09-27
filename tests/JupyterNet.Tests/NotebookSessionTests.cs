using JupyterNet.Engine;
using JupyterNet.Kernels.Abstractions;

namespace JupyterNet.Tests;

public class NotebookSessionTests
{
    [Fact]
    public async Task RunsCellsAcrossBothBuiltinKernelsInOneSession()
    {
        var session = new NotebookSession();
        var sink = new TestSink();

        Assert.True(await session.ExecuteAsync("csharp", "var x = 1;", sink, default));
        Assert.True(await session.ExecuteAsync("csharp", "x + 1", sink, default));
        Assert.True(await session.ExecuteAsync("fsharp", "let y = 10", sink, default));
        Assert.True(await session.ExecuteAsync("fsharp", "y * 3", sink, default));

        Assert.False(sink.HasError);
        Assert.Contains("2", sink.Texts);
        Assert.Contains("30", sink.Texts);
    }

    [Fact]
    public async Task UnknownKernelReturnsFalseAndWritesErrorInsteadOfThrowing()
    {
        var session = new NotebookSession();
        var sink = new TestSink();

        var ok = await session.ExecuteAsync("no-such-kernel", "whatever", sink, default);

        Assert.False(ok);
        Assert.True(sink.HasError);
    }

    [Fact]
    public async Task ExecuteCellAtAnUnknownIndexIsAnErrorNotAnException()
    {
        var session = new NotebookSession();
        var sink = new TestSink();

        var ok = await session.ExecuteCellAsync(99, sink, default);

        Assert.False(ok);
        Assert.True(sink.HasError);
    }

    [Fact]
    public async Task RunCellAsyncThroughINotebookHostBuffersOutputAsText()
    {
        var session = new NotebookSession();
        session.UpdateCells([new NotebookCellInfo(0, "csharp", "42")]);

        var text = await ((INotebookHost)session).RunCellAsync(0, default);

        Assert.Contains("42", text);
    }

    [Fact]
    public async Task SetVariableOnAnUnknownKernelThrows()
    {
        var session = new NotebookSession();
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.SetVariableAsync("no-such-kernel", "x", 1, default));
    }
}
