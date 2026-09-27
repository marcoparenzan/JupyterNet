# Architecture

## Process shape

One `JupyterNet.Host` process per open `.ipynb` notebook, spawned by the VS Code extension
(`JupyterNetController.getClient`, [vscode-extension/src/kernelController.ts](vscode-extension/src/kernelController.ts))
and killed when the notebook closes or "JupyterNet: Restart Kernel Host" is run. The extension and the
host talk NDJSON over stdio — see [docs/protocol.md](docs/protocol.md) for the exact messages.

Inside the host ([src/JupyterNet.Host/Program.cs](src/JupyterNet.Host/Program.cs)), a single `while`
loop reads one request at a time and awaits it fully before reading the next — there is no
concurrent execution of two cells in the same notebook. A dictionary maps kernel id → `IKernel`
instance, created lazily on first use and kept for the rest of the session; that instance *is* the
cell's session state (Roslyn's `ScriptState`, the F# `FsiEvaluationSession`, PySharp's
`PyEngine.Globals`, the Ralf `AgentEngine`).

## Kernels: two builtin, three plugins

`IKernel`/`IKernelOutputSink`/`IKernelPlugin`
([src/JupyterNet.Kernels.Abstractions](src/JupyterNet.Kernels.Abstractions)) is the entire contract
anything running as a JupyterNet kernel depends on:

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
interface IKernelPlugin
{
    string KernelId;
    IKernel CreateKernel(INotebookHost notebookHost);
}
```

`csharp` and `fsharp` are **builtin** — compiled straight into `JupyterNet.Host`, no discovery
needed. `pysharp`/`ontly`/`ralf` are **plugins**: each lives in its own engine's repo (PySharp/
Ontly/RalfAI), builds a small project implementing `IKernelPlugin`, and gets loaded into the host
at runtime — see "Plugin loading" below for why they're split out this way and how it works.

**CSharp** ([src/JupyterNet.Kernels.CSharp](src/JupyterNet.Kernels.CSharp)) — `CSharpScript.RunAsync`
for the first cell, `ScriptState<object>.ContinueWithAsync` for every one after, so top-level
`var`s declared in one cell are visible in the next. `Display.Html`/`Display.Text`
(an `AsyncLocal<IKernelOutputSink>` set for the duration of `ExecuteAsync`) let a cell push rich
output explicitly; the last expression's value, if any, is also written as text via `ToString()`.
**Known limit**: no formatter registry like dotnet-interactive's (per-type HTML formatters) —
v1 only has `ToString()` plus explicit `Display.Html`.

**FSharp** ([src/JupyterNet.Kernels.FSharp](src/JupyterNet.Kernels.FSharp)) — a single
`FsiEvaluationSession` (`FSharp.Compiler.Service`) per session, a real long-lived FSI/REPL session,
so (unlike PySharp) cross-cell state persistence needs no workaround at all. Two things had to be
gotten right that a first pass at the API didn't get for free:

- `EvalInteractionNonThrowing` returns `Tuple<FSharpChoice<FSharpOption<FsiValue>?, Exception>,
  FSharpDiagnostic[]>` — the outer `FSharpChoice` is success/failure, and on success the *inner*
  `FSharpOption<FsiValue>` is itself `None` for a non-expression interaction (e.g. `let x = 5`),
  `Some` for an expression. Both option layers need unwrapping.
- `EvalInteractionNonThrowing`'s own `outWriter`/`errorWriter` only capture FSI's *own* echo
  (`val x: int = 21`) — plain `printfn`/`Console.Write` inside evaluated code still targets the
  real process `Console.Out`, which is also `JupyterNet.Host`'s own NDJSON stdout. `FSharpKernel`
  swaps `Console.Out`/`Console.Error` for the duration of exactly one `EvalInteractionNonThrowing`
  call and restores them immediately after (never left swapped for other kernels/cells — a
  permanent swap would silently break the host's own protocol output for everything else).

**PySharp** (plugin, `D:\dev\2026\repos\PySharp\src\JupyterNet.Kernels.PySharp`) — one `PyEngine`
per session. `PyEngine.Run` always starts a fresh `__main__` module seeded only from
`PyEngine.Globals`, so there is no built-in "REPL" mode: `PySharpKernel` gets the same effect
itself, by copying every non-dunder name out of the finished module's `PyModule.Dict` back into
`Globals` after each cell — entirely through `PyEngine`'s own public surface, no internals touched.
Stdout is captured via the `TextWriter` the engine was built with; a `display_html(html)` callable
is injected (`SetVariable`) so cells can push HTML explicitly, the same role `Display.Html` plays
for C#.

**Ontly** (plugin, `D:\dev\2026\repos\ontly\src\JupyterNet.Kernels.Ontly`) — a cell is one full,
standalone YAML contract (no cross-cell `includes` in v1 — Ontly's own `ContractLoader` supports
them, but resolving an `include` relative to "a notebook cell" has no obvious meaning yet).
Compiles it with the real `Ontly.Domain.ContractCompiler` against both `Ontly.CSharp.CSharpGenerator`
and `Ontly.Python.PythonGenerator`; diagnostics (if any) become the cell's error, otherwise the
output is a self-contained HTML report (a summary table plus the generated C# and Python,
HTML-escaped — no external CDN scripts, so it renders offline in the notebook's sandboxed webview).

**Ralf** (plugin, `D:\dev\MarcoParenzan\RalfAI\src\JupyterNet.Kernels.Ralf`) — the cell's text is
sent as a prompt to a lazily-created `RalfAI.AgentEngine`, built around a custom `IAgentContext`
(`JupyterNetNotebookContext`) whose tools (`Notebook_ListCells`/`GetCell`/`RunCell`/`SetCellCode`)
are closures over `INotebookHost` — the host's cache of every cell's index/language/code, refreshed
from the `cells` field the extension attaches to *every* execute request (see protocol.md).
`Notebook_RunCell` dispatches straight back into the host's own kernel table (via
`INotebookHost.RunCellAsync`, implemented by `NotebookHostState` in `JupyterNet.Host`), capturing
that cell's output as plain text instead of NDJSON events. `Notebook_SetCellCode` can't touch the
editor directly — only the extension can apply a `WorkspaceEdit` — so it sends an `editCell` event
instead and returns immediately; the extension applies it and the *next*
`Notebook_RunCell`/`GetCell` call sees the change (the host's cell cache is only as fresh as the
last execute request). Progress/tool-call/notice events from `AgentEngine` stream out as
plain-text output as they happen; the final answer is rendered Markdown → HTML (Markdig, the same
library `RalfAI.Studio` uses) as the cell's rich output. Config is loaded with RalfAI's own
`RalfAIConfig.Load()` — an existing `RALFAI_CONFIG_PATH` or `appsettings.json` next to
`JupyterNet.Host` works unchanged; if neither is set up, the cell fails with a `WriteError`
explaining what to add, instead of crashing the host. Since this plugin now lives inside the
RalfAI repo itself, it references `RalfAI.Core`/`RalfAI.Abstractions`/`RalfAI.Providers` directly
via `ProjectReference` rather than through NuGet.

## Plugin loading

PySharp/Ontly/Ralf are split out of this repo on purpose: each engine's own repo should own how it
plugs into a notebook, versioned and released on that engine's own schedule, not JupyterNet's.
That only works if `JupyterNet.Host` can load a kernel it was never built against —
[src/JupyterNet.Host/KernelPluginLoader.cs](src/JupyterNet.Host/KernelPluginLoader.cs) is the
whole of that mechanism:

- **Discovery**: search directories come from `JUPYTERNET_KERNEL_PATHS` (an OS path-list, one
  directory per plugin — each is that plugin project's own `dotnet publish` output), falling back
  to scanning `<host base dir>/kernels/*` if unset. The VS Code extension sets the environment
  variable from its own `jupyternet.kernelPaths` setting (default: this machine's three plugin
  repos); a packaged `.vsix` instead bundles published copies under `host/kernels/*`, which is
  exactly the fallback path, so it works without the setting.
- **Loading**: `Assembly.LoadFrom(mainAssemblyPath)` — the one deliberately unglamorous choice
  here. `LoadFrom` comes with .NET's own "LoadFrom context" dependency probing: a dependency that
  can't otherwise be resolved is looked for beside the assembly that requested it, so everything
  `dotnet publish` copied into a plugin's own folder (project-reference dependencies like
  PySharpLib included) is found automatically, no resolver code needed.
- **Discovery of the plugin type itself**: once a plugin's main assembly is loaded, the host scans
  its types for one implementing `IKernelPlugin` and instantiates it via its parameterless
  constructor — nothing to register or configure beyond that class existing.
- **Fail-soft**: a configured directory that's missing, empty, or whose assembly won't load logs a
  warning to stderr (visible in the "JupyterNet" output channel) and simply leaves that kernel id
  unavailable; a cell that tries to use it gets a normal `WriteError`, not a crashed host — the
  same fail-soft precedent the Ralf kernel's missing-config path already set.
- **Why not per-plugin `AssemblyLoadContext` isolation**: tried first, for real version isolation
  between plugins (concretely: the Ralf plugin's Azure SDK/Speech SDK versions never colliding
  with anything else). It produced a reproducible failure instead — running *either* builtin
  kernel (C#'s Roslyn scripting or F#'s FSI, both of which do their own dynamic/JIT compilation)
  before a plugin's first use made that plugin's own same-folder dependency resolution start
  failing with "operation is not legal in the current state", evidence of the two dynamic-
  compilation engines interacting with custom `AssemblyLoadContext`s in a way not understood well
  enough to ship. A single shared load context (what every kernel, plugin or not, uses today) is
  the correct-first choice for what this actually is — three specific plugins on one person's
  machine, not an untrusted multi-tenant plugin host. **Known limitation**: two plugins pinning
  genuinely conflicting versions of the same shared dependency would now collide; revisit
  per-plugin isolation only if that becomes a real problem, with more room to actually track down
  the interaction above first.

Kernel instantiation stays lazy exactly as before — a plugin is only loaded the first time a cell
of its kernel id actually executes in a session; `csharp`/`fsharp` need no discovery step at all.

## VS Code extension

- `notebookSerializer.ts` — real Jupyter nbformat v4 (`.ipynb`), so a JupyterNet notebook opens,
  diffs and renders like any other notebook file. Per-cell language (nbformat has no field for one
  language per cell — it assumes one language for the whole file) is stored under each cell's
  `metadata.vscode.languageId`, the convention VS Code's own built-in notebook tooling uses for the
  same gap. `priority: "option"` in `package.json` means JupyterNet is offered as a choice for
  `.ipynb` rather than silently taking over every notebook (a real concern once the Jupyter
  extension is also installed, which handles `.ipynb` for actual Python/Jupyter kernels).
- `kernelController.ts` — one `NotebookController`
  (`supportedLanguages = [csharp, fsharp, pysharp, ontly, ralf]`, the same "pick a language per
  cell" shape dotnet-interactive's polyglot notebooks used) per extension activation; one
  `HostClient`/host process per open notebook document, keyed by URI. `jupyternet.kernelPaths`
  (a setting, array of directories) is forwarded to the spawned host as `JUPYTERNET_KERNEL_PATHS`.
  Output events are chained (not just individually awaited) before being applied to the cell, so a
  fast burst of events — Ralf's progress lines in particular — lands in order.
- `renderer/index.ts` — a `notebookRenderer` contribution for the `text/html` mime type: sets
  `innerHTML` from the output item's text inside the notebook's own sandboxed renderer webview.
  Compiled as its own ES module (`tsconfig.renderer.json`, a separate `tsc` pass) rather than
  CommonJS like the rest of the extension — the renderer webview loads its entrypoint as a real ES
  module, and a CommonJS build references a nonexistent `exports` global there at runtime
  ("Error loading renderer: exports is not defined"), which is exactly what happened before this
  was split out.

## Known limits (v1)

- No C# formatter registry — raw `ToString()` unless a cell calls `Display.Html` itself.
- One host process per notebook: no remote/attached kernels, no restart-and-keep-state.
- Ontly cells are standalone contracts; no cross-cell `includes`.
- `Notebook_SetCellCode`'s effect is only visible to Ralf's *next* tool call in the same turn once
  the extension has applied the edit and a subsequent execute request has refreshed the cache —
  not instantaneous within the same tool call.
- All kernels (builtin and plugin) share one load context; two plugins pinning conflicting
  versions of the same dependency would collide (see "Why not per-plugin `AssemblyLoadContext`
  isolation" above). Native-library dependencies (e.g. under `runtimes/<rid>/native/`) still need
  the OS loader to find them through its own mechanism regardless.
- The VS Code extension compiles cleanly (`npm install && npm run compile`, verified) and the host
  (builtin kernels + all three plugins, including the exact cell sequence in `samples/tour.ipynb`)
  was exercised end to end via direct NDJSON smoke tests, but the notebook UI itself has not yet
  been driven through an actual Extension Development Host or installed-extension session with a
  human clicking "run cell" — do that before trusting the UI half specifically.
