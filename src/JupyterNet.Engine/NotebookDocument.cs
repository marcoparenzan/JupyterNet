using System.Text.Json;
using System.Text.Json.Nodes;

namespace JupyterNet.Engine;

/// <summary>One output a cell produced, in nbformat's own shape (only the two kinds JupyterNet kernels ever emit).</summary>
public sealed class NotebookOutput
{
    public required string OutputType { get; init; } // "display_data" | "error"
    public IReadOnlyDictionary<string, string>? Data { get; init; } // mime -> text, for "display_data"
    public string? ErrorName { get; init; }
    public string? ErrorMessage { get; init; }
    public string? ErrorTraceback { get; init; }

    public static NotebookOutput Display(string mimeType, string text) =>
        new() { OutputType = "display_data", Data = new Dictionary<string, string> { [mimeType] = text } };

    public static NotebookOutput Error(string message, string? traceback = null) =>
        new() { OutputType = "error", ErrorName = "Error", ErrorMessage = message, ErrorTraceback = traceback };
}

/// <summary>One notebook cell. <see cref="Language"/> is meaningful only for <c>"code"</c> cells.</summary>
public sealed class NotebookCell
{
    public int Index { get; set; }
    public string Kind { get; set; } = "code"; // "code" | "markdown"
    public string Language { get; set; } = "csharp";
    public string Source { get; set; } = "";
    public List<NotebookOutput> Outputs { get; set; } = [];
}

/// <summary>
/// A minimal C# reader/writer for real Jupyter nbformat v4 (<c>.ipynb</c>) — the same shape
/// <c>vscode-extension/src/notebookSerializer.ts</c> implements for the VS Code side. Per-cell
/// language (nbformat has no field for one language per cell, only per-notebook) lives under each
/// cell's <c>metadata.vscode.languageId</c>, the convention VS Code's own built-in notebook tooling
/// uses for the same gap — kept identical here so a notebook round-trips the same way whichever
/// side (extension or this library) last saved it.
/// </summary>
public sealed class NotebookDocument
{
    private const string DefaultCodeLanguage = "csharp";

    public List<NotebookCell> Cells { get; } = [];

    public IEnumerable<NotebookCell> CodeCells => Cells.Where(c => c.Kind == "code");

    public static NotebookDocument Load(string path) => Parse(File.ReadAllText(path));

    public static NotebookDocument Parse(string json)
    {
        var document = new NotebookDocument();
        if (string.IsNullOrWhiteSpace(json)) return document;

        var root = JsonNode.Parse(json)?.AsObject();
        var cells = root?["cells"]?.AsArray();
        if (cells is null) return document;

        var index = 0;
        foreach (var node in cells)
        {
            if (node is not JsonObject cell) continue;
            var cellType = (string?)cell["cell_type"] ?? "code";
            var isMarkdown = cellType == "markdown";
            var language = isMarkdown ? "markdown" : (string?)cell["metadata"]?["vscode"]?["languageId"] ?? DefaultCodeLanguage;

            document.Cells.Add(new NotebookCell
            {
                Index = index++,
                Kind = isMarkdown ? "markdown" : "code",
                Language = language,
                Source = JoinSource(cell["source"]),
                Outputs = (cell["outputs"] as JsonArray)?.Select(ParseOutput).Where(o => o is not null).Select(o => o!).ToList() ?? []
            });
        }
        return document;
    }

    public void Save(string path) => File.WriteAllText(path, ToJson());

    public string ToJson()
    {
        var cellsArray = new JsonArray();
        foreach (var cell in Cells)
        {
            var isMarkdown = cell.Kind == "markdown";
            var cellObject = new JsonObject
            {
                ["cell_type"] = isMarkdown ? "markdown" : "code",
                ["source"] = SplitSource(cell.Source),
                ["metadata"] = isMarkdown ? new JsonObject() : new JsonObject { ["vscode"] = new JsonObject { ["languageId"] = cell.Language } }
            };
            if (!isMarkdown)
            {
                cellObject["outputs"] = new JsonArray(cell.Outputs.Select(o => (JsonNode)SerializeOutput(o)).ToArray());
                cellObject["execution_count"] = null;
            }
            cellsArray.Add(cellObject);
        }

        var root = new JsonObject
        {
            ["cells"] = cellsArray,
            ["metadata"] = new JsonObject
            {
                ["kernelspec"] = new JsonObject { ["name"] = "jupyternet", ["display_name"] = "JupyterNet", ["language"] = "jupyternet" },
                ["language_info"] = new JsonObject { ["name"] = "jupyternet" }
            },
            ["nbformat"] = 4,
            ["nbformat_minor"] = 5
        };
        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n";
    }

    /// <summary>nbformat's <c>source</c>/<c>text</c> convention: an array of lines, each keeping its own trailing "\n" except possibly the last.</summary>
    private static JsonArray SplitSource(string value)
    {
        var array = new JsonArray();
        if (value.Length == 0) return array;
        foreach (var line in System.Text.RegularExpressions.Regex.Split(value, "(?<=\n)"))
            if (line.Length > 0) array.Add(line);
        return array;
    }

    private static string JoinSource(JsonNode? source) => source switch
    {
        JsonArray array => string.Concat(array.Select(n => (string?)n ?? "")),
        JsonValue value => (string?)value ?? "",
        _ => ""
    };

    private static NotebookOutput? ParseOutput(JsonNode? node)
    {
        if (node is not JsonObject output) return null;
        var type = (string?)output["output_type"];

        if (type == "error")
        {
            var traceback = (output["traceback"] as JsonArray)?.Select(n => (string?)n ?? "");
            return new NotebookOutput
            {
                OutputType = "error",
                ErrorName = (string?)output["ename"] ?? "Error",
                ErrorMessage = (string?)output["evalue"] ?? "",
                ErrorTraceback = traceback is null ? null : string.Concat(traceback)
            };
        }

        if (type == "stream")
        {
            return NotebookOutput.Display("text/plain", JoinSource(output["text"]));
        }

        var data = (output["data"] as JsonObject)?.ToDictionary(kv => kv.Key, kv => JoinSource(kv.Value));
        return new NotebookOutput { OutputType = "display_data", Data = data };
    }

    private static JsonObject SerializeOutput(NotebookOutput output)
    {
        if (output.OutputType == "error")
        {
            return new JsonObject
            {
                ["output_type"] = "error",
                ["ename"] = output.ErrorName ?? "Error",
                ["evalue"] = output.ErrorMessage ?? "",
                ["traceback"] = output.ErrorTraceback is null ? new JsonArray() : SplitSource(output.ErrorTraceback)
            };
        }

        var data = new JsonObject();
        if (output.Data is not null)
            foreach (var (mime, text) in output.Data) data[mime] = SplitSource(text);
        return new JsonObject { ["output_type"] = "display_data", ["data"] = data, ["metadata"] = new JsonObject() };
    }
}
