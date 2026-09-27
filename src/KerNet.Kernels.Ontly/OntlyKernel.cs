using System.Net;
using System.Text;
using KerNet.Kernels.Abstractions;
using Ontly.Domain;

namespace KerNet.Kernels.Ontly;

/// <summary>
/// A cell is a standalone Ontly YAML contract (no cross-cell <c>includes</c> in this version —
/// each cell is compiled on its own, the same shape as Ontly's own single-file examples).
/// Success renders a summary of the contract plus the generated C# and Python side by side;
/// failure renders the compiler's own diagnostics. Compilation is the real
/// <see cref="ContractCompiler"/>/generators Ontly ships — nothing here re-implements the format.
/// </summary>
public sealed class OntlyKernel : IKernel
{
    public string Id => "ontly";

    private readonly ContractCompiler _compiler = new();
    private readonly global::Ontly.CSharp.CSharpGenerator _csharpGenerator = new();
    private readonly global::Ontly.Python.PythonGenerator _pythonGenerator = new();

    public Task ExecuteAsync(string code, IKernelOutputSink sink, CancellationToken cancellationToken)
    {
        var csharpResult = _compiler.Compile(code, _csharpGenerator);
        if (!csharpResult.Success)
        {
            sink.WriteError(string.Join(Environment.NewLine,
                csharpResult.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
            return Task.CompletedTask;
        }

        var pythonResult = _compiler.Compile(code, _pythonGenerator);
        var resolution = new ContractLoader().Load($"<cell-{Guid.NewGuid():N}>.ontly.yaml", code);

        sink.WriteHtml(RenderHtml(resolution.Document, csharpResult.Code!, pythonResult.Code));
        return Task.CompletedTask;
    }

    private static string RenderHtml(ContractDocument? document, string csharp, string? python)
    {
        var html = new StringBuilder();
        html.Append("<div style=\"font-family:var(--vscode-font-family,sans-serif)\">");

        if (document is not null)
        {
            html.Append("<table style=\"border-collapse:collapse;margin-bottom:8px\">");
            AppendRow(html, "context", document.Context);
            AppendRow(html, "namespace", document.Namespace);
            AppendRow(html, "scalars", string.Join(", ", document.Scalars.Keys));
            AppendRow(html, "types", string.Join(", ", document.Types.Keys));
            AppendRow(html, "interfaces", string.Join(", ", document.Interfaces.Keys));
            AppendRow(html, "quantities", string.Join(", ", document.Quantities.Keys));
            html.Append("</table>");
        }

        html.Append("<div style=\"display:flex;gap:12px;flex-wrap:wrap\">");
        AppendCodeBlock(html, "C#", csharp);
        if (python is not null) AppendCodeBlock(html, "Python", python);
        html.Append("</div></div>");
        return html.ToString();
    }

    private static void AppendRow(StringBuilder html, string label, string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        html.Append("<tr><td style=\"padding:2px 8px;color:var(--vscode-descriptionForeground,#888)\">")
            .Append(WebUtility.HtmlEncode(label))
            .Append("</td><td style=\"padding:2px 8px\"><code>")
            .Append(WebUtility.HtmlEncode(value))
            .Append("</code></td></tr>");
    }

    private static void AppendCodeBlock(StringBuilder html, string title, string code)
    {
        html.Append("<div style=\"flex:1;min-width:280px\"><div style=\"font-weight:600;margin-bottom:4px\">")
            .Append(WebUtility.HtmlEncode(title))
            .Append("</div><pre style=\"background:var(--vscode-textCodeBlock-background,#1e1e1e);padding:8px;border-radius:4px;overflow:auto\"><code>")
            .Append(WebUtility.HtmlEncode(code))
            .Append("</code></pre></div>");
    }
}
