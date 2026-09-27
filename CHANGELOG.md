# Changelog

## 0.2.0 — rename to JupyterNet, plugin split, F#

- Renamed KerNet → **JupyterNet** throughout (namespaces, `PackageId`s, the extension's id/commands/
  settings, docs) — including the repo folder itself
  (`D:\Dev\2026\repos\KerNet` → `D:\Dev\2026\repos\JupyterNet`).
- `pysharp`/`ontly`/`ralf` moved out of this repo into their own engine's repo as kernel plugins
  (`JupyterNet.Kernels.{PySharp,Ontly,Ralf}` under PySharp/ontly/RalfAI's own `src/`), each
  implementing a new `IKernelPlugin` contract in `JupyterNet.Kernels.Abstractions`.
  `JupyterNet.Host` discovers and loads them at runtime (`KernelPluginLoader.cs`) from
  `JUPYTERNET_KERNEL_PATHS`/`kernels/*`, via `Assembly.LoadFrom` and its own-folder dependency
  probing — see ARCHITECTURE.md for why a per-plugin `AssemblyLoadContext` was tried first and
  reverted after it broke plugin loading whenever a builtin kernel ran first.
- Added a builtin **`fsharp`** kernel (`FSharp.Compiler.Service`'s `FsiEvaluationSession`) — a real
  FSI session, so cross-cell state persistence needs no workaround the way PySharp's does.
  VB.NET was considered and dropped: Roslyn has no scripting/REPL API for VB shaped like
  `CSharpScript`. FSI's own `val x: T = ...` echo is discarded (`TextWriter.Null`) rather than
  shown — a cell's output is exactly what it printed, plus the last expression's value.
- `vscode-extension`: added `jupyternet.kernelPaths` (forwarded to the host as
  `JUPYTERNET_KERNEL_PATHS`), `fsharp` in `supportedLanguages`; `build/package-extension.ps1` now
  also publishes the three plugins into `vscode-extension/host/kernels/*` for a self-contained
  `.vsix`.
- Fixed the notebook HTML renderer failing with "exports is not defined": it was compiled as
  CommonJS like the rest of the extension, but the renderer webview loads its entrypoint as a real
  ES module — split into its own `tsconfig.renderer.json`/`tsc` pass.

## 0.1.0 — initial version

- `JupyterNet.Host`: NDJSON stdio loop dispatching to four kernels.
- `csharp` kernel: Roslyn scripting, session state across cells, `Display.Html`/`Display.Text`.
- `pysharp` kernel: `PySharpLib.PyEngine`, session state carried forward by hand across cells,
  `display_html` callback.
- `ontly` kernel: `Ontly.Domain.ContractCompiler` against the C# and Python generators, rendered as
  an HTML report; diagnostics rendered as cell errors.
- `ralf` kernel: an in-process RalfAI `AgentEngine` with a `JupyterNetNotebookContext` exposing
  `Notebook_ListCells`/`GetCell`/`RunCell`/`SetCellCode` tools.
- VS Code extension: `jupyternet-notebook` type for `*.ipynb` files (real Jupyter nbformat v4, offered
  as an "option" rather than taking over every `.ipynb`), one multi-language `NotebookController`,
  a `text/html` output renderer, `JupyterNet: New Notebook` / `JupyterNet: Restart Kernel Host` commands.
- Preliminary change in the RalfAI repo: packaged `RalfAI.Core` and `RalfAI.Abstractions` to
  `D:\dev\NuGetLocalFeed` (metadata-only — `PackageId`/`Version`/`Description` — no logic changes),
  so JupyterNet could reference them the same way it references PySharp/Ontly.
