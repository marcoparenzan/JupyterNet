# Usage

## Prerequisites

- .NET SDK 10.
- Node.js + npm, to build the VS Code extension.
- `D:\dev\NuGetLocalFeed` reachable and containing `PySharp.Interpreter`, `Ontly.Domain`,
  `Ontly.CSharp`, `Ontly.Python`, `RalfAI.Core`, `RalfAI.Abstractions`, `RalfAI.Providers`, and
  `JupyterNet.Kernels.Abstractions` (pack the last one from this repo — see Packaging below — the
  other six are already published there as of this writing).
- The three kernel plugins built and published once (they live in their own repos, not this one):

  ```powershell
  dotnet publish D:\dev\2026\repos\PySharp\src\JupyterNet.Kernels.PySharp -c Release -o D:\dev\2026\repos\PySharp\src\JupyterNet.Kernels.PySharp\bin\publish --self-contained false
  dotnet publish D:\dev\2026\repos\ontly\src\JupyterNet.Kernels.Ontly -c Release -o D:\dev\2026\repos\ontly\src\JupyterNet.Kernels.Ontly\bin\publish --self-contained false
  dotnet publish D:\dev\MarcoParenzan\RalfAI\src\JupyterNet.Kernels.Ralf -c Release -o D:\dev\MarcoParenzan\RalfAI\src\JupyterNet.Kernels.Ralf\bin\publish --self-contained false
  ```

  These exact paths are the extension's `jupyternet.kernelPaths` default — nothing else to
  configure if you publish there. `pysharp`/`ontly`/`ralf` cells simply fail with "Unknown kernel"
  until this step has been done at least once; rerun it after changing any of those three plugins
  or the engine they wrap.
- For `ralf` cells specifically: an existing RalfAI configuration — either `RALFAI_CONFIG_PATH`
  pointing at a working `config.json` (the same one `ralf`/`ralf-studio` use), or an
  `appsettings.json` next to `JupyterNet.Host.dll` with the same
  `AIProvider`/`OpenAI`|`Anthropic`|`AzureAIFoundry` shape. Without either, `ralf` cells fail with
  a clear error instead of crashing the host.

## Build, test, and smoke-test the host

```powershell
dotnet build JupyterNet.slnx
dotnet test tests/JupyterNet.Tests
```

`JupyterNet.Host` reads NDJSON requests on stdin and writes NDJSON events on stdout — see
[docs/protocol.md](docs/protocol.md). To try it directly without VS Code (`csharp`/`fsharp` work
with no further setup; `pysharp`/`ontly`/`ralf` need the plugin publish step above and
`JUPYTERNET_KERNEL_PATHS` pointed at those three `bin/publish` folders, `;`-separated):

```powershell
'{"id":"1","method":"execute","params":{"kernel":"csharp","code":"21 * 2"}}' | dotnet run --project src/JupyterNet.Host
```

## Run a notebook headlessly (CLI)

No VS Code, no editor — `jupyternet run` executes a `.ipynb`'s cells top to bottom and exits `0`
only if every one of them ran without error (handy in CI, or just from a terminal):

```powershell
dotnet run --project src/JupyterNet.Cli -- run samples/tour.ipynb

# pysharp/ontly/ralf need the same plugin paths as the host/extension:
dotnet run --project src/JupyterNet.Cli -- run samples/tour.ipynb `
  --kernel-paths "D:\dev\2026\repos\PySharp\src\JupyterNet.Kernels.PySharp\bin\publish;D:\dev\2026\repos\ontly\src\JupyterNet.Kernels.Ontly\bin\publish;D:\dev\MarcoParenzan\RalfAI\src\JupyterNet.Kernels.Ralf\bin\publish"

# --output writes the executed notebook (with real outputs) elsewhere; --in-place overwrites the input
dotnet run --project src/JupyterNet.Cli -- run samples/tour.ipynb --output executed.ipynb
```

`--fail-fast` stops at the first cell whose output is an error instead of running the rest.
Without `--kernel-paths`, it falls back to `JUPYTERNET_KERNEL_PATHS`/the same `kernels/` default
directory the host uses.

## Embed a notebook in a .NET application

[samples/EmbeddingSample](samples/EmbeddingSample) is the whole story in one file: reference
`JupyterNet.Engine`, create a `NotebookSession`, inject a live object into a kernel's context, run
some code that uses it.

```csharp
using JupyterNet.Engine;

var session = new NotebookSession();
await session.SetVariableAsync("csharp", "weather", new Weather());   // a plain .NET object
await session.ExecuteAsync("csharp", "weather.City", sink, default);  // -> "Trento"
```

```powershell
dotnet run --project samples/EmbeddingSample
```

`SetVariableAsync` only works on a kernel that implements `IVariableInjectable` — today that's
`csharp` and `fsharp` (the two builtin kernels); a kernel that doesn't throws `NotSupportedException`
rather than silently doing nothing. See ARCHITECTURE.md's "Engine, CLI and embedding" section for
how each of the two actually makes an injected object usable from cell code.

## Install the VS Code extension

```powershell
cd vscode-extension
npm install
npm run compile
```

Then, in VS Code, open the `vscode-extension` folder and press F5 (`launch.json` is already set
up — it runs `npm: compile` first and opens `samples/tour.ipynb` in the new window). In that
window:

- `.ipynb` is registered with `priority: "option"` (see [ARCHITECTURE.md](ARCHITECTURE.md)) —
  JupyterNet is offered as a choice, not forced as the default, so if another extension (e.g. Jupyter)
  also handles `.ipynb` you may need **right-click → Open With... → JupyterNet Notebook** the first
  time, or set `"workbench.editorAssociations": { "*.ipynb": "jupyternet-notebook" }` to make it the
  default for every `.ipynb` in that workspace.
- Run **JupyterNet: New Notebook**, or open `samples/tour.ipynb` from the repo root.
- Each cell picks its language from VS Code's usual cell-language picker: `csharp`, `fsharp`,
  `pysharp`, `ontly` or `ralf`.
- Run a cell with the usual ▷ button/`Ctrl+Enter`. The first cell of a session starts a
  `JupyterNet.Host` process for that notebook (visible, if needed, in the **JupyterNet** output channel —
  stderr from the host lands there, including a warning per kernel plugin that failed to load);
  it stays alive, keeping every kernel's state, until the notebook is closed or you run
  **JupyterNet: Restart Kernel Host**.

The extension looks for the host in this order: the `jupyternet.hostDll` setting if you set one; else
`<extension folder>/host/JupyterNet.Host.dll` (produced by `build/package-extension.ps1`, see below);
else it falls back automatically to `src/JupyterNet.Host/bin/{Debug,Release}/net10.0/JupyterNet.Host.dll`
in the repo, so a plain `dotnet build` is enough while developing — no setting to configure.

`jupyternet.kernelPaths` (an array setting) is where the `pysharp`/`ontly`/`ralf` plugins are found —
forwarded to the host as `JUPYTERNET_KERNEL_PATHS`. It defaults to this machine's three plugin
repos' own `bin/publish` output (see Prerequisites); override it if those repos live elsewhere, or
point it at `<extension folder>/host/kernels/*` if you installed a packaged `.vsix` that bundled
them (see Packaging).

## Cell examples

```csharp
var greeting = "hello";
Display.Html($"<b>{greeting}</b>");
greeting.Length
```

```fsharp
let greeting = "hello from F#"
printfn "%s" greeting
```

```pysharp
message = "hello from PySharp"
print(message)
display_html(f"<i>{message}</i>")
```

```ontly
context: tour
namespace: JupyterNet.Tour
scalars:
  Email:
    base: string
    maxLength: 200
  Name:
    base: string
    maxLength: 100
types:
  Person:
    kind: dto
    attributes:
      name: Name
      email: Email
```

```ralf
List the cells in this notebook, then run the first PySharp cell and tell me what it printed.
```

## Packaging

```powershell
./build/pack.ps1               # dotnet pack this repo's own projects (incl. Engine, Cli) -> D:\dev\NuGetLocalFeed
./build/package-extension.ps1  # publish JupyterNet.Host + the 3 plugins into vscode-extension/host + build a .vsix
```

`package-extension.ps1` bundles the three plugins into `vscode-extension/host/kernels/<name>/` so
a packaged `.vsix` works standalone, without the PySharp/ontly/RalfAI repos present — that bundled
path is exactly `KernelPluginLoader`'s fallback (`<host base dir>/kernels/*`), used automatically
whenever `jupyternet.kernelPaths` isn't set to something else.
