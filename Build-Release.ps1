[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repoRoot = $PSScriptRoot
$project = Join-Path $repoRoot 'AgentCake\src\AgentCake.csproj'
$releaseRoot = Join-Path $repoRoot 'release'
$publishDir = Join-Path $releaseRoot 'AgentCake'

if (Test-Path -LiteralPath $publishDir) {
    Remove-Item -LiteralPath $publishDir -Recurse -Force
}

New-Item -ItemType Directory -Force -Path $publishDir | Out-Null

dotnet publish $project `
    --configuration Release `
    --runtime win-x64 `
    --self-contained true `
    --output $publishDir `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=None `
    -p:DebugSymbols=false

Copy-Item -LiteralPath (Join-Path $repoRoot 'Install-AgentCake.ps1') -Destination (Join-Path $releaseRoot 'Install-AgentCake.ps1') -Force
Copy-Item -LiteralPath (Join-Path $repoRoot 'install.bat') -Destination (Join-Path $releaseRoot 'install.bat') -Force
Copy-Item -LiteralPath (Join-Path $repoRoot 'README.md') -Destination (Join-Path $releaseRoot 'README.md') -Force
Copy-Item -LiteralPath (Join-Path $repoRoot 'LICENSE') -Destination (Join-Path $releaseRoot 'LICENSE') -Force

Write-Host "Release created: $releaseRoot"
Write-Host 'Run release\install.bat to install AgentCake for the current user.'
