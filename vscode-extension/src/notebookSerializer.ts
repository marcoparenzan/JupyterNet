import * as vscode from "vscode";

/**
 * Real Jupyter nbformat v4 (`.ipynb`) — so a JupyterNet notebook opens, renders and diffs like any
 * other notebook file, not a bespoke format only this extension understands. Per-cell language is
 * the one thing plain nbformat has no field for (a `.ipynb` normally has one language for the
 * whole file, in `metadata.language_info`): stored per cell under `metadata.vscode.languageId`,
 * the same convention VS Code's own built-in notebook tooling uses for exactly this gap.
 */

interface NbOutput {
    output_type: "stream" | "error" | "execute_result" | "display_data";
    name?: string;
    text?: string[];
    data?: Record<string, string[]>;
    ename?: string;
    evalue?: string;
    traceback?: string[];
    metadata?: Record<string, unknown>;
    execution_count?: number | null;
}

interface NbCell {
    cell_type: "code" | "markdown";
    source: string[];
    metadata?: { vscode?: { languageId?: string }; [key: string]: unknown };
    outputs?: NbOutput[];
    execution_count?: number | null;
}

interface NbFormat {
    cells: NbCell[];
    metadata: Record<string, unknown>;
    nbformat: number;
    nbformat_minor: number;
}

const DEFAULT_CODE_LANGUAGE = "csharp";
const ERROR_MIME = "application/vnd.code.notebook.error";

export class JupyterNetNotebookSerializer implements vscode.NotebookSerializer {
    deserializeNotebook(content: Uint8Array): vscode.NotebookData {
        const text = Buffer.from(content).toString("utf8").trim();
        const nb: NbFormat = text.length === 0 ? emptyNotebook() : JSON.parse(text);

        const cells = nb.cells.map((cell) => {
            const kind = cell.cell_type === "markdown" ? vscode.NotebookCellKind.Markup : vscode.NotebookCellKind.Code;
            const language = cell.cell_type === "markdown" ? "markdown" : cell.metadata?.vscode?.languageId ?? DEFAULT_CODE_LANGUAGE;
            const cellData = new vscode.NotebookCellData(kind, joinSource(cell.source), language);
            cellData.outputs = (cell.outputs ?? []).map(fromNbOutput);
            return cellData;
        });

        const notebookData = new vscode.NotebookData(cells);
        notebookData.metadata = nb.metadata;
        return notebookData;
    }

    serializeNotebook(data: vscode.NotebookData): Uint8Array {
        const nb: NbFormat = {
            cells: data.cells.map(toNbCell),
            metadata: {
                ...(data.metadata ?? {}),
                kernelspec: { name: "jupyternet", display_name: "JupyterNet", language: "jupyternet" },
                language_info: { name: "jupyternet" }
            },
            nbformat: 4,
            nbformat_minor: 5
        };
        return Buffer.from(JSON.stringify(nb, null, 1), "utf8");
    }
}

function emptyNotebook(): NbFormat {
    return { cells: [], metadata: {}, nbformat: 4, nbformat_minor: 5 };
}

/** nbformat's `source`/`text` convention: an array of lines, each keeping its own trailing "\n" except possibly the last. */
function splitSource(value: string): string[] {
    return value.length === 0 ? [] : value.split(/(?<=\n)/);
}

function joinSource(source: string[] | string | undefined): string {
    if (source === undefined) return "";
    return Array.isArray(source) ? source.join("") : source;
}

function toNbCell(cell: vscode.NotebookCellData): NbCell {
    const isMarkdown = cell.kind === vscode.NotebookCellKind.Markup;
    return {
        cell_type: isMarkdown ? "markdown" : "code",
        source: splitSource(cell.value),
        metadata: isMarkdown ? {} : { vscode: { languageId: cell.languageId } },
        ...(isMarkdown ? {} : { outputs: (cell.outputs ?? []).map(toNbOutput), execution_count: null })
    };
}

function toNbOutput(output: vscode.NotebookCellOutput): NbOutput {
    const errorItem = output.items.find((item) => item.mime === ERROR_MIME);
    if (errorItem) {
        const error = JSON.parse(Buffer.from(errorItem.data).toString("utf8")) as { name?: string; message?: string; stack?: string };
        return {
            output_type: "error",
            ename: error.name ?? "Error",
            evalue: error.message ?? "",
            traceback: error.stack ? splitSource(error.stack) : []
        };
    }

    const data: Record<string, string[]> = {};
    for (const item of output.items) {
        data[item.mime] = splitSource(Buffer.from(item.data).toString("utf8"));
    }
    return { output_type: "display_data", data, metadata: {} };
}

function fromNbOutput(output: NbOutput): vscode.NotebookCellOutput {
    if (output.output_type === "error") {
        const error = new Error(output.evalue ?? "");
        error.name = output.ename ?? "Error";
        if (output.traceback) error.stack = output.traceback.join("");
        return new vscode.NotebookCellOutput([vscode.NotebookCellOutputItem.error(error)]);
    }

    if (output.output_type === "stream") {
        return new vscode.NotebookCellOutput([vscode.NotebookCellOutputItem.text(joinSource(output.text), "text/plain")]);
    }

    const items = Object.entries(output.data ?? {}).map(([mime, lines]) => vscode.NotebookCellOutputItem.text(joinSource(lines), mime));
    return new vscode.NotebookCellOutput(items);
}
