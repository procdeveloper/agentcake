[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$source = Join-Path $repoRoot 'JS\agentcake-statusline.js'
$claudeDir = Join-Path $env:USERPROFILE '.claude'
$hook = Join-Path $claudeDir 'agentcake-statusline.js'
$settingsPath = Join-Path $claudeDir 'settings.json'

if (-not (Test-Path -LiteralPath $source -PathType Leaf)) { throw "Missing hook source: $source" }
New-Item -ItemType Directory -Force -Path $claudeDir | Out-Null
Copy-Item -LiteralPath $source -Destination $hook -Force

$settings = $null
if (Test-Path -LiteralPath $settingsPath) {
    try { $settings = Get-Content -Raw -LiteralPath $settingsPath | ConvertFrom-Json } catch {
        $raw = Get-Content -Raw -LiteralPath $settingsPath
        $backup = "$settingsPath.agentcake-backup"
        Copy-Item -LiteralPath $settingsPath -Destination $backup -Force
        $settings = [pscustomobject]@{}
        if ($raw -match '"theme"\s*:\s*"([^"]+)"') {
            $settings | Add-Member -NotePropertyName theme -NotePropertyValue $Matches[1]
        }
        Write-Warning "Existing Claude settings were invalid JSON; backed up to $backup."
    }
}
if ($null -eq $settings) { $settings = [pscustomobject]@{} }
$statusLine = [pscustomobject]@{
    type = 'command'
    command = "node `"$hook`""
    padding = 0
}
if ($settings.PSObject.Properties.Name -contains 'statusLine') { $settings.statusLine = $statusLine }
else { $settings | Add-Member -NotePropertyName statusLine -NotePropertyValue $statusLine }
$settings | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $settingsPath -Encoding utf8
Write-Host "Installed AgentCake Claude status hook: $hook"
Write-Host "Claude settings updated: $settingsPath"