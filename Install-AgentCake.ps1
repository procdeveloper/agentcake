[CmdletBinding()]
param(
    [switch]$NoStart
)

$ErrorActionPreference = 'Stop'
$sourceExe = @(
    (Join-Path $PSScriptRoot 'AgentCake.exe'),
    (Join-Path $PSScriptRoot 'AgentCake\AgentCake.exe'),
    (Join-Path $PSScriptRoot 'release\AgentCake\AgentCake.exe')
) | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1

if (-not (Test-Path -LiteralPath $sourceExe)) {
    throw 'AgentCake.exe was not found. Run Build-Release.ps1 first, then run release\install.bat.'
}

$sourceDir = Split-Path -Parent $sourceExe

$installDir = Join-Path $env:LOCALAPPDATA 'AgentCake'
$installedExe = Join-Path $installDir 'AgentCake.exe'
$runKeyPath = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$runValueName = 'AgentCake'

Get-Process -Name AgentCake -ErrorAction SilentlyContinue | Stop-Process -Force
New-Item -ItemType Directory -Force -Path $installDir | Out-Null
Copy-Item -Path (Join-Path $sourceDir '*') -Destination $installDir -Recurse -Force

New-Item -Path $runKeyPath -Force | Out-Null
Set-ItemProperty -Path $runKeyPath -Name $runValueName -Value ('"{0}"' -f $installedExe)

if (-not $NoStart) {
    Start-Process -FilePath $installedExe -WorkingDirectory $installDir -WindowStyle Hidden
}

Write-Host "Installed AgentCake: $installedExe"
Write-Host 'It will run automatically when you sign in.'
