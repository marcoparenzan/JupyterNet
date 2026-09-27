#!/usr/bin/env pwsh
# Publishes KerNet.Host into vscode-extension/host (framework-dependent — the target machine
# needs the .NET 10 runtime, same as every other tool in D:\dev), then builds and packages the
# VS Code extension as a .vsix. Requires Node.js/npm on PATH.
param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$extensionDir = Join-Path $root "vscode-extension"
$hostProject = Join-Path $root "src/KerNet.Host/KerNet.Host.csproj"
$hostOut = Join-Path $extensionDir "host"

Write-Host "==> dotnet publish KerNet.Host -> $hostOut" -ForegroundColor Cyan
dotnet publish $hostProject -c $Configuration -o $hostOut --self-contained false

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
