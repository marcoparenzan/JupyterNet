# Changelog

## 0.1.0 — initial version

- `KerNet.Host`: NDJSON stdio loop dispatching to four kernels.
- `csharp` kernel: Roslyn scripting, session state across cells, `Display.Html`/`Display.Text`.
- `pysharp` kernel: `PySharpLib.PyEngine`, session state carried forward by hand across cells,
  `display_html` callback.
- `ontly` kernel: `Ontly.Domain.ContractCompiler` against the C# and Python generators, rendered as
  an HTML report; diagnostics rendered as cell errors.
- `ralf` kernel: an in-process RalfAI `AgentEngine` with a `KerNetNotebookContext` exposing
  `Notebook_ListCells`/`GetCell`/`RunCell`/`SetCellCode` tools.
- VS Code extension: `kernet-notebook` type for `*.ipynb` files (real Jupyter nbformat v4, offered
  as an "option" rather than taking over every `.ipynb`), one multi-language `NotebookController`,
  a `text/html` output renderer, `KerNet: New Notebook` / `KerNet: Restart Kernel Host` commands.
- Preliminary change in the RalfAI repo: packaged `RalfAI.Core` and `RalfAI.Abstractions` to
  `D:\dev\NuGetLocalFeed` (metadata-only — `PackageId`/`Version`/`Description` — no logic changes),
  so KerNet could reference them the same way it references PySharp/Ontly.
