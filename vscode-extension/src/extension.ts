import * as vscode from "vscode";
import { KerNetController } from "./kernelController";
import { KerNetNotebookSerializer } from "./notebookSerializer";

export function activate(context: vscode.ExtensionContext): void {
    context.subscriptions.push(vscode.workspace.registerNotebookSerializer("kernet-notebook", new KerNetNotebookSerializer()));

    const controller = new KerNetController(context);
    context.subscriptions.push(controller);

    context.subscriptions.push(
        vscode.commands.registerCommand("kernet.newNotebook", async () => {
            const data = new vscode.NotebookData([
                new vscode.NotebookCellData(vscode.NotebookCellKind.Markup, "# New KerNet notebook", "markdown"),
                new vscode.NotebookCellData(vscode.NotebookCellKind.Code, "1 + 1", "csharp")
            ]);
            const document = await vscode.workspace.openNotebookDocument("kernet-notebook", data);
            await vscode.window.showNotebookDocument(document);
        })
    );

    context.subscriptions.push(
        vscode.commands.registerCommand("kernet.restartHost", () => {
            const editor = vscode.window.activeNotebookEditor;
            if (!editor) {
                vscode.window.showWarningMessage("KerNet: open a KerNet notebook first.");
                return;
            }
            controller.disposeClient(editor.notebook);
            vscode.window.showInformationMessage("KerNet: kernel host restarted for this notebook.");
        })
    );
}

export function deactivate(): void {
    // Controller disposal (via context.subscriptions) already terminates every KerNet.Host process.
}
