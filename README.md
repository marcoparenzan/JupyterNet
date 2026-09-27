# JupyterNet

A kernel host for **.NET notebooks in VS Code** — the itch [dotnet/interactive](https://github.com/dotnet/interactive)
left unscratched once it stopped being developed. JupyterNet ships two ".NET languages" itself
(C#, F#) and is otherwise a small **plugin host**: PySharp, Ontly and Ralf each carry their own
kernel plugin in their own repo, matching the engine's own release cycle rather than JupyterNet's.

| Cell language | Backed by |
| --- | --- |
| `csharp` | Roslyn scripting (`Microsoft.CodeAnalysis.CSharp.Scripting`) — builtin, a REPL-style C#, variables persist across cells. |
| `fsharp` | `FSharp.Compiler.Service`'s `FsiEvaluationSession` — builtin, a real long-lived FSI session, so state just persists on its own. |
| `pysharp` | Plugin, from `D:\dev\2026\repos\PySharp` — a Python 3 interpreter written from scratch in C#, embedded via `PySharpLib.PyEngine`. |
| `ontly` | Plugin, from `D:\dev\2026\repos\ontly` — a YAML domain-contract compiler; a cell is a contract, the output is its generated C#/Python. |
| `ralf` | Plugin, from `D:\dev\MarcoParenzan\RalfAI` — RalfAI's own coding agent, embedded in-process with tools to inspect, run and edit the notebook's *other* cells. |

Run code in cells, get HTML output, keep state between cells — the parts of dotnet/interactive
that made it worth using, on top of tools already living under `D:\dev`.

## Layout

- `src/JupyterNet.Protocol` — the NDJSON message contracts shared by the host and the extension.
- `src/JupyterNet.Kernels.Abstractions` — `IKernel`/`IKernelOutputSink`/`INotebookHost`/`IKernelPlugin`,
  the entire contract a kernel plugin implements.
- `src/JupyterNet.Kernels.{CSharp,FSharp}` — the two builtin kernels.
- `src/JupyterNet.Host` — the process the extension spawns: reads NDJSON on stdin, runs the builtin
  kernels, discovers and loads kernel plugins (`KernelPluginLoader.cs`), writes NDJSON on stdout.
- `vscode-extension/` — the VS Code extension: notebook type `jupyternet-notebook` for `*.ipynb`
  files (real Jupyter nbformat, offered as an option rather than hijacking every notebook), one
  multi-language `NotebookController`, an HTML output renderer.
- `samples/tour.ipynb` — a notebook exercising all five kernels.
- `docs/protocol.md` — the wire protocol. [ARCHITECTURE.md](ARCHITECTURE.md) — how it all fits
  together, the plugin-loading design, and known limits. [USAGE.md](USAGE.md) — install and run
  it. [CONVERSATION.md](CONVERSATION.md) — how this project came to be.

The PySharp/Ontly/Ralf kernel plugins themselves live beside the engine they wrap:
`D:\dev\2026\repos\PySharp\src\JupyterNet.Kernels.PySharp`,
`D:\dev\2026\repos\ontly\src\JupyterNet.Kernels.Ontly`,
`D:\dev\MarcoParenzan\RalfAI\src\JupyterNet.Kernels.Ralf` — not in this repo.

## Quick start

```powershell
# build the host + builtin kernels
dotnet build JupyterNet.slnx

# smoke-test the host directly (see docs/protocol.md for the message shapes)
echo '{"id":"1","method":"execute","params":{"kernel":"csharp","code":"21 * 2"}}' | dotnet run --project src/JupyterNet.Host

# the VS Code extension (needs Node.js — see USAGE.md)
cd vscode-extension
npm install
npm run compile
# then F5 in VS Code to launch an Extension Development Host, and open samples/tour.ipynb
```

To exercise the `pysharp`/`ontly`/`ralf` cells you also need those three plugins published once —
see [USAGE.md](USAGE.md) for the exact commands and where the extension expects to find them
(`jupyternet.kernelPaths`).

## Publishing

`build/pack.ps1` packs the projects that live in this repo (`JupyterNet.Protocol`,
`Kernels.Abstractions`, `Kernels.CSharp`, `Kernels.FSharp`, `Host`) into `D:\dev\NuGetLocalFeed`.
`build/package-extension.ps1` publishes `JupyterNet.Host` *and* the three external kernel plugins
into `vscode-extension/host/`, then packages the extension as a `.vsix`.
