#!/usr/bin/env pwsh
# Packs every KerNet.* library/host into D:\dev\NuGetLocalFeed, the same feed
# PySharp/Ontly/RalfAI already publish to (see NuGet.Config at the repo root).
param(
    [string]$Configuration = "Release",
    [string]$OutputFeed = "D:\dev\NuGetLocalFeed"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot

$projects = @(
    "src/KerNet.Protocol/KerNet.Protocol.csproj",
    "src/KerNet.Kernels.Abstractions/KerNet.Kernels.Abstractions.csproj",
    "src/KerNet.Kernels.CSharp/KerNet.Kernels.CSharp.csproj",
    "src/KerNet.Kernels.PySharp/KerNet.Kernels.PySharp.csproj",
    "src/KerNet.Kernels.Ontly/KerNet.Kernels.Ontly.csproj",
    "src/KerNet.Kernels.Ralf/KerNet.Kernels.Ralf.csproj",
    "src/KerNet.Host/KerNet.Host.csproj"
)

foreach ($project in $projects) {
    $path = Join-Path $root $project
    Write-Host "==> dotnet pack $project" -ForegroundColor Cyan
    dotnet pack $path -c $Configuration -o $OutputFeed
}
