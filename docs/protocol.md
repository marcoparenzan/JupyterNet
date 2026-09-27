# KerNet host protocol

NDJSON (one JSON object per line) over the stdin/stdout of a `KerNet.Host` process. One host
process per open notebook; the VS Code extension spawns it and owns its lifetime. No LSP-style
`Content-Length` framing — line-delimited JSON was simple enough on both the C# and TypeScript
sides that framing would only have added ceremony for an MVP.

Message shapes are defined once, in C#, in [`src/KerNet.Protocol/Messages.cs`](../src/KerNet.Protocol/Messages.cs);
[`vscode-extension/src/protocol.ts`](../vscode-extension/src/protocol.ts) mirrors them by hand.
There is no shared schema/codegen step — if you change one side, change the other.

## Requests (extension → host, stdin)

```jsonc
// Run a cell. `cells` is the *entire* notebook's current state (index/language/code for every
// code cell), sent with every request — not just "ralf" ones — so the host's cache of "what does
// the notebook currently look like" never goes stale. Only the "ralf" kernel's Notebook_* tools
// read it; the other three kernels ignore the field.
{ "id": "3", "method": "execute", "params": { "kernel": "csharp", "code": "1 + 1", "cells": [
  { "index": 0, "language": "csharp", "code": "1 + 1" }
] } }

// Terminate the host process's read loop (it then exits).
{ "id": "4", "method": "shutdown" }
```

## Events (host → extension, stdout)

Every event carries `executionId`, the `id` of the request it answers — except `editCell`, which
is not tied to one specific request (see below) and carries `executionId: ""`.

```jsonc
// Zero or more per execution, in order. mimeType is "text/plain" or "text/html".
{ "event": "output", "executionId": "3", "mimeType": "text/plain", "data": "2" }
{ "event": "output", "executionId": "3", "mimeType": "text/html",  "data": "<b>hi</b>" }

// A kernel reported a failure. Does not by itself end the execution — the host still always
// follows with exactly one "complete" (its status reflects whether any "error" event was sent).
{ "event": "error", "executionId": "3", "message": "...", "stackTrace": "..." }

// Exactly one per execution, always last.
{ "event": "complete", "executionId": "3", "status": "ok" } // or "error"

// Sent when the "ralf" kernel's Notebook_SetCellCode tool runs. Not correlated to a request id
// (Ralf can edit a cell from *any* of its turns, not just the one the extension is currently
// awaiting) — the extension applies it to cellIndex's document via a WorkspaceEdit as soon as it
// arrives, independent of whatever execute() call is in flight.
{ "event": "editCell", "executionId": "", "cellIndex": 0, "newCode": "2 + 2" }
```

## Kernel ids

`csharp`, `pysharp`, `ontly`, `ralf` — see `KerNet.Protocol.KernelIds` / `protocol.ts`'s `KernelIds`.
Each is a language id a notebook cell can be set to; `KerNet.Host` creates the matching `IKernel`
lazily, the first time that language is used in a session, and keeps it (and its state) for the
rest of the session.
