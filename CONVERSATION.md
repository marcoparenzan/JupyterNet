# How this project came to be

A record of the conversation that started KerNet, kept here (per Marco's own convention of
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
  KerNet's `pysharp` kernel is a thin wrapper, not a reimplementation.
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
one, editing one. That meant embedding `AgentEngine` **in-process** inside `KerNet.Host`, which in
turn meant referencing `RalfAI.Core`/`RalfAI.Abstractions` as libraries. Only `RalfAI.Providers`
was published as a NuGet package at the time; `RalfAI.Core`/`RalfAI.Abstractions` were not.

Two ways to close that gap were on the table: a cross-repo `ProjectReference` (fast, no changes to
RalfAI, but couples KerNet's build to a fixed path on disk), or packaging `RalfAI.Core`/
`RalfAI.Abstractions` the same way `RalfAI.Providers` already was and publishing them to
`D:\dev\NuGetLocalFeed`. Marco chose the packaging route — consistent with how every other project
under `D:\dev` is consumed — so that's what happened first, as a small, logic-free change to two
`.csproj` files in the RalfAI repo (`PackageId`/`Version`/`Description` only).

## Protocol and process shape

Rather than replicating .NET Interactive's own message protocol, KerNet uses a deliberately smaller
one: NDJSON over the stdio of one `KerNet.Host` process per open notebook (see
[docs/protocol.md](docs/protocol.md)). The one addition beyond plain "run this cell, get output
back" is that every execute request carries a snapshot of *all* the notebook's cells — the only way
the `ralf` kernel's tools can know what else is in the notebook without a separate sync protocol.

## What was verified live, in this session

- `dotnet build KerNet.slnx` — clean build, including resolving `PySharp.Interpreter`,
  `Ontly.Domain`/`Ontly.CSharp`/`Ontly.Python`, and the newly-packaged `RalfAI.Core`/
  `RalfAI.Abstractions`/`RalfAI.Providers` from `D:\dev\NuGetLocalFeed`.
- `KerNet.Host` piped NDJSON by hand: C# session state across cells, PySharp session state across
  cells plus `display_html`, a valid and an invalid Ontly contract (HTML report vs. diagnostic
  error), and — using the RalfAI configuration already set up on this machine — a real `ralf` cell
  that called `Notebook_ListCells` and `Notebook_RunCell` against a live PySharp cell and reported
  its actual output back in natural language.
- After Marco installed Node.js/npm mid-session: `npm install && npm run compile` — clean, zero
  TypeScript errors, confirming the hand-written extension code against the real `vscode` API
  types. That run also caught one real bug: a planned `kernet.hostDll` default in
  `.vscode/settings.json` using `${workspaceFolder}` would *not* have worked (VS Code only
  substitutes that in `launch.json`/`tasks.json`, not in arbitrary extension settings) — replaced
  with an `resolveHostDll` fallback in `kernelController.ts` that checks the filesystem itself
  (bundled path → repo Debug/Release build → error), and added a proper `launch.json` so F5 in
  `vscode-extension/` actually runs `npm: compile` then opens `samples/tour.kernet`.
- **Still not verified**: an actual Extension Development Host session (F5) with a cell run by
  hand. An attempt to script this via `code --extensionDevelopmentPath=... --new-window ...` from
  the sandboxed shell opened a plain new window without the dev flags taking effect (most likely
  argument handling by the `code` CLI shim in that shell, not a problem with the extension) — worth
  retrying directly from a normal terminal/VS Code's own F5, not through that path.
