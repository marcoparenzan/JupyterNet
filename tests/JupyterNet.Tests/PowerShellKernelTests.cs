using JupyterNet.Kernels.PowerShell;

namespace JupyterNet.Tests;

public class PowerShellKernelTests
{
    [Fact]
    public async Task SessionStatePersistsAcrossCells()
    {
        using var kernel = new PowerShellKernel();
        var sink = new TestSink();

        await kernel.ExecuteAsync("$x = 21", sink, default);
        await kernel.ExecuteAsync("$x * 2", sink, default);

        Assert.False(sink.HasError);
        Assert.Contains("42", sink.Texts);
    }

    [Fact]
    public async Task WriteHostIsCapturedAsText()
    {
        using var kernel = new PowerShellKernel();
        var sink = new TestSink();

        await kernel.ExecuteAsync("Write-Host 'hello'", sink, default);

        Assert.False(sink.HasError);
        Assert.Contains("hello", sink.Texts);
    }

    [Fact]
    public async Task WriteWarningIsCapturedAsText()
    {
        using var kernel = new PowerShellKernel();
        var sink = new TestSink();

        await kernel.ExecuteAsync("Write-Warning 'careful'", sink, default);

        Assert.False(sink.HasError);
        Assert.Contains(sink.Texts, t => t.Contains("careful", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DivideByZeroBecomesWriteError()
    {
        using var kernel = new PowerShellKernel();
        var sink = new TestSink();

        await kernel.ExecuteAsync("1 / 0", sink, default);

        Assert.True(sink.HasError);
    }

    [Fact]
    public async Task ParseErrorBecomesWriteError()
    {
        using var kernel = new PowerShellKernel();
        var sink = new TestSink();

        await kernel.ExecuteAsync("if (", sink, default);

        Assert.True(sink.HasError);
    }

    [Fact]
    public async Task InjectedVariableIsUsableInALaterCell()
    {
        using var kernel = new PowerShellKernel();
        var sink = new TestSink();

        await kernel.SetVariableAsync("weather", new Weather(), default);
        await kernel.ExecuteAsync("$weather.City", sink, default);
        await kernel.ExecuteAsync("$weather.TempC(3)", sink, default);

        Assert.False(sink.HasError);
        Assert.Contains("Trento", sink.Texts);
        Assert.Contains("23", sink.Texts);
    }
}
