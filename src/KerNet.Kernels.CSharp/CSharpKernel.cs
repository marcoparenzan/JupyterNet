using KerNet.Kernels.Abstractions;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;

namespace KerNet.Kernels.CSharp;

/// <summary>
/// Runs C# cells with Roslyn scripting. Each cell continues the previous
/// <see cref="ScriptState{T}"/>, so top-level variables declared in one cell are visible in the
/// next — the same "REPL" feel dotnet-interactive's C# kernel has.
/// </summary>
public sealed class CSharpKernel : IKernel
{
    public string Id => "csharp";

    private ScriptState<object?>? _state;

    private static readonly ScriptOptions Options = ScriptOptions.Default
        .WithReferences(typeof(object).Assembly, typeof(Enumerable).Assembly, typeof(Display).Assembly)
        .WithImports("System", "System.Linq", "System.Collections.Generic", "KerNet.Kernels.CSharp");

    public async Task ExecuteAsync(string code, IKernelOutputSink sink, CancellationToken cancellationToken)
    {
        Display.CurrentSink.Value = sink;
        try
        {
            _state = _state is null
                ? await CSharpScript.RunAsync(code, Options, cancellationToken: cancellationToken)
                : await _state.ContinueWithAsync(code, Options, cancellationToken: cancellationToken);

            if (_state.ReturnValue is not null)
                sink.WriteText(_state.ReturnValue is string s ? s : _state.ReturnValue.ToString() ?? "");
        }
        catch (CompilationErrorException ex)
        {
            sink.WriteError(string.Join(Environment.NewLine, ex.Diagnostics));
        }
        catch (Exception ex)
        {
            sink.WriteError(ex.Message, ex.StackTrace);
        }
        finally
        {
            Display.CurrentSink.Value = null;
        }
    }
}
