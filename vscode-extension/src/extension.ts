import * as vscode from "vscode";
import { JupyterNetController } from "./kernelController";
import { JupyterNetNotebookSerializer } from "./notebookSerializer";

export function activate(context: vscode.ExtensionContext): void {
    context.subscriptions.push(vscode.workspace.registerNotebookSerializer("jupyternet-notebook", new JupyterNetNotebookSerializer()));

    const controller = new JupyterNetController(context);
    context.subscriptions.push(controller);

    context.subscriptions.push(
        vscode.commands.registerCommand("jupyternet.newNotebook", async () => {
            const data = new vscode.NotebookData([
                new vscode.NotebookCellData(vscode.NotebookCellKind.Markup, "# New JupyterNet notebook", "markdown"),
                new vscode.NotebookCellData(vscode.NotebookCellKind.Code, "1 + 1", "csharp")
            ]);
            const document = await vscode.workspace.openNotebookDocument("jupyternet-notebook", data);
            await vscode.window.showNotebookDocument(document);
        })
    );

    context.subscriptions.push(
        vscode.commands.registerCommand("jupyternet.restartHost", () => {
            const editor = vscode.window.activeNotebookEditor;
            if (!editor) {
                vscode.window.showWarningMessage("JupyterNet: open a JupyterNet notebook first.");
                return;
            }
            controller.disposeClient(editor.notebook);
            vscode.window.showInformationMessage("JupyterNet: kernel host restarted for this notebook.");
        })
    );
}

export function deactivate(): void {
    // Controller disposal (via context.subscriptions) already terminates every JupyterNet.Host process.
}
