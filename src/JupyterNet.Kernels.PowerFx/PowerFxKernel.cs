using System.Text.RegularExpressions;
using JupyterNet.Kernels.Abstractions;
using Microsoft.PowerFx;
using Microsoft.PowerFx.Types;

namespace JupyterNet.Kernels.PowerFx;

/// <summary>
/// Evaluates Power Fx formulas on a single <see cref="RecalcEngine"/> per session. Unlike the other
/// builtin kernels, a cell here is *one formula*, not a script of statements — that's the grain
/// Power Fx itself works at (the same thing a Power Apps formula bar evaluates).
/// </summary>
public sealed class PowerFxKernel : IKernel, IVariableInjectable
{
    public string Id => "powerfx";

    private readonly RecalcEngine _engine = CreateEngine();
    private readonly TypeMarshallerCache _marshaller = new();
    private readonly HashSet<string> _knownNames = new(StringComparer.Ordinal);

    // Formula syntax itself is locale-sensitive (a comma-decimal locale, e.g. it-IT, expects `;` as
    // the function-argument separator instead of `,`, to avoid ambiguity with decimal numbers) —
    // pinning the invariant culture means a notebook parses the same way on every machine,
    // regardless of the host process's own culture (found by actually hitting it: the same formula
    // that works on an en-US machine fails to parse at all on this one otherwise).
    private static readonly ParserOptions Options = new(System.Globalization.CultureInfo.InvariantCulture) { AllowsSideEffects = true };

    // A whole cell that is exactly `Set(name, expression)` — the common "assign a session variable"
    // case, and the one this kernel implements itself rather than delegating to Power Fx's own Set
    // function. Power Fx's Set() can only *update* a name the engine already knows the type of; it
    // rejects a brand-new name at bind time ("Name isn't valid") before Set's own logic even runs,
    // and pre-declaring the name as an untyped Blank first (the first thing tried) just moves the
    // failure to "Invalid argument type (Decimal). Expecting a Blank value instead." — a Power Fx
    // variable's type is fixed for good at its first UpdateVariable call. So: evaluate the *value*
    // expression on its own, then UpdateVariable(name, thatValue) directly — which both declares a
    // brand-new name (with the right type, taken from the value itself) and updates an existing one,
    // with no native-Set binder involved either way. Both findings came from actually running this
    // against the real engine, not from any Power Fx documentation.
    private static readonly Regex TopLevelSet = new(@"^\s*Set\(\s*([A-Za-z_][A-Za-z0-9_]*)\s*,\s*(.*)\)\s*$", RegexOptions.Compiled | RegexOptions.Singleline);

    private static RecalcEngine CreateEngine()
    {
        var config = new PowerFxConfig();
        config.EnableSetFunction(); // nested/advanced Set() usage against an already-declared name can still go through Power Fx's own function
        return new RecalcEngine(config);
    }

    public Task ExecuteAsync(string code, IKernelOutputSink sink, CancellationToken cancellationToken)
    {
        var setMatch = TopLevelSet.Match(code);
        return setMatch.Success
            ? ExecuteSetAsync(setMatch.Groups[1].Value, setMatch.Groups[2].Value, sink)
            : ExecuteFormulaAsync(code, sink);
    }

    private Task ExecuteSetAsync(string name, string valueExpression, IKernelOutputSink sink)
    {
        if (!TryEval(valueExpression, sink, out var value)) return Task.CompletedTask;
        _engine.UpdateVariable(name, value);
        _knownNames.Add(name);
        return Task.CompletedTask;
    }

    private Task ExecuteFormulaAsync(string code, IKernelOutputSink sink)
    {
        if (!TryEval(code, sink, out var result)) return Task.CompletedTask;

        // Void (e.g. a nested Set(...)/Collect(...)) has nothing to echo, same as a C# statement
        // with no value. Anything else is the formula's result, the cell's whole "output".
        if (result is not BlankValue and not VoidValue)
        {
            // Formatting, unlike parsing, isn't given a culture by Eval — plain ToObject()?.ToString()
            // silently goes through CultureInfo.CurrentCulture, so a Decimal/DateTime result prints
            // with this machine's own formatting (e.g. "59,97" on it-IT) despite every cell having
            // been *parsed* as invariant — found by actually running a formula with a decimal result.
            var text = result.ToObject() switch
            {
                null => null,
                IFormattable formattable => formattable.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
                var other => other.ToString()
            };
            if (!string.IsNullOrEmpty(text)) sink.WriteText(text);
        }

        return Task.CompletedTask;
    }

    private bool TryEval(string expression, IKernelOutputSink sink, out FormulaValue result)
    {
        try
        {
            result = _engine.Eval(expression, options: Options);
        }
        catch (Exception ex)
        {
            sink.WriteError(ex.Message, ex.StackTrace);
            result = FormulaValue.NewBlank();
            return false;
        }

        if (result is ErrorValue error)
        {
            sink.WriteError(string.Join(Environment.NewLine, error.Errors.Select(e => e.Message)));
            return false;
        }
        return true;
    }

    /// <summary>
    /// <see cref="RecalcEngine.UpdateVariable(string, FormulaValue)"/> for primitives;
    /// <see cref="TypeMarshallerCache.Marshal(object, Type)"/> (reflects an object's public
    /// properties into a record) for anything else. <b>Only properties</b> — Power Fx has no
    /// concept of calling an arbitrary CLR instance method on a value, by design (it's a closed
    /// formula language, not a scripting one), so <c>weather.City</c> works here the same as every
    /// other kernel but <c>weather.TempC(3)</c> does not; that's a real difference in what this
    /// kernel can do with an injected object, not a bug.
    /// </summary>
    public Task SetVariableAsync(string name, object? value, CancellationToken cancellationToken)
    {
        var formulaValue = value switch
        {
            null => FormulaValue.NewBlank(),
            double d => FormulaValue.New(d),
            decimal m => FormulaValue.New(m),
            int i => FormulaValue.New(i),
            long l => FormulaValue.New(l),
            float f => FormulaValue.New(f),
            string s => FormulaValue.New(s),
            bool b => FormulaValue.New(b),
            Guid g => FormulaValue.New(g),
            DateTime dt => FormulaValue.New(dt),
            TimeSpan ts => FormulaValue.New(ts),
            _ => _marshaller.Marshal(value, value.GetType())
        };
        _engine.UpdateVariable(name, formulaValue);
        _knownNames.Add(name);
        return Task.CompletedTask;
    }
}
