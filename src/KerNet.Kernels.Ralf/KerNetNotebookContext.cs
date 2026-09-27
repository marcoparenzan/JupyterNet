using KerNet.Kernels.Abstractions;
using Microsoft.Extensions.AI;
using RalfAI.Contexts;

namespace KerNet.Kernels.Ralf;

/// <summary>
/// The <see cref="IAgentContext"/> Ralf runs under when invoked as the "ralf" cell language: its
/// tools are closures over the current <see cref="INotebookHost"/>, so the agent can see, run and
/// edit the notebook's other cells instead of only ever answering in text.
/// </summary>
internal sealed class KerNetNotebookContext(INotebookHost notebook) : IAgentContext
{
    public string Name => "KerNet Notebook";

    public string Description =>
        "A KerNet .NET notebook open in VS Code — inspect, run and edit its C#/PySharp/Ontly cells.";

    public string BuildSystemPrompt(string targetFolder) => """
        You are Ralf, running as the "ralf" cell language inside a KerNet notebook in VS Code.
        The notebook mixes cells written in csharp (Roslyn scripting), pysharp (a from-scratch
        Python interpreter) and ontly (a YAML domain-contract compiler that emits C#/Python); your
        own cells are "ralf" prompts like this one, and the user is talking to you from one of them.

        Use Notebook_ListCells to see what is in the notebook, Notebook_GetCell to read one cell in
        full, Notebook_RunCell to execute a cell and see exactly what it printed or rendered, and
        Notebook_SetCellCode to rewrite a cell's source. When asked to fix or change another cell,
        always re-run it after editing to confirm it now works before telling the user it is done.
        """;

    public IEnumerable<AITool> GetTools(string targetFolder) =>
    [
        AIFunctionFactory.Create(ListCells, name: "Notebook_ListCells",
            description: "Lists every cell currently in the notebook: index, language, and a preview of its source."),
        AIFunctionFactory.Create(GetCell, name: "Notebook_GetCell",
            description: "Returns one cell's language and full source code by index."),
        AIFunctionFactory.Create(SetCellCode, name: "Notebook_SetCellCode",
            description: "Overwrites a cell's source code in the editor. Does not run it — call Notebook_RunCell afterwards."),
        AIFunctionFactory.Create(RunCell, name: "Notebook_RunCell",
            description: "Executes a cell through its own kernel (csharp/pysharp/ontly/ralf) and returns what it printed or rendered.")
    ];

    private string ListCells() => notebook.Cells.Count == 0
        ? "The notebook has no cells."
        : string.Join("\n", notebook.Cells.Select(c => $"[{c.Index}] {c.Language}: {Preview(c.Code)}"));

    private string GetCell(int index)
    {
        var cell = notebook.Cells.FirstOrDefault(c => c.Index == index);
        return cell is null ? $"No cell at index {index}." : $"[{cell.Index}] {cell.Language}\n{cell.Code}";
    }

    private string SetCellCode(int index, string code)
    {
        if (notebook.Cells.All(c => c.Index != index)) return $"No cell at index {index}.";
        notebook.RequestCellEdit(index, code);
        return $"Cell {index} updated.";
    }

    private async Task<string> RunCell(int index, CancellationToken cancellationToken)
    {
        if (notebook.Cells.All(c => c.Index != index)) return $"No cell at index {index}.";
        return await notebook.RunCellAsync(index, cancellationToken);
    }

    private static string Preview(string code) => code.Length <= 80
        ? code.Replace('\n', ' ')
        : code[..80].Replace('\n', ' ') + "…";
}
