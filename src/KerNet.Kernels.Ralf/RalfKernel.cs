using KerNet.Kernels.Abstractions;
using Markdig;
using RalfAI;

namespace KerNet.Kernels.Ralf;

/// <summary>
/// Runs "ralf" cells by forwarding the cell's text as a prompt to a RalfAI <see cref="AgentEngine"/>,
/// built lazily (and reused for the rest of the session) around a <see cref="KerNetNotebookContext"/>
/// so the agent can act on the notebook's other cells. Uses the same config shape as RalfAI's own
/// CLI (<see cref="RalfAIConfig"/>) — an existing <c>RALFAI_CONFIG_PATH</c> or <c>appsettings.json</c>
/// works here unchanged.
/// </summary>
public sealed class RalfKernel(INotebookHost notebook) : IKernel
{
    public string Id => "ralf";

    private AgentEngine? _agent;

    public async Task ExecuteAsync(string code, IKernelOutputSink sink, CancellationToken cancellationToken)
    {
        AgentEngine agent;
        try
        {
            agent = _agent ??= await AgentEngine.CreateAsync(
                RalfAIConfig.Load(),
                targetFolder: Directory.GetCurrentDirectory(),
                contextKey: "kernet",
                context: new KerNetNotebookContext(notebook),
                ct: cancellationToken);
        }
        catch (Exception ex)
        {
            sink.WriteError(
                $"Could not start Ralf: {ex.Message}",
                "Set RALFAI_CONFIG_PATH to an existing RalfAI config.json, or place an " +
                "appsettings.json next to KerNet.Host with the same AIProvider/OpenAI|Anthropic|" +
                "AzureAIFoundry settings RalfAI's own CLI uses.");
            return;
        }

        void OnProgress(TurnProgress p) => sink.WriteText($"… {p.Message}");
        void OnNotice(AgentNotice n) => sink.WriteText($"[{n.Level}] {n.Message}");
        void OnToolStart(ToolCallStart t) => sink.WriteText($"→ {t.Name}({t.ArgsPreview})");
        void OnToolDone(ToolCallResult t) => sink.WriteText($"← {t.Name}: {Truncate(t.Result)}");

        agent.Progress += OnProgress;
        agent.Notice += OnNotice;
        agent.ToolCallStarted += OnToolStart;
        agent.ToolCallCompleted += OnToolDone;
        try
        {
            var result = await agent.SendAsync(code, cancellationToken);
            if (result.Error is not null)
            {
                sink.WriteError(result.Error);
                return;
            }

            sink.WriteHtml(Markdown.ToHtml(result.FinalText ?? "_(no final answer)_"));
        }
        finally
        {
            agent.Progress -= OnProgress;
            agent.Notice -= OnNotice;
            agent.ToolCallStarted -= OnToolStart;
            agent.ToolCallCompleted -= OnToolDone;
        }
    }

    private static string Truncate(string text) => text.Length <= 200 ? text : text[..200] + "…";
}
