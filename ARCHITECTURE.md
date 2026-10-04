# Architecture

## Process shape

One `JupyterNet.Host` process per open `.ipynb` notebook, spawned by the VS Code extension
(`JupyterNetController.getClient`, [vscode-extension/src/kernelController.ts](vscode-extension/src/kernelController.ts))
and killed when the notebook closes or "JupyterNet: Restart Kernel Host" is run. The extension and the
host talk NDJSON over stdio — see [docs/protocol.md](docs/protocol.md) for the exact messages.

Inside the host ([src/JupyterNet.Host/Program.cs](src/JupyterNet.Host/Program.cs)), a single `while`
loop reads one request at a time and awaits it fully before reading the next — there is no
concurrent execution of two cells in the same notebook. All of the actual work — the kernel
dictionary, plugin discovery, dispatch — lives one layer down, in `JupyterNet.Engine`'s
`NotebookSession` (see "Engine, CLI and embedding" below); `Program.cs` is now just that plus the
NDJSON protocol wrapped around it. A kernel instance, created lazily on first use and kept for the
rest of the session, *is* the cell's session state (Roslyn's `ScriptState`, the F#
`FsiEvaluationSession`, PySharp's `PyEngine.Globals`, the Ralf `AgentEngine`).

## Kernels: four builtin, three plugins

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
interface IVariableInjectable
{
    Task SetVariableAsync(string name, object? value, CancellationToken ct);
}
```

`IVariableInjectable` is optional — the embedding entry point (see below); a kernel that doesn't
implement it simply doesn't support injection, and `NotebookSession.SetVariableAsync` throws a
clear `NotSupportedException` rather than pretending it worked.

`csharp`, `fsharp`, `powershell` and `powerfx` are **builtin** — compiled straight into `JupyterNet.Host`, no
discovery needed. `pysharp`/`ontly`/`ralf` are **plugins**: each lives in its own engine's repo
(PySharp/Ontly/RalfAI), builds a small project implementing `IKernelPlugin`, and gets loaded into
the host at runtime — see "Plugin loading" below for why they're split out this way and how it
works.

**CSharp** ([src/JupyterNet.Kernels.CSharp](src/JupyterNet.Kernels.CSharp)) — `CSharpScript.RunAsync`
for the first cell, `ScriptState<object>.ContinueWithAsync` for every one after, so top-level
`var`s declared in one cell are visible in the next. `Display.Html`/`Display.Text`
(an `AsyncLocal<IKernelOutputSink>` set for the duration of `ExecuteAsync`) let a cell push rich
output explicitly; the last expression's value, if any, is also written as text via `ToString()`.
**Known limit**: no formatter registry like dotnet-interactive's (per-type HTML formatters) —
v1 only has `ToString()` plus explicit `Display.Html`.

**Variable injection** (`IVariableInjectable.SetVariableAsync`) — the interesting constraint: Roslyn
scripting fixes the "globals" object's *type* on the very first `RunAsync` call, so there's no
runtime-extensible globals story to lean on directly. The globals object
(`ScriptGlobals`) instead carries one `Injected` dictionary (a public member, reachable from script
code by bare name like any other global), and injecting a new name runs a one-line hidden
interaction — `dynamic weather = Injected["weather"];` — through `ScriptState.ContinueWithAsync`
exactly like a real cell would be, so it becomes an ordinary top-level binding for every cell after
it. `dynamic` sidesteps needing the injected object's exact (possibly non-public) type name at the
call site — but note the DLR binder still enforces real accessibility on the *object's own type* at
each call site: an injected instance of a `private`/non-public type will still throw a
`RuntimeBinderException` when a cell tries to use its members (found writing this exact code's
tests — the injected type has to be `public`).

**FSharp** ([src/JupyterNet.Kernels.FSharp](src/JupyterNet.Kernels.FSharp)) — a single
`FsiEvaluationSession` (`FSharp.Compiler.Service`) per session, a real long-lived FSI/REPL session,
so (unlike PySharp) cross-cell state persistence needs no workaround at all. Two things had to be
gotten right that a first pass at the API didn't get for free:

- `EvalInteractionNonThrowing` returns `Tuple<FSharpChoice<FSharpOption<FsiValue>?, Exception>,
  FSharpDiagnostic[]>` — the outer `FSharpChoice` is success/failure, and on success the *inner*
  `FSharpOption<FsiValue>` is itself `None` for a non-expression interaction (e.g. `let x = 5`),
  `Some` for an expression. Both option layers need unwrapping — and that `Some`/`None` split is
  also how the cell's "result" is decided: when `Some`, its `ReflectionValue` is written as text,
  the same "last expression's value becomes the cell's output" role the C# kernel's `ReturnValue`
  plays.
- Left alone, every top-level binding/expression makes FSI auto-print its own echo (`val x: int =
  21`) — genuine, if noisy, FSI/REPL behavior. `FsiEvaluationSession.Create`'s `outWriter`/
  `errorWriter` are where that echo goes, so `FSharpKernel` passes `TextWriter.Null` for both and
  discards it entirely, rather than trying to filter it back out of captured text (a `Settings.fsi.
  ShowDeclarationValues = false` toggle exists and looked like the real switch, but proved
  inconsistent — it drops the value from a `let` binding's echo but not from an expression's `it`
  binding). What a cell actually "printed" is defined purely as what `printfn`/`Console.Write`
  wrote to the *real* `Console.Out` — captured by swapping `Console.Out`/`Console.Error` for the
  duration of exactly one `EvalInteractionNonThrowing` call and restoring them immediately after
  (never left swapped for other kernels/cells — a permanent swap would silently break the host's
  own protocol output for everything else). **This makes `Console.Out`/`Error` swapping — and so
  `FSharpKernel` itself — not safe to run concurrently from multiple threads**: two `FSharpKernel`
  instances executing at the same time race on that shared process-global state (found by the test
  suite: `PrintfnIsCapturedWithoutFsiEcho` flaked to empty output under xUnit's default parallel
  test execution, passed reliably once test parallelization was disabled). Never an issue in
  `JupyterNet.Host`/the CLI (both process one cell at a time by design), but an embedding host
  running more than one `NotebookSession` concurrently on different threads should not do so if
  any of them touch an `fsharp` kernel at the same time.

**Variable injection** (`IVariableInjectable.SetVariableAsync`) — FSI's own, real API for exactly
this: `FsiEvaluationSession.AddBoundValue(name, value)`. Unlike the C# kernel's `dynamic` trick,
the bound name gets the value's *actual* runtime type (via reflection), so member access/calls
just work with no unwrapping ceremony — confirmed with a `public` class exposing a property, a
method and an array, all reachable directly (`weather.City`, `weather.TempC(3)`,
`for x in weather.Forecast do ...`).

**PowerShell** ([src/JupyterNet.Kernels.PowerShell](src/JupyterNet.Kernels.PowerShell)) — a single
`Runspace` (`Microsoft.PowerShell.SDK` — real PowerShell 7+, not Windows PowerShell 5.1) per
session; a fresh `System.Management.Automation.PowerShell` pipeline per cell (the API's own
intended usage — one pipeline per invocation), all running against that same `Runspace`, so `$x`
set in one cell is still there in the next. `Write-Host` output is captured from
`pipeline.Streams.Information` (PowerShell 7+ tags it `"PSHOST"` and routes it there specifically
so a host without a rich UI can still see it — no custom `PSHost` implementation needed, unlike
what a fuller embedding would typically require) and `Write-Warning` from `Streams.Warning`; the
pipeline's own return objects (its "last expression" equivalent) are written via `ToString()`,
same simple convention as the other two builtin kernels. Adding this kernel forced a real
dependency-version bump: `Microsoft.PowerShell.SDK` transitively needs
`Microsoft.CodeAnalysis.CSharp >= 5.0.0` (it uses Roslyn internally for its own `Add-Type`
support), which conflicted with the `= 4.14.0` the C# kernel had pinned via
`Microsoft.CodeAnalysis.CSharp.Scripting` — resolved by moving that package to its current
`5.9.0`, re-verified against the full C# kernel test suite (session persistence, `Display.Html`,
errors, variable injection — all still passing) rather than assumed compatible from the version
number alone.

**Variable injection** (`IVariableInjectable.SetVariableAsync`) —
`Runspace.SessionStateProxy.SetVariable(name, value)`, PowerShell's own real API for exactly this. Like F#'s `AddBoundValue`
and unlike the C# kernel's `dynamic` trick, `$name` afterwards has real member/method access with
no unwrapping — PowerShell's own dynamically-typed object model makes this the natural case, not a
special one. Unlike `FSharpKernel`, `PowerShellKernel` never touches `Console.Out`/`Error` (output
capture goes through `PowerShell.Streams`/the pipeline's return objects instead), so it has none of
`FSharpKernel`'s concurrent-execution constraint.

**PowerFx** ([src/JupyterNet.Kernels.PowerFx](src/JupyterNet.Kernels.PowerFx)) — a single
`RecalcEngine` (`Microsoft.PowerFx.Interpreter`) per session. The grain is different from every
other kernel here: a cell is *one formula*, not a script of statements — the same thing a Power
Apps formula bar evaluates, with `ParserOptions.Culture` pinned to
`CultureInfo.InvariantCulture` so a notebook parses the same way regardless of the host machine's
own culture (found by actually hitting it: the same cell that parses fine on an en-US machine fails
outright on an it-IT one, which expects `;` instead of `,` as the function-argument separator).
**`Set(name, expr)`, the common "assign a session variable" cell, is implemented by the kernel
itself rather than delegated to Power Fx's own `Set` function.** Power Fx's native `Set` can only
*update* a name the engine already knows the type of — it rejects a brand-new name at bind time
("Name isn't valid"), and pre-declaring that name as an untyped `Blank` first just moves the
failure to a later type-mismatch error ("Invalid argument type (Decimal). Expecting a Blank value
instead.") once `Set` tries to assign a real value into it; a Power Fx variable's type, once fixed
by its first `UpdateVariable` call, doesn't widen. So a whole cell matching `Set(name, expr)` is
recognized by the kernel, `expr` is evaluated on its own, and the result goes straight into
`_engine.UpdateVariable(name, value)` — which both declares a brand-new name (typed correctly, from
the value itself) and updates an existing one, with Power Fx's own `Set` binder never involved
either way. `EnableSetFunction()` stays enabled regardless, so nested/advanced `Set()` calls
(inside an `If`/`With`, say) against an *already-declared* name still have a chance of working
natively; a `Set()` nested that way against a brand-new name is a known, undocumented limitation
(out of scope for what a notebook cell normally does).

**`Microsoft.PowerFx.Core` and `InvariantGlobalization` don't mix.** `JupyterNet.Host.csproj`/
`JupyterNet.Cli.csproj` both had `<InvariantGlobalization>true</InvariantGlobalization>` set —
unrelated boilerplate from this repo's very first commit, before any kernel existed, kept with no
particular reason (both are published framework-dependent, so it bought no actual trimming/size
benefit). Under that mode, Power Fx's *own error-message formatting* — not just `Text()`'s
custom-format path — calls `CultureInfo.CreateSpecificCulture("en")` internally and throws ("Only
the invariant culture is supported in globalization-invariant mode"), so something as basic as a
division-by-zero or a syntax-error cell stopped producing a clean `WriteError` and instead surfaced
a raw, confusing .NET exception. Found by actually running a PowerFx error cell through the real
`JupyterNet.Host` — the kernel's own test project has no such setting, so `dotnet test` alone never
would have caught it; worth remembering for any future builtin kernel that leans on another
library's own resource/localization machinery. Fixed by removing the setting from both `.csproj`
files, re-verified against the full regression sequence (`Set`, errors, string functions, variable
injection) run through the actual host process, not just the kernel in isolation.

**Variable injection** (`IVariableInjectable.SetVariableAsync`) — primitives go through
`FormulaValue.New(...)` directly; anything else through `TypeMarshallerCache.Marshal`, which
reflects an object's public *properties* into a Power Fx record. **Properties only** — Power Fx has
no concept of calling an arbitrary CLR instance method from a formula, by design (a closed formula
language, not a scripting one) — so `weather.City` works here exactly like every other kernel, but
`weather.TempC(3)` does not. That's a real difference in what this kernel can do with an injected
object, not a bug to fix.

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
`Notebook_RunCell` dispatches straight back into the session's own kernel table (via
`INotebookHost.RunCellAsync`, implemented by `NotebookSession` itself in `JupyterNet.Engine`),
capturing that cell's output as plain text instead of NDJSON events. `Notebook_SetCellCode` can't touch the
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
That only works if the host can load a kernel it was never built against —
[src/JupyterNet.Engine/KernelPluginLoader.cs](src/JupyterNet.Engine/KernelPluginLoader.cs) is the
whole of that mechanism (in `JupyterNet.Engine` precisely so `JupyterNet.Host`, the CLI, and an
embedding application all get it for free):

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
  PySharpLib included) is found automatically, no resolver code needed. This covers *managed*
  dependencies only — a *native* one (SkiaSharp, OpenCvSharp, the libraries PySharp's own
  numpy/cv2/matplotlib support need) isn't found this way, since `LoadFrom`'s probing is specific
  to the CLR assembly resolver. `KernelPluginLoader` additionally hooks
  `AssemblyLoadContext.Default.ResolvingUnmanagedDll` once, probing each loaded plugin's own
  `runtimes/<rid>/native/` folder and its root for the requested native library by name — the same
  thing .NET's own native-dependency resolution does automatically for a *referenced* package, done
  by hand here because a `LoadFrom`-loaded plugin's native dependencies aren't wired into that
  mechanism either.
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
of its kernel id actually executes in a session; `csharp`/`fsharp`/`powershell`/`powerfx` need no discovery step at all.

## Engine, CLI and embedding

`JupyterNet.Host`'s NDJSON loop was never the only reasonable way to run a notebook — a CI job or
a script wants to run one headlessly, and a .NET application wants to embed one directly and hand
it live objects. Both need the same kernel dispatch/plugin discovery logic `Program.cs` used to
own inline, so that logic moved to a standalone library, [`JupyterNet.Engine`](src/JupyterNet.Engine):

- **`NotebookDocument`**/`NotebookCell`/`NotebookOutput` — a small C# nbformat v4 reader/writer,
  the same shape `vscode-extension/src/notebookSerializer.ts` implements on the TypeScript side
  (`source` as newline-terminated line arrays, per-cell language in
  `metadata.vscode.languageId`, `display_data`/`error` outputs round-tripped). Nothing in this repo
  read `.ipynb` from C# before this — only the extension's TS side did.
- **`NotebookSession`** — the reusable engine: owns the builtin (`csharp`/`fsharp`/`powershell`/`powerfx`) and
  plugin-discovered kernels for one session, *is* the `INotebookHost` a Ralf-style kernel's tools
  call against (`RequestCellEdit` becomes a plain `CellEditRequested` event instead of an NDJSON
  `editCell` — a headless host applies it however makes sense for it, e.g. the CLI updates its own
  in-memory `NotebookDocument` cell directly), and adds the two entry points a protocol-based host
  didn't need to expose: `ExecuteCellAsync`/`ExecuteAsync` returning a plain `bool` (success — no
  exception, no separate "did it error" flag to check on the sink), and `SetVariableAsync` — the
  embedding entry point, delegating to a kernel's own `IVariableInjectable` if it has one.

`JupyterNet.Host` ([src/JupyterNet.Host/Program.cs](src/JupyterNet.Host/Program.cs)) is now just a
`NotebookSession` plus the NDJSON loop around it — no kernel/plugin logic of its own left.

**`JupyterNet.Cli`** ([src/JupyterNet.Cli/Program.cs](src/JupyterNet.Cli/Program.cs)) —
`jupyternet run <notebook.ipynb> [--kernel-paths ...] [--output <file>|--in-place] [--fail-fast]`:
loads a `NotebookDocument`, runs every code cell through a `NotebookSession` in order, printing
output to the console as it happens and (for `--output`/`--in-place`) recording it back into real
nbformat `outputs` — a minimal `jupyter nbconvert --execute` equivalent. Exit code `0` only if
every cell completed without an error. Hand-parsed flags, no CLI framework dependency — the same
"no more machinery than the task needs" choice `JupyterNet.Host` itself already made.

**Embedding** ([samples/EmbeddingSample](samples/EmbeddingSample)) — the whole story in one small
console app: `new NotebookSession()`, `SetVariableAsync("csharp", "weather", new Weather())`, then
cells that use `weather.City`/`weather.TempC(3)`/iterate `weather.Forecast` directly. No process,
no NDJSON — just a library call.

## VS Code extension

- `notebookSerializer.ts` — real Jupyter nbformat v4 (`.ipynb`), so a JupyterNet notebook opens,
  diffs and renders like any other notebook file. Per-cell language (nbformat has no field for one
  language per cell — it assumes one language for the whole file) is stored under each cell's
  `metadata.vscode.languageId`, the convention VS Code's own built-in notebook tooling uses for the
  same gap. `priority: "option"` in `package.json` means JupyterNet is offered as a choice for
  `.ipynb` rather than silently taking over every notebook (a real concern once the Jupyter
  extension is also installed, which handles `.ipynb` for actual Python/Jupyter kernels).
- `kernelController.ts` — one `NotebookController`
  (`supportedLanguages = [csharp, fsharp, powershell, powerfx, pysharp, ontly, ralf]`, the same "pick a language per
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
- `IVariableInjectable` is only implemented by the four builtin kernels, and the PowerFx kernel's
  own implementation only exposes an injected object's *properties* (no methods — see the PowerFx
  section above). PySharp's own kernel has an equivalent native capability
  (`PyEngine.SetVariable`) not yet wired to the interface — a natural follow-up in that repo, not
  done here since nothing in this round's ask required it. Ontly/Ralf have no obvious equivalent
  (a compiler, an agent) and likely never will.
- `FSharpKernel` is not safe to run concurrently across threads (it swaps the process-global
  `Console.Out`/`Error`) — see the F# section above. Not an issue for `JupyterNet.Host`/the CLI
  (sequential by design); an embedding host must not run two `NotebookSession`s with `fsharp`
  kernels on different threads at the same time. `CSharpKernel`/`PowerShellKernel` have no such
  constraint.
- Builtin kernels share one dependency graph, so adding one can force a version bump in another —
  already happened once (`Microsoft.PowerShell.SDK` forced `Microsoft.CodeAnalysis.CSharp.Scripting`
  from `4.14.0` to `5.9.0`, see the PowerShell section above). Worth re-running the full test suite
  after adding any future builtin kernel, not just trusting that `dotnet restore` resolving a
  version conflict means behavior didn't change.
- The VS Code extension compiles cleanly, `dotnet test tests/JupyterNet.Tests` passes (nbformat
  round-tripping, all four builtin kernels including variable injection, `NotebookSession`
  dispatch), and the host/CLI (builtin kernels + all three plugins, the exact cell sequence in
  `samples/tour.ipynb`) were exercised end to end via direct NDJSON and CLI runs *and*, separately,
  confirmed by hand in the installed extension: an F# cell in `samples/tour.ipynb`, run for real.
