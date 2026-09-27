# How this project came to be

A record of the conversation that started JupyterNet, kept here (per Marco's own convention of
tracking everything inside the project, not just in chat) rather than only in a chat log.

## The ask

Marco loved [dotnet/interactive](https://github.com/dotnet/interactive) — writing .NET inside
VS Code notebooks — and was sad to see it stop being developed. He asked for a "nucleo" (kernel
host) that could create kernels for **C#**, **PySharp** and **Ontly**, run code in cells, generate
HTML output, and — like every project of his — be fully tracked: README, architecture, usage docs,
VS Code registration, package generation, and the conversation itself.

Mid-request he added two more things: invoking **Ralf** (his own coding agent) so it could
interact with the notebook, and where to find everything — "tutti i progetti li trovi in D:\dev;
i package .NET li trovi in D:\dev\NuGetLocalFeed".

## What "PySharp" and "Ontly" turned out to be

Neither was invented for this project — both were guessed wrong at first and then found on disk:

- **PySharp** (`D:\dev\2026\repos\PySharp`) is not IronPython or a custom Python-like DSL — it's a
  **from-scratch Python 3 interpreter written in C#**, ~13k lines, 628 tests, with a real embedding
  facade (`PySharpLib.PyEngine`). Its README documents the embedding API precisely enough that
  JupyterNet's `pysharp` kernel is a thin wrapper, not a reimplementation.
- **Ontly** (`D:\dev\2026\repos\ontly`) is not a semantic-web/ontology DSL — it's a **YAML
  domain-contract compiler for C# and Python** (`Ontly.Domain.ContractCompiler` +
  `Ontly.CSharp`/`Ontly.Python` generators). That reframed what an "ontly kernel" should even mean:
  not "run this code", but "compile this contract and show me what it generates" — which is what
  the kernel does (HTML report: contract summary + generated C#/Python side by side).

Getting these two right mattered more than anything else in the design — the whole point was
wrapping real, already-built engines, not inventing placeholder languages.

## Ralf: what it is and how it's wired in

`D:\dev\MarcoParenzan\RalfAI` is Marco's own coding agent: a CLI (`ralf`) and Blazor Hybrid desktop
app (`ralf-studio`), built around `RalfAI.Core.AgentEngine` (Microsoft Agent Framework) and
pluggable `IAgentContext`s (`RalfAI.Contexts.DotNet`/`Markdown`/`HtmlApps`, each giving the agent
a system prompt + tools for one kind of project).

For Ralf to "interact with the notebook" rather than just answer in text, the `ralf` kernel needed
its own `IAgentContext` with tools bound to the live notebook — listing cells, reading one, running
one, editing one. That meant embedding `AgentEngine` **in-process** inside `JupyterNet.Host`, which in
turn meant referencing `RalfAI.Core`/`RalfAI.Abstractions` as libraries. Only `RalfAI.Providers`
was published as a NuGet package at the time; `RalfAI.Core`/`RalfAI.Abstractions` were not.

Two ways to close that gap were on the table: a cross-repo `ProjectReference` (fast, no changes to
RalfAI, but couples JupyterNet's build to a fixed path on disk), or packaging `RalfAI.Core`/
`RalfAI.Abstractions` the same way `RalfAI.Providers` already was and publishing them to
`D:\dev\NuGetLocalFeed`. Marco chose the packaging route — consistent with how every other project
under `D:\dev` is consumed — so that's what happened first, as a small, logic-free change to two
`.csproj` files in the RalfAI repo (`PackageId`/`Version`/`Description` only).

## Protocol and process shape

Rather than replicating .NET Interactive's own message protocol, JupyterNet uses a deliberately smaller
one: NDJSON over the stdio of one `JupyterNet.Host` process per open notebook (see
[docs/protocol.md](docs/protocol.md)). The one addition beyond plain "run this cell, get output
back" is that every execute request carries a snapshot of *all* the notebook's cells — the only way
the `ralf` kernel's tools can know what else is in the notebook without a separate sync protocol.

## What was verified live, in this session

- `dotnet build JupyterNet.slnx` — clean build, including resolving `PySharp.Interpreter`,
  `Ontly.Domain`/`Ontly.CSharp`/`Ontly.Python`, and the newly-packaged `RalfAI.Core`/
  `RalfAI.Abstractions`/`RalfAI.Providers` from `D:\dev\NuGetLocalFeed`.
- `JupyterNet.Host` piped NDJSON by hand: C# session state across cells, PySharp session state across
  cells plus `display_html`, a valid and an invalid Ontly contract (HTML report vs. diagnostic
  error), and — using the RalfAI configuration already set up on this machine — a real `ralf` cell
  that called `Notebook_ListCells` and `Notebook_RunCell` against a live PySharp cell and reported
  its actual output back in natural language.
- After Marco installed Node.js/npm mid-session: `npm install && npm run compile` — clean, zero
  TypeScript errors, confirming the hand-written extension code against the real `vscode` API
  types. That run also caught one real bug: a planned `jupyternet.hostDll` default in
  `.vscode/settings.json` using `${workspaceFolder}` would *not* have worked (VS Code only
  substitutes that in `launch.json`/`tasks.json`, not in arbitrary extension settings) — replaced
  with an `resolveHostDll` fallback in `kernelController.ts` that checks the filesystem itself
  (bundled path → repo Debug/Release build → error), and added a proper `launch.json` so F5 in
  `vscode-extension/` actually runs `npm: compile` then opens `samples/tour.ipynb`.
- **Still not verified**: an actual Extension Development Host session (F5) with a cell run by
  hand. An attempt to script this via `code --extensionDevelopmentPath=... --new-window ...` from
  the sandboxed shell opened a plain new window without the dev flags taking effect (most likely
  argument handling by the `code` CLI shim in that shell, not a problem with the extension) — worth
  retrying directly from a normal terminal/VS Code's own F5, not through that path.

## Switching the file format to real `.ipynb`

Marco pushed back on the invented `.kernet` JSON format (the project's name at the time): "il
notebook è sempre ipynb" — notebooks
are always `.ipynb`, full stop. `notebookSerializer.ts` was rewritten to read/write real Jupyter
nbformat v4 (cells, `source` as an array of newline-terminated lines, `outputs`, `nbformat`/
`nbformat_minor`), including round-tripping outputs (`display_data`/`error`) so saved notebooks
keep their results like any other `.ipynb`. Per-cell language — something plain nbformat has no
field for, since it assumes one language for the whole notebook — is stored under each cell's
`metadata.vscode.languageId`, the same convention VS Code's own built-in notebook tooling uses for
that exact gap, rather than inventing a JupyterNet-specific key.

Registering `*.ipynb` also meant addressing what happens when the Jupyter extension (or another
notebook extension) is installed and already claims `.ipynb`: `package.json`'s `notebooks`
contribution now sets `"priority": "option"`, so JupyterNet is offered as a choice via "Open With..."
rather than silently taking over every notebook in the workspace.

## Rename to JupyterNet, splitting the three engine kernels out, adding F#

Marco's next ask, after seeing the notebook actually render: rename the project to **JupyterNet**
(a better fit now that it produces real `.ipynb` files), move the PySharp/Ontly/Ralf kernels into
their own engine's repo instead of living inside this one, and keep only C# in this repo —
"aggiungi anche F#? VB.NET?" ("add F# too? VB.NET?"). Recommended, and agreed: F# in (a real
scripting/REPL API exists, `FSharp.Compiler.Service`'s FSI), VB.NET out (Roslyn never shipped a
`CSharpScript`-shaped scripting API for VB — a different, unstarted problem, not a small addition).

Splitting the kernels out meant `JupyterNet.Host` could no longer be built against all four
kernels directly — it needed to become a small plugin host instead, with PySharp/Ontly/Ralf each
carrying their own kernel plugin in their own repo. `JupyterNet.Kernels.Abstractions` gained one
new interface, `IKernelPlugin` (a kernel id plus a factory method), and `JupyterNet.Host` gained a
`KernelPluginLoader` that discovers plugin directories (`JUPYTERNET_KERNEL_PATHS`, or `kernels/*`
next to the host) and loads whichever one class in each implements that interface.

**Renaming the repo folder itself** (`D:\Dev\2026\repos\KerNet` → `...\JupyterNet`) needed VS Code
closed twice in a row — Windows file locks blocked first the top-level folder rename, then,
separately, renaming the individual project subfolders inside it once work resumed (the same
`EPERM`/"Device or resource busy" shape already seen once this session when reinstalling the
extension). Both times: ask Marco to close VS Code, retry, done — no destructive workaround
needed, just patience with Windows' file-locking model for anything with an open editor watcher.

**The plugin loader's first design didn't survive contact with real usage.** The initial version
gave each plugin its own collectible `AssemblyLoadContext`, for real version isolation between
plugins (a legitimate concern — Ralf's plugin drags in specific Azure SDK/Speech SDK versions).
Running the actual `samples/tour.ipynb` end to end — not just each kernel in isolation — surfaced
a reproducible failure: a plugin's own dependency (PySharpLib, Ontly.Domain) would fail to load
with "operation is not legal in the current state", but *only* if a builtin kernel (C# or F#, both
of which do dynamic/JIT compilation of their own) had already run in that session; the exact same
plugin loaded perfectly fine if it was the very first thing executed. That's a real interaction
between custom `AssemblyLoadContext`s and Roslyn/FSI's own dynamic compilation machinery, not
something to ship half-understood. Rebuilt on `Assembly.LoadFrom` instead — .NET's own "LoadFrom
context" already probes an assembly's own folder for its dependencies, no custom resolver needed —
and the exact same repeatedly-failing sequence (C# cell, then F#, then every plugin kernel in
`samples/tour.ipynb`'s actual order) passed cleanly. Documented as a deliberate simplification in
ARCHITECTURE.md, not a silently-dropped feature: version isolation between plugins is a real but
smaller risk than a host that intermittently fails to load a kernel depending on what ran before
it.

## What was verified live, this round

- `dotnet build JupyterNet.slnx` (host + builtin kernels only) and separately building each of the
  three plugins in their new homes (PySharp/ontly/RalfAI repos) — all clean.
- The exact cell sequence in `samples/tour.ipynb` (2 C# cells, 2 F# cells, 2 PySharp cells, 1 Ontly
  cell, 1 Ralf cell using its notebook tools against a live PySharp cell) run end to end through
  `JupyterNet.Host` via generated NDJSON — the same real RalfAI configuration on this machine
  handled the `ralf` cell for real again.
- The plugin loader's fail-soft path: a configured kernel directory with no valid plugin in it logs
  a warning and leaves that kernel id unavailable rather than crashing the host.
- `npm run compile` after all the extension-side changes (`jupyternet.kernelPaths`, `fsharp` in
  `supportedLanguages`) — clean.
- **Still not verified**: an actual Extension Development Host/installed-extension session with a
  human running these cells by hand in the notebook UI — same gap noted after the previous round.
