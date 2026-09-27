import * as path from "path";
import * as vscode from "vscode";
import { HostClient } from "./hostClient";
import { HostEvent, NotebookCellSnapshot } from "./protocol";

const SUPPORTED_LANGUAGES = ["csharp", "pysharp", "ontly", "ralf"];

/**
 * One `NotebookController` for all four KerNet languages (matching how a polyglot notebook picks
 * a language per cell rather than per kernel). Keeps one `HostClient` — one `KerNet.Host` process —
 * per open notebook document, so kernel session state (C#/PySharp variables, the Ralf agent) lives
 * for as long as the notebook stays open.
 */
export class KerNetController implements vscode.Disposable {
    readonly controller: vscode.NotebookController;
    private readonly clients = new Map<string, HostClient>();
    private readonly outputChannel: vscode.OutputChannel;
    private executionOrder = 0;
    private readonly disposables: vscode.Disposable[] = [];

    constructor(private readonly context: vscode.ExtensionContext) {
        this.outputChannel = vscode.window.createOutputChannel("KerNet");
        this.controller = vscode.notebooks.createNotebookController("kernet-controller", "kernet-notebook", "KerNet");
        this.controller.supportedLanguages = SUPPORTED_LANGUAGES;
        this.controller.supportsExecutionOrder = true;
        this.controller.executeHandler = (cells, notebook) => this.executeAll(cells, notebook);

        this.disposables.push(
            vscode.workspace.onDidCloseNotebookDocument((notebook) => this.disposeClient(notebook))
        );
    }

    /** Kills and forgets the host process for one notebook — used on close and by "KerNet: Restart Kernel Host". */
    disposeClient(notebook: vscode.NotebookDocument): void {
        const key = notebook.uri.toString();
        this.clients.get(key)?.dispose();
        this.clients.delete(key);
    }

    private snapshotCells(notebook: vscode.NotebookDocument): NotebookCellSnapshot[] {
        return notebook
            .getCells()
            .filter((cell) => cell.kind === vscode.NotebookCellKind.Code)
            .map((cell) => ({ index: cell.index, language: cell.document.languageId, code: cell.document.getText() }));
    }

    private getClient(notebook: vscode.NotebookDocument): HostClient {
        const key = notebook.uri.toString();
        let client = this.clients.get(key);
        if (!client) {
            const config = vscode.workspace.getConfiguration("kernet");
            const dotnetPath = config.get<string>("dotnetPath", "dotnet");
            const hostDll = this.resolveHostDll(config.get<string>("hostDll", ""));
            const cwd = notebook.uri.scheme === "file" ? path.dirname(notebook.uri.fsPath) : this.context.extensionPath;

            client = new HostClient(dotnetPath, hostDll, cwd, (text) => this.outputChannel.append(text));
            client.onEditCell((cellIndex, newCode) => void this.applyEditCell(notebook, cellIndex, newCode));
            this.clients.set(key, client);
        }
        return client;
    }

    private resolveHostDll(configured: string): string {
        return configured || path.join(this.context.extensionPath, "host", "KerNet.Host.dll");
    }

    private async applyEditCell(notebook: vscode.NotebookDocument, cellIndex: number, newCode: string): Promise<void> {
        const cell = notebook.cellAt(cellIndex);
        if (!cell) return;
        const edit = new vscode.WorkspaceEdit();
        const fullRange = new vscode.Range(0, 0, cell.document.lineCount, 0);
        edit.replace(cell.document.uri, fullRange, newCode);
        await vscode.workspace.applyEdit(edit);
    }

    private async executeAll(cells: vscode.NotebookCell[], notebook: vscode.NotebookDocument): Promise<void> {
        for (const cell of cells) {
            await this.executeCell(cell, notebook);
        }
    }

    private async executeCell(cell: vscode.NotebookCell, notebook: vscode.NotebookDocument): Promise<void> {
        const execution = this.controller.createNotebookCellExecution(cell);
        execution.executionOrder = ++this.executionOrder;
        execution.start(Date.now());
        await execution.clearOutput();

        const client = this.getClient(notebook);
        let ok = true;

        // Events stream in as they happen; appendOutput calls are chained (not just awaited
        // individually) so a burst of quick events — the "ralf" kernel's progress lines in
        // particular — lands in the editor in the order the host sent it.
        let chain: Promise<void> = Promise.resolve();
        await client.execute(cell.document.languageId, cell.document.getText(), this.snapshotCells(notebook), (evt: HostEvent) => {
            chain = chain.then(async () => {
                const item = this.toOutputItem(evt);
                if (item) await execution.appendOutput(new vscode.NotebookCellOutput([item]));
                if (evt.event === "complete") ok = evt.status !== "error";
            });
        });
        await chain;

        execution.end(ok, Date.now());
    }

    private toOutputItem(evt: HostEvent): vscode.NotebookCellOutputItem | undefined {
        switch (evt.event) {
            case "output":
                return vscode.NotebookCellOutputItem.text(evt.data ?? "", evt.mimeType ?? "text/plain");
            case "error": {
                const error = new Error(evt.message ?? "Unknown error");
                if (evt.stackTrace) error.stack = evt.stackTrace;
                return vscode.NotebookCellOutputItem.error(error);
            }
            default:
                return undefined;
        }
    }

    dispose(): void {
        for (const client of this.clients.values()) client.dispose();
        this.clients.clear();
        this.controller.dispose();
        this.outputChannel.dispose();
        for (const d of this.disposables) d.dispose();
    }
}
