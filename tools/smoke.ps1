param([switch]$Build, [switch]$IconsOnly)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
Push-Location $projectRoot
$previousAppData = $env:APPDATA
try {
    if ($Build) { & "$PSScriptRoot/build.ps1" }
    & "$PSScriptRoot/launch.ps1" -PrepareOnly
    $env:APPDATA = Join-Path $projectRoot 'tools/runtime/userdata'
    $settingsPath = Join-Path $env:APPDATA 'PunchConstructTest/default/1/settings.save'
    New-Item -ItemType Directory -Force -Path (Split-Path $settingsPath) | Out-Null
    if (Test-Path -LiteralPath $settingsPath) {
        $settings = Get-Content -LiteralPath $settingsPath -Raw -Encoding UTF8 | ConvertFrom-Json
    } else {
        $settings = [PSCustomObject]@{schema_version=8; language='zhs'}
    }
    $settings | Add-Member -Force -NotePropertyName mod_settings -NotePropertyValue @{mods_enabled=$true;mod_list=@()}
    $settings | Add-Member -Force -NotePropertyName seen_ea_disclaimer -NotePropertyValue $true
    $settings | Add-Member -Force -NotePropertyName skip_intro_logo -NotePropertyValue $true
    foreach ($qaVolume in @('volume_master', 'volume_bgm', 'volume_sfx', 'volume_ambience')) {
        $settings | Add-Member -Force -NotePropertyName $qaVolume -NotePropertyValue 0
    }
    [IO.File]::WriteAllText($settingsPath, ($settings | ConvertTo-Json -Depth 20))
    $logPath = Join-Path $projectRoot $(if ($IconsOnly) { 'tools/runtime/icon-check.log' } else { 'tools/runtime/smoke.log' })
    $modeFlag = if ($IconsOnly) { '--punchconstruct-iconcheck' } else { '--punchconstruct-smoke' }
    if (Test-Path -LiteralPath $logPath) { Remove-Item -LiteralPath $logPath }
    Start-Process -FilePath (Join-Path $projectRoot 'tools/runtime/SlayTheSpire2.exe') -WindowStyle Hidden -Wait `
        -ArgumentList @('--headless', '--force-steam=off', $modeFlag, '--quit-after', '1800', '--log-file', ('"' + $logPath + '"')) `
        -RedirectStandardOutput (Join-Path $projectRoot 'tools/runtime/smoke-stdout.log') -RedirectStandardError (Join-Path $projectRoot 'tools/runtime/smoke-stderr.log')
    $report = Get-Content -LiteralPath $logPath -Raw -Encoding UTF8
    $passMarker = if ($IconsOnly) { '\[PunchConstruct ICON PASS\]' } else { '\[PunchConstruct SMOKE PASS\]' }
    if ($report -notmatch $passMarker -or $report -match '\[PunchConstruct SMOKE FAIL\]') {
        throw "Smoke checks failed; inspect $logPath"
    }
    Write-Output $(if ($IconsOnly) { 'PASS: nine adopted power icons and large textures; no run or combat started.' } else { 'PASS: character registration, deck, upgrades, pool ownership, localization and scenes.' })
} finally {
    $env:APPDATA = $previousAppData
    Pop-Location
}
