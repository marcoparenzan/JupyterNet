# Architecture

## Process shape

One `KerNet.Host` process per open `.kernet` notebook, spawned by the VS Code extension
(`KerNetController.getClient`, [vscode-extension/src/kernelController.ts](vscode-extension/src/kernelController.ts))
and killed when the notebook closes or "KerNet: Restart Kernel Host" is run. The extension and the
host talk NDJSON over stdio — see [docs/protocol.md](docs/protocol.md) for the exact messages.

Inside the host ([src/KerNet.Host/Program.cs](src/KerNet.Host/Program.cs)), a single `while` loop
reads one request at a time and awaits it fully before reading the next — there is no concurrent
execution of two cells in the same notebook. A dictionary maps kernel id → `IKernel` instance,
created lazily on first use and kept for the rest of the session; that instance *is* the cell's
session state (Roslyn's `ScriptState`, PySharp's `PyEngine.Globals`, the Ralf `AgentEngine`).

## Kernels

`IKernel`/`IKernelOutputSink` ([src/KerNet.Kernels.Abstractions](src/KerNet.Kernels.Abstractions))
is the only contract the host depends on:

```csharp
interface IKernel
{
    string Id;
    Task ExecuteAsync(string code, IKernelOutputSink sink, CancellationToken ct);
}
interface IKernelOutputSink
{
    void WriteText(string text);
    void WriteHtml(string html);
    void WriteError(string message, string? stackTrace = null);
}
```

**CSharp** ([src/KerNet.Kernels.CSharp](src/KerNet.Kernels.CSharp)) — `CSharpScript.RunAsync` for
the first cell, `ScriptState<object>.ContinueWithAsync` for every one after, so top-level
`var`s declared in one cell are visible in the next. `Display.Html`/`Display.Text`
(an `AsyncLocal<IKernelOutputSink>` set for the duration of `ExecuteAsync`) let a cell push rich
output explicitly; the last expression's value, if any, is also written as text via `ToString()`.
**Known limit**: no formatter registry like dotnet-interactive's (per-type HTML formatters) —
v1 only has `ToString()` plus explicit `Display.Html`.

**PySharp** ([src/KerNet.Kernels.PySharp](src/KerNet.Kernels.PySharp)) — one `PyEngine` per
session (from the `PySharp.Interpreter` package, `D:\dev\2026\repos\PySharp`). `PyEngine.Run`
always starts a fresh `__main__` module seeded only from `PyEngine.Globals`, so there is no
built-in "REPL" mode: `PySharpKernel` gets the same effect itself, by copying every non-dunder name
out of the finished module's `PyModule.Dict` back into `Globals` after each cell — entirely
through `PyEngine`'s own public surface, no internals touched. Stdout is captured via the
`TextWriter` the engine was built with; a `display_html(html)` callable is injected
(`SetVariable`) so cells can push HTML explicitly, the same role `Display.Html` plays for C#.

**Ontly** ([src/KerNet.Kernels.Ontly](src/KerNet.Kernels.Ontly)) — a cell is one full, standalone
YAML contract (no cross-cell `includes` in v1 — Ontly's own `ContractLoader` supports them, but
resolving an `include` relative to "a notebook cell" has no obvious meaning yet). Compiles it with
the real `Ontly.Domain.ContractCompiler` against both `Ontly.CSharp.CSharpGenerator` and
`Ontly.Python.PythonGenerator`; diagnostics (if any) become the cell's error, otherwise the output
is a self-contained HTML report (a summary table plus the generated C# and Python, HTML-escaped —
no external CDN scripts, so it renders offline in the notebook's sandboxed webview).

**Ralf** ([src/KerNet.Kernels.Ralf](src/KerNet.Kernels.Ralf)) — the cell's text is sent as a prompt
to a lazily-created `RalfAI.AgentEngine`, built around a custom `IAgentContext`
(`KerNetNotebookContext`) whose tools (`Notebook_ListCells`/`GetCell`/`RunCell`/`SetCellCode`) are
closures over `INotebookHost` — the host's cache of every cell's index/language/code, refreshed
from the `cells` field the extension attaches to *every* execute request (see protocol.md).
`Notebook_RunCell` dispatches straight back into the host's own kernel table (via
`INotebookHost.RunCellAsync`, implemented by `NotebookHostState` in `KerNet.Host`), capturing that
cell's output as plain text instead of NDJSON events. `Notebook_SetCellCode` can't touch the editor
directly — only the extension can apply a `WorkspaceEdit` — so it sends an `editCell` event instead
and returns immediately; the extension applies it and the *next* `Notebook_RunCell`/`GetCell` call
sees the change (the host's cell cache is only as fresh as the last execute request). Progress/
tool-call/notice events from `AgentEngine` stream out as plain-text output as they happen; the
final answer is rendered Markdown → HTML (Markdig, the same library `RalfAI.Studio` uses) as the
cell's rich output. Config is loaded with RalfAI's own `RalfAIConfig.Load()` — an existing
`RALFAI_CONFIG_PATH` or `appsettings.json` next to `KerNet.Host` works unchanged; if neither is
set up, the cell fails with a `WriteError` explaining what to add, instead of crashing the host.

`RalfAI.Core`/`RalfAI.Abstractions` are consumed as ordinary `PackageReference`s from
`D:\dev\NuGetLocalFeed` — see the "RalfAI packaging" note in [CONVERSATION.md](CONVERSATION.md) for
why that step existed and what it touched in the RalfAI repo (metadata only, no logic changes).

## VS Code extension

- `notebookSerializer.ts` — the `.kernet` file format: `{ "cells": [{ "kind", "language", "value" }] }`,
  nothing fancier.
- `kernelController.ts` — one `NotebookController` (`supportedLanguages = [csharp, pysharp, ontly, ralf]`,
  the same "pick a language per cell" shape dotnet-interactive's polyglot notebooks used) per
  extension activation; one `HostClient`/host process per open notebook document, keyed by URI.
  Output events are chained (not just individually awaited) before being applied to the cell, so a
  fast burst of events — Ralf's progress lines in particular — lands in order.
- `renderer/index.ts` — a `notebookRenderer` contribution for the `text/html` mime type: sets
  `innerHTML` from the output item's text inside the notebook's own sandboxed renderer webview.

## Known limits (v1)

- No C# formatter registry — raw `ToString()` unless a cell calls `Display.Html` itself.
- One host process per notebook: no remote/attached kernels, no restart-and-keep-state.
- Ontly cells are standalone contracts; no cross-cell `includes`.
- `Notebook_SetCellCode`'s effect is only visible to Ralf's *next* tool call in the same turn once
  the extension has applied the edit and a subsequent execute request has refreshed the cache —
  not instantaneous within the same tool call.
- The VS Code extension was written and reviewed by hand but not compiled/run in this environment
  (no Node.js install here — see USAGE.md) — treat first-run friction there as expected, not a sign
  the design is wrong.
