#!/usr/bin/env pwsh
# Packs every JupyterNet.* library/host into D:\dev\NuGetLocalFeed, the same feed
# PySharp/Ontly/RalfAI already publish to (see NuGet.Config at the repo root).
#
# The three kernel plugins (PySharp/Ontly/Ralf) live in their own repos now — this only packs
# what actually lives here: the protocol, the plugin contract, and the four builtin kernels
# (csharp/fsharp/powershell/powerfx) plus the host itself.
param(
    [string]$Configuration = "Release",
    [string]$OutputFeed = "D:\dev\NuGetLocalFeed"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot

$projects = @(
    "src/JupyterNet.Protocol/JupyterNet.Protocol.csproj",
    "src/JupyterNet.Kernels.Abstractions/JupyterNet.Kernels.Abstractions.csproj",
    "src/JupyterNet.Kernels.CSharp/JupyterNet.Kernels.CSharp.csproj",
    "src/JupyterNet.Kernels.FSharp/JupyterNet.Kernels.FSharp.csproj",
    "src/JupyterNet.Kernels.PowerShell/JupyterNet.Kernels.PowerShell.csproj",
    "src/JupyterNet.Kernels.PowerFx/JupyterNet.Kernels.PowerFx.csproj",
    "src/JupyterNet.Engine/JupyterNet.Engine.csproj",
    "src/JupyterNet.Host/JupyterNet.Host.csproj",
    "src/JupyterNet.Cli/JupyterNet.Cli.csproj"
)

foreach ($project in $projects) {
    $path = Join-Path $root $project
    Write-Host "==> dotnet pack $project" -ForegroundColor Cyan
    dotnet pack $path -c $Configuration -o $OutputFeed
}
