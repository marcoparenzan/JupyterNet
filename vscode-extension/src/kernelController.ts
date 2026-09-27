import * as fs from "fs";
import * as path from "path";
import * as vscode from "vscode";
import { HostClient } from "./hostClient";
import { HostEvent, NotebookCellSnapshot } from "./protocol";

const SUPPORTED_LANGUAGES = ["csharp", "fsharp", "pysharp", "ontly", "ralf"];

/**
 * One `NotebookController` for all four JupyterNet languages (matching how a polyglot notebook picks
 * a language per cell rather than per kernel). Keeps one `HostClient` — one `JupyterNet.Host` process —
 * per open notebook document, so kernel session state (C#/PySharp variables, the Ralf agent) lives
 * for as long as the notebook stays open.
 */
export class JupyterNetController implements vscode.Disposable {
    readonly controller: vscode.NotebookController;
    private readonly clients = new Map<string, HostClient>();
    private readonly outputChannel: vscode.OutputChannel;
    private executionOrder = 0;
    private readonly disposables: vscode.Disposable[] = [];

    constructor(private readonly context: vscode.ExtensionContext) {
        this.outputChannel = vscode.window.createOutputChannel("JupyterNet");
        this.controller = vscode.notebooks.createNotebookController("jupyternet-controller", "jupyternet-notebook", "JupyterNet");
        this.controller.supportedLanguages = SUPPORTED_LANGUAGES;
        this.controller.supportsExecutionOrder = true;
        this.controller.executeHandler = (cells, notebook) => this.executeAll(cells, notebook);

        this.disposables.push(
            vscode.workspace.onDidCloseNotebookDocument((notebook) => this.disposeClient(notebook))
        );
    }

    /** Kills and forgets the host process for one notebook — used on close and by "JupyterNet: Restart Kernel Host". */
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
            const config = vscode.workspace.getConfiguration("jupyternet");
            const dotnetPath = config.get<string>("dotnetPath", "dotnet");
            const hostDll = this.resolveHostDll(config.get<string>("hostDll", ""));
            const cwd = notebook.uri.scheme === "file" ? path.dirname(notebook.uri.fsPath) : this.context.extensionPath;
            const kernelPaths = config.get<string[]>("kernelPaths", []);
            const env = { ...process.env };
            if (kernelPaths.length > 0) env.JUPYTERNET_KERNEL_PATHS = kernelPaths.join(path.delimiter);

            client = new HostClient(dotnetPath, hostDll, cwd, env, (text) => this.outputChannel.append(text));
            client.onEditCell((cellIndex, newCode) => void this.applyEditCell(notebook, cellIndex, newCode));
            this.clients.set(key, client);
        }
        return client;
    }

    /**
     * `jupyternet.hostDll` wins if set. Otherwise prefer the copy bundled next to the extension
     * (`host/JupyterNet.Host.dll`, produced by build/package-extension.ps1); if that doesn't exist —
     * e.g. running the extension straight from source via F5 — fall back to the repo's own
     * Debug/Release build output, since settings.json values aren't variable-substituted the way
     * launch.json/tasks.json ones are and so can't point here by themselves.
     */
    private resolveHostDll(configured: string): string {
        if (configured) return configured;

        const bundled = path.join(this.context.extensionPath, "host", "JupyterNet.Host.dll");
        if (fs.existsSync(bundled)) return bundled;

        for (const configuration of ["Debug", "Release"]) {
            const devBuild = path.join(this.context.extensionPath, "..", "src", "JupyterNet.Host", "bin", configuration, "net10.0", "JupyterNet.Host.dll");
            if (fs.existsSync(devBuild)) return devBuild;
        }

        return bundled;
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
