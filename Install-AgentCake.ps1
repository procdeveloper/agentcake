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

function Stop-RunningAgentCake {
    $deadline = (Get-Date).AddSeconds(10)
    do {
        $runningAgentCake = Get-Process -Name AgentCake -ErrorAction SilentlyContinue
        if (-not $runningAgentCake) { return }
        $runningAgentCake | Stop-Process -Force -ErrorAction SilentlyContinue
        Start-Sleep -Milliseconds 250
    } while ((Get-Date) -lt $deadline)

    throw 'AgentCake did not exit within 10 seconds. Close it from the notification area and rerun the installer.'
}

Stop-RunningAgentCake
New-Item -ItemType Directory -Force -Path $installDir | Out-Null

# Windows can retain a just-closed executable handle briefly. Retry the copy so
# a normal update never leaves the existing tray app half-installed.
$copied = $false
for ($attempt = 1; $attempt -le 10; $attempt++) {
    try {
        Copy-Item -Path (Join-Path $sourceDir '*') -Destination $installDir -Recurse -Force
        $copied = $true
        break
    }
    catch [System.IO.IOException] {
        if ($attempt -eq 10) { throw }
        Start-Sleep -Milliseconds 500
    }
}
if (-not $copied) { throw 'AgentCake files could not be updated.' }

New-Item -Path $runKeyPath -Force | Out-Null
Set-ItemProperty -Path $runKeyPath -Name $runValueName -Value ('"{0}"' -f $installedExe)

if (-not $NoStart) {
    Start-Process -FilePath $installedExe -WorkingDirectory $installDir -WindowStyle Hidden

    $deadline = (Get-Date).AddSeconds(10)
    do {
        Start-Sleep -Milliseconds 250
        $started = Get-Process -Name AgentCake -ErrorAction SilentlyContinue
        if ($started) { break }
    } while ((Get-Date) -lt $deadline)

    if (-not $started) { throw 'AgentCake was installed but did not start. Run the installed AgentCake.exe once to see the Windows error.' }
}

Write-Host "Installed AgentCake: $installedExe"
Write-Host 'It will run automatically when you sign in.'
