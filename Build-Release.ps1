[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repoRoot = $PSScriptRoot
$project = Join-Path $repoRoot 'AgentCake\src\AgentCake.csproj'
$releaseRoot = Join-Path $repoRoot 'release'
$publishDir = Join-Path $releaseRoot 'AgentCake'
[xml]$projectFile = Get-Content -LiteralPath $project
$version = $projectFile.Project.PropertyGroup.Version | Select-Object -First 1
$archivePath = Join-Path $releaseRoot ("AgentCake-v{0}-win-x64.zip" -f $version)

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

if (Test-Path -LiteralPath $archivePath) {
    Remove-Item -LiteralPath $archivePath -Force
}
Compress-Archive -Path @(
    $publishDir,
    (Join-Path $releaseRoot 'Install-AgentCake.ps1'),
    (Join-Path $releaseRoot 'install.bat'),
    (Join-Path $releaseRoot 'README.md'),
    (Join-Path $releaseRoot 'LICENSE')
) -DestinationPath $archivePath -CompressionLevel Optimal

Write-Host "Release created: $releaseRoot"
Write-Host "Release archive: $archivePath"
Write-Host 'Run release\install.bat to install AgentCake for the current user.'
