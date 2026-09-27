using System.Collections.ObjectModel;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using JupyterNet.Kernels.Abstractions;

namespace JupyterNet.Kernels.PowerShell;

/// <summary>
/// Runs PowerShell cells on a single embedded <see cref="Runspace"/> (<c>Microsoft.PowerShell.SDK</c>
/// — real PowerShell 7+, not Windows PowerShell 5.1). Session state persistence is what a
/// <see cref="Runspace"/> already does by construction: a fresh <see cref="System.Management.Automation.PowerShell"/>
/// pipeline is created per cell (that's how the API is meant to be used — one pipeline per
/// invocation), but they all run against the *same* runspace, so <c>$x</c> set in one cell is still
/// there in the next, same as PySharp/F#.
/// </summary>
public sealed class PowerShellKernel : IKernel, IVariableInjectable, IDisposable
{
    public string Id => "powershell";

    private readonly Runspace _runspace;

    public PowerShellKernel()
    {
        _runspace = RunspaceFactory.CreateRunspace();
        _runspace.Open();
    }

    public async Task ExecuteAsync(string code, IKernelOutputSink sink, CancellationToken cancellationToken)
    {
        using var pipeline = System.Management.Automation.PowerShell.Create();
        pipeline.Runspace = _runspace;
        pipeline.AddScript(code);

        using var cancellation = cancellationToken.Register(() => pipeline.Stop());

        Collection<PSObject> results;
        try
        {
            results = await Task.Run(() => pipeline.Invoke(), cancellationToken);
        }
        catch (Exception ex)
        {
            // A terminating error (e.g. a parse error) throws instead of landing in the Error stream.
            sink.WriteError(ex.Message, ex.StackTrace);
            return;
        }

        // Write-Host (PowerShell 7+ routes it through the Information stream, tagged "PSHOST") and
        // Write-Warning both read naturally as plain output; genuine pipeline results (the "value"
        // of the cell, mirroring the C#/F# kernels' "last expression" convention) come after.
        foreach (var record in pipeline.Streams.Information)
            sink.WriteText(record.ToString());
        foreach (var record in pipeline.Streams.Warning)
            sink.WriteText($"WARNING: {record}");
        foreach (var result in results)
            if (result is not null) sink.WriteText(result.ToString());

        if (pipeline.Streams.Error.Count > 0)
            sink.WriteError(string.Join(Environment.NewLine, pipeline.Streams.Error.Select(e => e.ToString())));
    }

    /// <summary>PowerShell's own, real API for exactly this — <c>$name</c> then works like any other session variable, with real member/method access on it (no unwrapping needed, same as F#'s AddBoundValue).</summary>
    public Task SetVariableAsync(string name, object? value, CancellationToken cancellationToken)
    {
        _runspace.SessionStateProxy.SetVariable(name, value);
        return Task.CompletedTask;
    }

    public void Dispose() => _runspace.Dispose();
}
