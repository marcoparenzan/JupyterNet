import * as vscode from "vscode";

interface RawCell {
    kind: "code" | "markup";
    language: string;
    value: string;
}

interface RawNotebook {
    cells: RawCell[];
}

/** The `.kernet` file format: plain JSON, one entry per cell. Nothing fancier than that in v1. */
export class KerNetNotebookSerializer implements vscode.NotebookSerializer {
    deserializeNotebook(content: Uint8Array): vscode.NotebookData {
        const text = Buffer.from(content).toString("utf8").trim();
        const raw: RawNotebook = text.length === 0 ? { cells: [] } : JSON.parse(text);

        const cells = raw.cells.map(
            (cell) =>
                new vscode.NotebookCellData(
                    cell.kind === "markup" ? vscode.NotebookCellKind.Markup : vscode.NotebookCellKind.Code,
                    cell.value,
                    cell.language
                )
        );

        return new vscode.NotebookData(cells);
    }

    serializeNotebook(data: vscode.NotebookData): Uint8Array {
        const raw: RawNotebook = {
            cells: data.cells.map((cell) => ({
                kind: cell.kind === vscode.NotebookCellKind.Markup ? "markup" : "code",
                language: cell.languageId,
                value: cell.value
            }))
        };

        return Buffer.from(JSON.stringify(raw, null, 2), "utf8");
    }
}
