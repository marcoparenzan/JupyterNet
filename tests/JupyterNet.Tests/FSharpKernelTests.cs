using JupyterNet.Kernels.FSharp;

namespace JupyterNet.Tests;

public class FSharpKernelTests
{
    [Fact]
    public async Task SessionStatePersistsAcrossCells()
    {
        var kernel = new FSharpKernel();
        var sink = new TestSink();

        await kernel.ExecuteAsync("let x = 21", sink, default);
        await kernel.ExecuteAsync("x * 2", sink, default);

        Assert.False(sink.HasError);
        Assert.Contains("42", sink.Texts);
    }

    [Fact]
    public async Task PrintfnIsCapturedWithoutFsiEcho()
    {
        var kernel = new FSharpKernel();
        var sink = new TestSink();

        await kernel.ExecuteAsync("let greeting = \"hello\"\nprintfn \"%s\" greeting", sink, default);

        Assert.False(sink.HasError);
        Assert.Contains(sink.Texts, t => t.Contains("hello", StringComparison.Ordinal));
        Assert.DoesNotContain(sink.Texts, t => t.Contains("val ", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DivideByZeroBecomesWriteError()
    {
        var kernel = new FSharpKernel();
        var sink = new TestSink();

        await kernel.ExecuteAsync("1 / 0", sink, default);

        Assert.True(sink.HasError);
    }

    [Fact]
    public async Task InjectedVariableIsUsableInALaterCell()
    {
        var kernel = new FSharpKernel();
        var sink = new TestSink();

        await kernel.SetVariableAsync("weather", new Weather(), default);
        await kernel.ExecuteAsync("weather.City", sink, default);
        await kernel.ExecuteAsync("weather.TempC(3)", sink, default);

        Assert.False(sink.HasError);
        Assert.Contains("Trento", sink.Texts);
        Assert.Contains("23", sink.Texts);
    }
}
