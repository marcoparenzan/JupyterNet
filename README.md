# JupyterNet

A kernel host for **.NET notebooks in VS Code** — the itch [dotnet/interactive](https://github.com/dotnet/interactive)
left unscratched once it stopped being developed. JupyterNet ships four ".NET languages" itself
(C#, F#, PowerShell, Power Fx) and is otherwise a small **plugin host**: PySharp, Ontly and Ralf each carry
their own kernel plugin in their own repo, matching the engine's own release cycle rather than
JupyterNet's.

| Cell language | Backed by |
| --- | --- |
| `csharp` | Roslyn scripting (`Microsoft.CodeAnalysis.CSharp.Scripting`) — builtin, a REPL-style C#, variables persist across cells. |
| `fsharp` | `FSharp.Compiler.Service`'s `FsiEvaluationSession` — builtin, a real long-lived FSI session, so state just persists on its own. |
| `powershell` | `Microsoft.PowerShell.SDK` — builtin, a real embedded PowerShell 7+ `Runspace`, so state persists the same way. |
| `powerfx` | `Microsoft.PowerFx.Interpreter`'s `RecalcEngine` — builtin, a single formula engine per session; a cell is one formula, same shape as a Power Apps formula bar. |
| `pysharp` | Plugin, from `D:\dev\2026\repos\PySharp` — a Python 3 interpreter written from scratch in C#, embedded via `PySharpLib.PyEngine`. |
| `ontly` | Plugin, from `D:\dev\2026\repos\ontly` — a YAML domain-contract compiler; a cell is a contract, the output is its generated C#/Python. |
| `ralf` | Plugin, from `D:\dev\MarcoParenzan\RalfAI` — RalfAI's own coding agent, embedded in-process with tools to inspect, run and edit the notebook's *other* cells. |

Run code in cells, get HTML output, keep state between cells — the parts of dotnet/interactive
that made it worth using, on top of tools already living under `D:\dev`. JupyterNet is not only a
VS Code extension: `JupyterNet.Engine` (nbformat read/write, kernel dispatch, plugin discovery,
variable injection) is a standalone library, so a `.ipynb` can also be run headlessly from a
**CLI** (`jupyternet run notebook.ipynb`) or **embedded directly in a .NET application**, which can
inject live objects into a kernel's context — see [samples/EmbeddingSample](samples/EmbeddingSample).

## Layout

- `src/JupyterNet.Protocol` — the NDJSON message contracts shared by the host and the extension.
- `src/JupyterNet.Kernels.Abstractions` — `IKernel`/`IKernelOutputSink`/`INotebookHost`/
  `IKernelPlugin`/`IVariableInjectable`, the entire contract a kernel (builtin or plugin) implements.
- `src/JupyterNet.Kernels.{CSharp,FSharp,PowerShell,PowerFx}` — the four builtin kernels.
- `src/JupyterNet.Engine` — the reusable core: `NotebookDocument` (a C# nbformat reader/writer),
  `NotebookSession` (kernel dispatch, plugin discovery, variable injection) — what `JupyterNet.Host`,
  `JupyterNet.Cli`, and any embedding application all build on.
- `src/JupyterNet.Host` — the process the VS Code extension spawns: wraps `NotebookSession` in the
  NDJSON stdin/stdout protocol.
- `src/JupyterNet.Cli` — `jupyternet run <notebook.ipynb>`, headless execution for CI/scripting.
- `vscode-extension/` — the VS Code extension: notebook type `jupyternet-notebook` for `*.ipynb`
  files (real Jupyter nbformat, offered as an option rather than hijacking every notebook), one
  multi-language `NotebookController`, an HTML output renderer.
- `samples/tour.ipynb` — a notebook exercising all seven kernels. `samples/` also has one
  per-language sample notebook (`csharp.ipynb`, `fsharp.ipynb`, `powershell.ipynb`,
  `powerfx.ipynb`, `pysharp.ipynb`, `ontly.ipynb`, `ralf.ipynb`), each focused on one kernel's own
  features. `samples/EmbeddingSample` — a console app embedding `JupyterNet.Engine` directly and
  injecting a live object into a cell.
- `tests/JupyterNet.Tests` — xUnit coverage for the nbformat reader/writer, all four builtin kernels
  (including variable injection), and `NotebookSession`'s dispatch/error handling.
- `docs/protocol.md` — the wire protocol. [ARCHITECTURE.md](ARCHITECTURE.md) — how it all fits
  together, the plugin-loading design, and known limits. [USAGE.md](USAGE.md) — install and run
  it. [CONVERSATION.md](CONVERSATION.md) — how this project came to be.

The PySharp/Ontly/Ralf kernel plugins themselves live beside the engine they wrap:
`D:\dev\2026\repos\PySharp\src\JupyterNet.Kernels.PySharp`,
`D:\dev\2026\repos\ontly\src\JupyterNet.Kernels.Ontly`,
`D:\dev\MarcoParenzan\RalfAI\src\JupyterNet.Kernels.Ralf` — not in this repo.

## Quick start

```powershell
# build everything (host, CLI, engine, builtin kernels, tests, embedding sample)
dotnet build JupyterNet.slnx
dotnet test tests/JupyterNet.Tests

# headless: run a notebook's cells top to bottom from the CLI
dotnet run --project src/JupyterNet.Cli -- run samples/tour.ipynb

# or install it as a real `jupyternet` command (see Publishing below), then just:
jupyternet run samples/tour.ipynb

# smoke-test the host directly (see docs/protocol.md for the message shapes)
echo '{"id":"1","method":"execute","params":{"kernel":"csharp","code":"21 * 2"}}' | dotnet run --project src/JupyterNet.Host

# the VS Code extension (needs Node.js — see USAGE.md)
cd vscode-extension
npm install
npm run compile
# then F5 in VS Code to launch an Extension Development Host, and open samples/tour.ipynb
```

To exercise the `pysharp`/`ontly`/`ralf` cells (in the CLI, the host, or the extension) you also
need those three plugins published once — see [USAGE.md](USAGE.md) for the exact commands and
where the extension/CLI expect to find them (`jupyternet.kernelPaths`/`--kernel-paths`).

## Publishing

`build/pack.ps1` packs the projects that live in this repo (`JupyterNet.Protocol`,
`Kernels.Abstractions`, `Kernels.CSharp`, `Kernels.FSharp`, `Kernels.PowerShell`, `Kernels.PowerFx`,
`Engine`, `Host`, `Cli`) into
`D:\dev\NuGetLocalFeed`. `build/package-extension.ps1` publishes `JupyterNet.Host` *and* the three
external kernel plugins into `vscode-extension/host/`, then packages the extension as a `.vsix`.

`JupyterNet.Cli` packs as a real `dotnet tool` (`jupyternet` on PATH), the same pattern PySharp/
Ontly/RalfAI already use for `pysharp`/`ontly`/`ralf`:

```powershell
./build/pack.ps1
dotnet tool install --global --add-source D:\dev\NuGetLocalFeed JupyterNet.Cli   # first time
dotnet tool update  --global --add-source D:\dev\NuGetLocalFeed JupyterNet.Cli   # after bumping <Version>
```

`dotnet tool update` only notices a *version bump* — re-`pack`ing the exact same `<Version>` (this
repo hasn't bumped it yet) leaves it reporting "already installed" and running the old bits, the
same NuGet global-package-cache gotcha Ontly's own `_rules.md` documents. Without a version bump,
force a real refresh instead:

```powershell
dotnet tool uninstall --global JupyterNet.Cli
dotnet tool install --global --add-source D:\dev\NuGetLocalFeed JupyterNet.Cli
```
