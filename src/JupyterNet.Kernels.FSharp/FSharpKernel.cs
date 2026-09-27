using JupyterNet.Kernels.Abstractions;
using Microsoft.FSharp.Core;
using static FSharp.Compiler.Interactive.Shell;

namespace JupyterNet.Kernels.FSharp;

/// <summary>
/// Runs F# cells on a single <see cref="FsiEvaluationSession"/> — a real, long-lived FSI/REPL
/// session, so (unlike PySharp's <c>PyEngine</c>) cross-cell variable persistence is simply what
/// the session already does; there is no "copy globals forward" workaround needed here.
/// </summary>
public sealed class FSharpKernel : IKernel, IVariableInjectable
{
    public string Id => "fsharp";

    private readonly FsiEvaluationSession _session;
    private readonly StringWriter _stdout = new();
    private readonly StringWriter _stderr = new();

    public FSharpKernel()
    {
        // FsiEvaluationSession.Create's own outWriter/errorWriter are where FSI writes its own
        // REPL echo ("val x: int = 21" for every top-level binding/expression — genuine FSI
        // behavior, the same thing `dotnet fsi` shows in a terminal). `Settings.fsi.
        // ShowDeclarationValues = false` looked like the "real" switch for that but turned out
        // inconsistent in practice (it drops the value from a `let` binding's echo but not from an
        // expression's `it` binding). Discarding FSI's own writers entirely is simpler and
        // complete: what a cell "printed" is defined as what `printfn`/`Console.Write` wrote via
        // the *real* `Console.Out`, captured below by redirecting it — never what FSI echoes.
        var config = FsiEvaluationSession.GetDefaultConfiguration();
        _session = FsiEvaluationSession.Create(
            config,
            ["fsi.exe", "--noninteractive", "--nologo", "--gui-"],
            new StringReader(""),
            TextWriter.Null,
            TextWriter.Null,
            collectible: null,
            legacyReferenceResolver: null);
    }

    public Task ExecuteAsync(string code, IKernelOutputSink sink, CancellationToken cancellationToken)
    {
        _stdout.GetStringBuilder().Clear();
        _stderr.GetStringBuilder().Clear();

        // `printfn`/`Console.Write` inside the evaluated code target the real process Console.Out
        // (that's what `printfn` resolves to), which is also JupyterNet.Host's own NDJSON stdout.
        // So Console.Out/Error are redirected here for the duration of exactly this one call and
        // restored immediately after, never left swapped for other kernels/cells.
        var previousOut = Console.Out;
        var previousError = Console.Error;
        Tuple<FSharpChoice<FSharpOption<FsiValue>?, Exception>, global::FSharp.Compiler.Diagnostics.FSharpDiagnostic[]> evaluation;
        try
        {
            Console.SetOut(_stdout);
            Console.SetError(_stderr);
            evaluation = _session.EvalInteractionNonThrowing(code, null);
        }
        finally
        {
            Console.SetOut(previousOut);
            Console.SetError(previousError);
        }

        var outcome = evaluation.Item1;
        var diagnostics = evaluation.Item2;

        if (outcome is FSharpChoice<FSharpOption<FsiValue>?, Exception>.Choice2Of2 failure)
        {
            sink.WriteError(failure.Item.Message, failure.Item.StackTrace);
            return Task.CompletedTask;
        }

        // Captured stdout is exactly what the cell itself printed (printfn/Console.Write) — FSI's
        // own echo went to a discarded writer.
        var text = _stdout.ToString();
        if (text.Length > 0) sink.WriteText(text);

        // A `let` binding has no value here (None); an expression does (Some) — mirrors the C#
        // kernel's "last expression's value becomes the cell's result" behavior.
        if (outcome is FSharpChoice<FSharpOption<FsiValue>?, Exception>.Choice1Of2 { Item: { } option } &&
            option.Value.ReflectionValue is { } value)
        {
            sink.WriteText(value.ToString() ?? "");
        }

        var errorText = _stderr.ToString();
        if (errorText.Length > 0) sink.WriteText(errorText);

        if (diagnostics.Length > 0)
            sink.WriteText(string.Join(Environment.NewLine, diagnostics.Select(d => d.ToString())));

        return Task.CompletedTask;
    }

    /// <summary>
    /// FSI's own, real API for exactly this — the bound name behaves like any other top-level FSI
    /// value afterwards (usable, reassignable, visible to <c>GetBoundValues</c>).
    /// </summary>
    public Task SetVariableAsync(string name, object? value, CancellationToken cancellationToken)
    {
        _session.AddBoundValue(name, value!);
        return Task.CompletedTask;
    }
}
