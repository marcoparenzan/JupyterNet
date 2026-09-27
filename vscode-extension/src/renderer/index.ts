// Notebook output renderer for the "text/html" mime type JupyterNet.Host's kernels emit (Ontly's
// generated-code report, Ralf's Markdown-rendered answers, `Display.Html`/`display_html` calls).
// Runs inside the notebook's sandboxed renderer webview — untyped on purpose to avoid an extra
// @types/vscode-notebook-renderer dependency for what is a two-method contract.

interface OutputItemLike {
    text(): string;
}

export function activate() {
    return {
        renderOutputItem(outputItem: OutputItemLike, element: HTMLElement): void {
            let container = element.querySelector<HTMLDivElement>("div.jupyternet-html-output");
            if (!container) {
                container = document.createElement("div");
                container.className = "jupyternet-html-output";
                element.appendChild(container);
            }
            container.innerHTML = outputItem.text();
        },
        disposeOutputItem(): void {
            // Nothing to release — the element is torn down by the notebook UI itself.
        }
    };
}
