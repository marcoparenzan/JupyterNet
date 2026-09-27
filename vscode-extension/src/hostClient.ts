import * as cp from "child_process";
import * as readline from "readline";
import { ExecuteParams, HostEvent, HostRequest, NotebookCellSnapshot } from "./protocol";

export type EditCellHandler = (cellIndex: number, newCode: string) => void;

/**
 * One KerNet.Host process per open notebook, talking NDJSON over its stdin/stdout. `execute`
 * resolves once the matching "complete" event comes back; every event in between (including
 * more than one "output") is streamed to `onEvent` as it arrives. "editCell" events are not tied
 * to a specific `execute` call (the host sends `executionId: ""` for them — see
 * NotebookHostState.RequestCellEdit) so they go through the separate `onEditCell` subscription.
 */
export class HostClient {
    private readonly process: cp.ChildProcessWithoutNullStreams;
    private nextId = 1;
    private readonly pending = new Map<string, (evt: HostEvent) => void>();
    private readonly editCellHandlers: EditCellHandler[] = [];

    constructor(dotnetPath: string, hostDllPath: string, cwd: string, onStderr: (text: string) => void) {
        this.process = cp.spawn(dotnetPath, [hostDllPath], { cwd });
        const rl = readline.createInterface({ input: this.process.stdout });
        rl.on("line", (line) => this.onLine(line));
        this.process.stderr.on("data", (chunk: Buffer) => onStderr(chunk.toString()));
    }

    onEditCell(handler: EditCellHandler): void {
        this.editCellHandlers.push(handler);
    }

    private onLine(line: string): void {
        if (!line.trim()) return;
        let evt: HostEvent;
        try {
            evt = JSON.parse(line);
        } catch {
            return;
        }

        if (evt.event === "editCell") {
            if (evt.cellIndex !== undefined && evt.newCode !== undefined) {
                for (const handler of this.editCellHandlers) handler(evt.cellIndex, evt.newCode);
            }
            return;
        }

        this.pending.get(evt.executionId)?.(evt);
    }

    execute(kernel: string, code: string, cells: NotebookCellSnapshot[], onEvent: (evt: HostEvent) => void): Promise<void> {
        const id = String(this.nextId++);
        return new Promise((resolve) => {
            this.pending.set(id, (evt) => {
                onEvent(evt);
                if (evt.event === "complete") {
                    this.pending.delete(id);
                    resolve();
                }
            });

            const params: ExecuteParams = { kernel, code, cells };
            const request: HostRequest = { id, method: "execute", params };
            this.process.stdin.write(JSON.stringify(request) + "\n");
        });
    }

    dispose(): void {
        try {
            const request: HostRequest = { id: "shutdown", method: "shutdown" };
            this.process.stdin.write(JSON.stringify(request) + "\n");
        } catch {
            // process may already be gone
        }
        this.process.kill();
    }
}
