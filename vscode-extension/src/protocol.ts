// Mirrors src/KerNet.Protocol/Messages.cs — keep the two in sync by hand (there is no shared
// schema generation in this MVP; see docs/protocol.md).

export interface NotebookCellSnapshot {
    index: number;
    language: string;
    code: string;
}

export interface ExecuteParams {
    kernel: string;
    code: string;
    cells?: NotebookCellSnapshot[];
}

export interface HostRequest {
    id: string;
    method: "execute" | "shutdown";
    params?: ExecuteParams;
}

export interface HostEvent {
    event: "output" | "error" | "complete" | "editCell";
    executionId: string;
    mimeType?: string;
    data?: string;
    message?: string;
    stackTrace?: string;
    status?: "ok" | "error";
    cellIndex?: number;
    newCode?: string;
}

export const KernelIds = {
    CSharp: "csharp",
    PySharp: "pysharp",
    Ontly: "ontly",
    Ralf: "ralf"
} as const;
