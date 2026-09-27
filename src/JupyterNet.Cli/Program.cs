using JupyterNet.Engine;
using JupyterNet.Kernels.Abstractions;

if (args.Length == 0 || args[0] != "run")
{
    PrintUsage();
    return 1;
}

string? notebookPath = null;
string? kernelPathsArg = null;
string? outputPath = null;
var inPlace = false;
var failFast = false;

for (var i = 1; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--kernel-paths" when i + 1 < args.Length: kernelPathsArg = args[++i]; break;
        case "--output" when i + 1 < args.Length: outputPath = args[++i]; break;
        case "--in-place": inPlace = true; break;
        case "--fail-fast": failFast = true; break;
        default:
            if (notebookPath is null) { notebookPath = args[i]; break; }
            Console.Error.WriteLine($"Unrecognized argument: {args[i]}");
            PrintUsage();
            return 1;
    }
}

if (notebookPath is null) { PrintUsage(); return 1; }
if (!File.Exists(notebookPath)) { Console.Error.WriteLine($"Notebook not found: {notebookPath}"); return 1; }

var document = NotebookDocument.Load(notebookPath);
var kernelPaths = kernelPathsArg?.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
var session = new NotebookSession(pluginWarnings: Console.Error, kernelPathsOverride: kernelPaths);

// A "ralf" cell's Notebook_SetCellCode tool reaches here — no editor to apply a WorkspaceEdit to,
// so it just updates this run's in-memory NotebookDocument directly.
session.CellEditRequested += (index, newCode) =>
{
    var target = document.Cells.FirstOrDefault(c => c.Index == index);
    if (target is not null) target.Source = newCode;
};

session.UpdateCells(document.CodeCells.Select(c => new NotebookCellInfo(c.Index, c.Language, c.Source)).ToList());

var overallOk = true;
foreach (var cell in document.CodeCells)
{
    Console.WriteLine($"=== [{cell.Index}] {cell.Language} ===");
    var sink = new ConsoleOutputSink();
    var ok = await session.ExecuteCellAsync(cell.Index, sink, CancellationToken.None);
    cell.Outputs = sink.Outputs;
    overallOk = overallOk && ok;
    if (!ok && failFast) break;
}

if (inPlace) document.Save(notebookPath);
else if (outputPath is not null) document.Save(outputPath);

return overallOk ? 0 : 1;

static void PrintUsage()
{
    Console.Error.WriteLine("""
        jupyternet run <notebook.ipynb> [--kernel-paths p1;p2;p3] [--output <file>] [--in-place] [--fail-fast]

          --kernel-paths  Kernel plugin directories (OS path-list), overriding JUPYTERNET_KERNEL_PATHS.
          --output <file> Write the executed notebook (with outputs) to <file>; the input is left untouched.
          --in-place      Write the executed notebook (with outputs) back to <notebook.ipynb>.
          --fail-fast     Stop at the first cell whose output includes an error, instead of running the rest.

        Exit code: 0 if every cell ran without error, 1 otherwise.
        """);
}

/// <summary>Prints a cell's output to the console as it happens, and records it (in nbformat's own shape) for <c>--output</c>/<c>--in-place</c>.</summary>
internal sealed class ConsoleOutputSink : IKernelOutputSink
{
    public List<NotebookOutput> Outputs { get; } = [];

    public void WriteText(string text)
    {
        Console.WriteLine(text);
        Outputs.Add(NotebookOutput.Display("text/plain", text));
    }

    public void WriteHtml(string html)
    {
        Console.WriteLine(html);
        Outputs.Add(NotebookOutput.Display("text/html", html));
    }

    public void WriteError(string message, string? stackTrace = null)
    {
        Console.Error.WriteLine(message);
        if (stackTrace is not null) Console.Error.WriteLine(stackTrace);
        Outputs.Add(NotebookOutput.Error(message, stackTrace));
    }
}
