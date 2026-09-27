using JupyterNet.Kernels.Abstractions;
using Microsoft.FSharp.Core;
using static FSharp.Compiler.Interactive.Shell;

namespace JupyterNet.Kernels.FSharp;

/// <summary>
/// Runs F# cells on a single <see cref="FsiEvaluationSession"/> — a real, long-lived FSI/REPL
/// session, so (unlike PySharp's <c>PyEngine</c>) cross-cell variable persistence is simply what
/// the session already does; there is no "copy globals forward" workaround needed here.
/// </summary>
public sealed class FSharpKernel : IKernel
{
    public string Id => "fsharp";

    private readonly FsiEvaluationSession _session;
    private readonly StringWriter _stdout = new();
    private readonly StringWriter _stderr = new();

    public FSharpKernel()
    {
        var config = FsiEvaluationSession.GetDefaultConfiguration();
        _session = FsiEvaluationSession.Create(
            config,
            ["fsi.exe", "--noninteractive", "--nologo", "--gui-"],
            new StringReader(""),
            _stdout,
            _stderr,
            collectible: null,
            legacyReferenceResolver: null);
    }

    public Task ExecuteAsync(string code, IKernelOutputSink sink, CancellationToken cancellationToken)
    {
        _stdout.GetStringBuilder().Clear();
        _stderr.GetStringBuilder().Clear();

        // FsiEvaluationSession's outWriter/errorWriter only capture FSI's own echo ("val x: int =
        // 21") — plain `printfn`/`Console.Write` inside the evaluated code still targets the real
        // process Console.Out (that's what `printfn` resolves to), which is also JupyterNet.Host's
        // own NDJSON stdout. So Console.Out/Error are redirected here for the duration of exactly
        // this one call and restored immediately after, never left swapped for other kernels/cells.
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

        // FSI's own evaluation already echoes each binding ("val x: int = 21"), so the captured
        // text alone is the cell's output — no separate print of outcome's FsiValue needed.
        var text = _stdout.ToString();
        if (text.Length > 0) sink.WriteText(text);

        var errorText = _stderr.ToString();
        if (errorText.Length > 0) sink.WriteText(errorText);

        if (diagnostics.Length > 0)
            sink.WriteText(string.Join(Environment.NewLine, diagnostics.Select(d => d.ToString())));

        return Task.CompletedTask;
    }
}
