# Usage

## Prerequisites

- .NET SDK 10.
- Node.js + npm, to build the VS Code extension (**not installed on the machine this project was
  first built on** — the TypeScript in `vscode-extension/` was written and reviewed by hand but
  never actually compiled or run; do that first before trusting it end to end).
- `D:\dev\NuGetLocalFeed` reachable and containing `PySharp.Interpreter`, `Ontly.Domain`,
  `Ontly.CSharp`, `Ontly.Python`, `RalfAI.Core`, `RalfAI.Abstractions`, `RalfAI.Providers` (all
  already published there as of this writing).
- For `ralf` cells: an existing RalfAI configuration — either `RALFAI_CONFIG_PATH` pointing at a
  working `config.json` (the same one `ralf`/`ralf-studio` use), or an `appsettings.json` next to
  `KerNet.Host.dll` with the same `AIProvider`/`OpenAI`|`Anthropic`|`AzureAIFoundry` shape. Without
  either, `ralf` cells fail with a clear error instead of crashing the host.

## Build and smoke-test the host

```powershell
dotnet build KerNet.slnx
```

`KerNet.Host` reads NDJSON requests on stdin and writes NDJSON events on stdout — see
[docs/protocol.md](docs/protocol.md). To try it directly without VS Code:

```powershell
'{"id":"1","method":"execute","params":{"kernel":"csharp","code":"21 * 2"}}' | dotnet run --project src/KerNet.Host
```

## Install the VS Code extension

```powershell
cd vscode-extension
npm install
npm run compile
```

Then, in VS Code, open the `vscode-extension` folder and press F5 to launch an Extension
Development Host. In that window:

- Run **KerNet: New Notebook**, or open `samples/tour.kernet` from the repo root.
- Each cell picks its language from VS Code's usual cell-language picker: `csharp`, `pysharp`,
  `ontly` or `ralf`.
- Run a cell with the usual ▷ button/`Ctrl+Enter`. The first cell of a session starts a
  `KerNet.Host` process for that notebook (visible, if needed, in the **KerNet** output channel —
  stderr from the host lands there); it stays alive, keeping every kernel's state, until the
  notebook is closed or you run **KerNet: Restart Kernel Host**.

By default the extension looks for the host at `<extension folder>/host/KerNet.Host.dll` (produced
by `build/package-extension.ps1`, see below) — while developing, point
`kernet.hostDll` (a VS Code setting) at
`src/KerNet.Host/bin/Debug/net10.0/KerNet.Host.dll` instead.

## Cell examples

```csharp
var greeting = "hello";
Display.Html($"<b>{greeting}</b>");
greeting.Length
```

```pysharp
message = "hello from PySharp"
print(message)
display_html(f"<i>{message}</i>")
```

```ontly
context: tour
namespace: KerNet.Tour
scalars:
  Email:
    base: string
    maxLength: 200
types:
  Person:
    kind: dto
    attributes:
      name: string
      email: Email
```

```ralf
List the cells in this notebook, then run the first PySharp cell and tell me what it printed.
```

## Packaging

```powershell
./build/pack.ps1               # dotnet pack every KerNet.* project -> D:\dev\NuGetLocalFeed
./build/package-extension.ps1  # publish KerNet.Host into vscode-extension/host + build a .vsix
```
