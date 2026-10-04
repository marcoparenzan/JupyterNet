using JupyterNet.Kernels.PowerFx;

namespace JupyterNet.Tests;

public class PowerFxKernelTests
{
    [Fact]
    public async Task SessionStatePersistsAcrossCells()
    {
        var kernel = new PowerFxKernel();
        var sink = new TestSink();

        await kernel.ExecuteAsync("Set(x, 21)", sink, default);
        await kernel.ExecuteAsync("x * 2", sink, default);

        Assert.False(sink.HasError);
        Assert.Contains("42", sink.Texts);
    }

    [Fact]
    public async Task SetOnAnAlreadyKnownNameUpdatesIt()
    {
        var kernel = new PowerFxKernel();
        var sink = new TestSink();

        await kernel.ExecuteAsync("Set(x, 21)", sink, default);
        await kernel.ExecuteAsync("Set(x, x + 1)", sink, default);
        await kernel.ExecuteAsync("x", sink, default);

        Assert.False(sink.HasError);
        Assert.Contains("22", sink.Texts);
    }

    /// <summary>
    /// A Decimal result's text must not go through this machine's own culture: on an it-IT machine,
    /// plain `ToString()` prints "59,97" (comma decimal separator) despite the formula itself having
    /// been parsed as invariant — found by actually running this exact formula during this session.
    /// </summary>
    [Fact]
    public async Task DecimalResultsFormatInvariantly()
    {
        var kernel = new PowerFxKernel();
        var sink = new TestSink();

        await kernel.ExecuteAsync("Round(19.99 * 3, 2)", sink, default);

        Assert.False(sink.HasError);
        Assert.Contains("59.97", sink.Texts);
    }

    [Fact]
    public async Task StringFunctionsWork()
    {
        var kernel = new PowerFxKernel();
        var sink = new TestSink();

        await kernel.ExecuteAsync("Concatenate(\"hello \", \"PowerFx\")", sink, default);

        Assert.False(sink.HasError);
        Assert.Contains("hello PowerFx", sink.Texts);
    }

    /// <summary>
    /// Regression for a real production bug, not something this test's own process setting can
    /// reproduce on its own: `Microsoft.PowerFx.Core` builds its *error messages* (not just
    /// `Text()`'s custom-format path) through a `ResourceManager`/`CultureInfo.CreateSpecificCulture`
    /// call that throws outright under .NET's `InvariantGlobalization` runtime mode — turning a
    /// clean "division by zero" into a raw, confusing .NET exception. `JupyterNet.Host` and
    /// `JupyterNet.Cli` both had `&lt;InvariantGlobalization&gt;true&lt;/InvariantGlobalization&gt;`
    /// set (unrelated boilerplate from this repo's very first commit) until this was found by
    /// actually running a PowerFx error cell through the real host, not through this test project
    /// (which has no such setting and so never would have caught it) — removed from both .csproj
    /// files as the fix. This test only documents that a normal error still parses/formats fine;
    /// it cannot exercise the InvariantGlobalization interaction itself.
    /// </summary>
    [Fact]
    public async Task DivideByZeroBecomesWriteError()
    {
        var kernel = new PowerFxKernel();
        var sink = new TestSink();

        await kernel.ExecuteAsync("1/0", sink, default);

        Assert.True(sink.HasError);
    }

    [Fact]
    public async Task ParseErrorBecomesWriteError()
    {
        var kernel = new PowerFxKernel();
        var sink = new TestSink();

        await kernel.ExecuteAsync("this is not valid", sink, default);

        Assert.True(sink.HasError);
    }

    /// <summary>
    /// Power Fx only ever reflects an injected object's public *properties* into a record — there's
    /// no concept of calling a CLR instance method from a formula — so this only exercises
    /// `weather.City`, unlike the other kernels' injection test which also calls `weather.TempC(3)`.
    /// </summary>
    [Fact]
    public async Task InjectedVariablePropertyIsUsableInALaterCell()
    {
        var kernel = new PowerFxKernel();
        var sink = new TestSink();

        await kernel.SetVariableAsync("weather", new Weather(), default);
        await kernel.ExecuteAsync("weather.City", sink, default);
        await kernel.ExecuteAsync("Concatenate(\"City is \", weather.City)", sink, default);

        Assert.False(sink.HasError);
        Assert.Contains("Trento", sink.Texts);
        Assert.Contains("City is Trento", sink.Texts);
    }
}
