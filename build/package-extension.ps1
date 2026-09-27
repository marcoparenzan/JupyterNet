#!/usr/bin/env pwsh
# Publishes JupyterNet.Host (and the three external kernel plugins) into vscode-extension/host
# (framework-dependent — the target machine needs the .NET 10 runtime, same as every other tool
# in D:\dev), then builds and packages the VS Code extension as a .vsix. Requires Node.js/npm on
# PATH.
#
# The plugins are bundled here (not just left in JUPYTERNET_KERNEL_PATHS) so a packaged .vsix is
# self-contained and doesn't need the PySharp/ontly/RalfAI repos present at runtime; the extension
# still defaults `jupyternet.kernelPaths` to those repos' own publish output for local dev (see
# kernelController.ts) — the bundled copies under host/kernels/ are what a plain install without
# that setting override falls back to instead once the extension resolves `kernels/*` next to
# JupyterNet.Host (see KernelPluginLoader's default search path).
param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$extensionDir = Join-Path $root "vscode-extension"
$hostOut = Join-Path $extensionDir "host"
$kernelsOut = Join-Path $hostOut "kernels"

Write-Host "==> dotnet publish JupyterNet.Host -> $hostOut" -ForegroundColor Cyan
dotnet publish (Join-Path $root "src/JupyterNet.Host/JupyterNet.Host.csproj") -c $Configuration -o $hostOut --self-contained false

$plugins = @(
    @{ Name = "pysharp"; Project = "D:\dev\2026\repos\PySharp\src\JupyterNet.Kernels.PySharp\JupyterNet.Kernels.PySharp.csproj" }
    @{ Name = "ontly";   Project = "D:\dev\2026\repos\ontly\src\JupyterNet.Kernels.Ontly\JupyterNet.Kernels.Ontly.csproj" }
    @{ Name = "ralf";    Project = "D:\dev\MarcoParenzan\RalfAI\src\JupyterNet.Kernels.Ralf\JupyterNet.Kernels.Ralf.csproj" }
)
foreach ($plugin in $plugins) {
    $pluginOut = Join-Path $kernelsOut $plugin.Name
    Write-Host "==> dotnet publish $($plugin.Name) plugin -> $pluginOut" -ForegroundColor Cyan
    dotnet publish $plugin.Project -c $Configuration -o $pluginOut --self-contained false
}

Push-Location $extensionDir
try {
    Write-Host "==> npm install" -ForegroundColor Cyan
    npm install

    Write-Host "==> npm run compile" -ForegroundColor Cyan
    npm run compile

    Write-Host "==> vsce package" -ForegroundColor Cyan
    npx --yes @vscode/vsce package
}
finally {
    Pop-Location
}
