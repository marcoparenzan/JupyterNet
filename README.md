# KerNet

A kernel host for **.NET notebooks in VS Code** — the itch [dotnet/interactive](https://github.com/dotnet/interactive)
left unscratched once it stopped being developed. KerNet doesn't reinvent languages: it wires
four things Marco Parenzan already built into one notebook experience.

| Cell language | Backed by |
| --- | --- |
| `csharp` | Roslyn scripting (`Microsoft.CodeAnalysis.CSharp.Scripting`) — a REPL-style C#, variables persist across cells. |
| `pysharp` | PySharp (`D:\dev\2026\repos\PySharp`) — a Python 3 interpreter written from scratch in C#, embedded via `PySharpLib.PyEngine`. |
| `ontly` | Ontly (`D:\dev\2026\repos\ontly`) — a YAML domain-contract compiler; a cell is a contract, the output is its generated C#/Python. |
| `ralf` | RalfAI's own coding agent, embedded in-process with tools to inspect, run and edit the notebook's *other* cells. |

Run code in cells, get HTML output, keep state between cells — the parts of dotnet/interactive
that made it worth using, on top of tools already living under `D:\dev`.

## Layout

- `src/KerNet.Protocol` — the NDJSON message contracts shared by the host and the extension.
- `src/KerNet.Kernels.Abstractions` — `IKernel`/`IKernelOutputSink`/`INotebookHost`.
- `src/KerNet.Kernels.{CSharp,PySharp,Ontly,Ralf}` — one kernel per language.
- `src/KerNet.Host` — the process the extension spawns: reads NDJSON on stdin, dispatches to the
  four kernels, writes NDJSON on stdout.
- `vscode-extension/` — the VS Code extension: notebook type `kernet-notebook` for `*.ipynb` files
  (real Jupyter nbformat, offered as an option rather than hijacking every notebook), one
  multi-language `NotebookController`, an HTML output renderer.
- `samples/tour.ipynb` — a notebook exercising all four kernels.
- `docs/protocol.md` — the wire protocol. [ARCHITECTURE.md](ARCHITECTURE.md) — how it all fits
  together and its known limits. [USAGE.md](USAGE.md) — install and run it.
  [CONVERSATION.md](CONVERSATION.md) — how this project came to be.

## Quick start

```powershell
# build everything
dotnet build KerNet.slnx

# smoke-test the host directly (see docs/protocol.md for the message shapes)
echo '{"id":"1","method":"execute","params":{"kernel":"csharp","code":"21 * 2"}}' | dotnet run --project src/KerNet.Host

# the VS Code extension (needs Node.js — see USAGE.md)
cd vscode-extension
npm install
npm run compile
# then F5 in VS Code to launch an Extension Development Host, and open samples/tour.ipynb
```

## Publishing

`build/pack.ps1` packs every `KerNet.*` project into `D:\dev\NuGetLocalFeed`, the same local feed
PySharp/Ontly/RalfAI already publish to. `build/package-extension.ps1` publishes `KerNet.Host`
into `vscode-extension/host/` and packages the extension as a `.vsix`.
