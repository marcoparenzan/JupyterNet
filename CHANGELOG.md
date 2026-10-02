# Changelog

## 0.5.0 — image output

- `IKernelOutputSink.WriteImage(mimeType, bytes)` — kernels can now emit real images. A default
  interface implementation falls back to an inline `<img>` data-URI through `WriteHtml`, so existing
  sinks and plugins keep working unchanged.
- Wire protocol: an `output` event may carry `"encoding": "base64"` with binary `data`
  (`image/png`, `image/jpeg`, ...); the extension turns it into a native `NotebookCellOutputItem`, so
  VS Code renders it without a custom renderer. `.ipynb` round-trips images as nbformat base64
  `image/png` entries (and loads them back as bytes).
- All packages (`Abstractions`, `Protocol`, `Engine`, `Host`, builtin kernels) bumped to 0.2.0.

## 0.4.0 — PowerShell kernel

- New builtin **`powershell`** kernel (`JupyterNet.Kernels.PowerShell`, `Microsoft.PowerShell.SDK`
  — real PowerShell 7+): one embedded `Runspace` per session, session state persists the same way
  the other builtin kernels' does; `Write-Host`/`Write-Warning` captured via `PowerShell.Streams`
  (no custom `PSHost` needed — PS7+ routes `Write-Host` through the Information stream
  specifically for this); variable injection via `Runspace.SessionStateProxy.SetVariable`, with
  real member/method access on the injected object, no unwrapping.
- Forced a real dependency bump: `Microsoft.PowerShell.SDK` needs
  `Microsoft.CodeAnalysis.CSharp >= 5.0.0`, conflicting with the C# kernel's pinned `4.14.0` via
  `Microsoft.CodeAnalysis.CSharp.Scripting` — moved to `5.9.0`; the C# kernel's own test suite
  re-verified passing after the bump, not just assumed compatible.
- `samples/tour.ipynb` gained two PowerShell cells; `jupyternet run`/the host/the extension all
  install/dispatch it identically to `csharp`/`fsharp`.

## 0.3.0 — headless CLI, embedding, variable injection, tests

- New `JupyterNet.Engine`: `NotebookDocument` (a C# nbformat v4 reader/writer — the first one in
  this repo outside the TypeScript extension), `NotebookSession` (kernel dispatch/plugin discovery/
  variable injection, pulled out of `JupyterNet.Host`'s `Program.cs`). `JupyterNet.Host` is now a
  thin NDJSON wrapper around it.
- New `JupyterNet.Kernels.Abstractions.IVariableInjectable`: an opt-in kernel capability for
  injecting a live .NET object into a kernel's context, reached via
  `NotebookSession.SetVariableAsync`. Implemented by both builtin kernels — F#'s via
  `FsiEvaluationSession.AddBoundValue` (real static-ish typing, no unwrapping needed), C#'s via a
  one-line hidden `dynamic x = Injected["x"];` interaction (Roslyn scripting has no runtime-
  extensible globals story otherwise).
- New `JupyterNet.Cli` (`jupyternet run <notebook.ipynb> [--kernel-paths] [--output|--in-place]
  [--fail-fast]`) — headless notebook execution, a minimal `nbconvert --execute` equivalent, exit
  code reflecting success.
- New `samples/EmbeddingSample` — a console app embedding `JupyterNet.Engine` directly (no process,
  no NDJSON) and injecting a `Weather` object into both builtin kernels.
- New `tests/JupyterNet.Tests` (xUnit): nbformat round-tripping, both builtin kernels (session
  persistence, output capture, error handling, variable injection), `NotebookSession` dispatch.
  Found two real issues while writing them: an injected object's type must be `public` (the DLR
  binder enforces real accessibility from the script's own separately-compiled assembly), and
  `FSharpKernel` is not safe under concurrent/parallel execution (it swaps the process-global
  `Console.Out`/`Error`) — xUnit's default test parallelization raced on it and flaked a test;
  fixed by disabling parallelization for this test assembly, and documented as a real constraint
  in ARCHITECTURE.md, not just a test workaround.

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
