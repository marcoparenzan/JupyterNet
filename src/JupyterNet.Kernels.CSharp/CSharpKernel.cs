using JupyterNet.Kernels.Abstractions;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;

namespace JupyterNet.Kernels.CSharp;

/// <summary>
/// Runs C# cells with Roslyn scripting. Each cell continues the previous
/// <see cref="ScriptState{T}"/>, so top-level variables declared in one cell are visible in the
/// next — the same "REPL" feel dotnet-interactive's C# kernel has.
/// </summary>
public sealed class CSharpKernel : IKernel, IVariableInjectable
{
    public string Id => "csharp";

    private ScriptState<object?>? _state;
    private readonly ScriptGlobals _globals = new();

    // Microsoft.CSharp.dll (the DLR runtime binder) is required for `dynamic` to compile in a
    // script submission — only ever exercised by SetVariableAsync's hidden "unwrap" interaction.
    private static readonly ScriptOptions Options = ScriptOptions.Default
        .WithReferences(typeof(object).Assembly, typeof(Enumerable).Assembly, typeof(Display).Assembly,
            typeof(Microsoft.CSharp.RuntimeBinder.Binder).Assembly)
        .WithImports("System", "System.Linq", "System.Collections.Generic", "JupyterNet.Kernels.CSharp");

    public async Task ExecuteAsync(string code, IKernelOutputSink sink, CancellationToken cancellationToken)
    {
        Display.CurrentSink.Value = sink;
        try
        {
            _state = await RunAsync(code, cancellationToken);

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

    /// <summary>
    /// The embedding entry point (<c>NotebookSession.SetVariableAsync</c>) reaches here. Roslyn
    /// scripting has no runtime-extensible "globals" story — the globals *type* is fixed on the
    /// very first <see cref="CSharpScript.RunAsync"/> call — but a real, script-visible binding is
    /// still achievable: the globals object carries one <see cref="ScriptGlobals.Injected"/>
    /// dictionary (a public member, so script code can reach it by bare name like any other global),
    /// and each newly-injected name gets a one-line hidden interaction —
    /// <c>dynamic weather = Injected["weather"];</c> — run exactly like a real cell would be, so it
    /// becomes an ordinary top-level binding for every cell after it, indistinguishable from
    /// something the notebook itself declared. `dynamic` sidesteps needing the injected object's
    /// exact (possibly non-public) type name at the call site; real member access/calls on it still
    /// resolve via the DLR when a later cell actually runs.
    /// </summary>
    public async Task SetVariableAsync(string name, object? value, CancellationToken cancellationToken)
    {
        if (!SyntaxFacts.IsValidIdentifier(name))
            throw new ArgumentException($"'{name}' is not a valid C# identifier.", nameof(name));

        _globals.Injected[name] = value;
        _state = await RunAsync($"dynamic {name} = Injected[\"{name}\"];", cancellationToken);
    }

    private Task<ScriptState<object?>> RunAsync(string code, CancellationToken cancellationToken) => _state is null
        ? CSharpScript.RunAsync(code, Options, _globals, cancellationToken: cancellationToken)
        : _state.ContinueWithAsync(code, Options, cancellationToken: cancellationToken);
}

/// <summary>The Roslyn scripting "globals" object for every C# cell in a session — its public members are reachable from script code by bare name.</summary>
public sealed class ScriptGlobals
{
    public Dictionary<string, object?> Injected { get; } = new();
}
