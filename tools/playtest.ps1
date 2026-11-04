param([switch]$Visual, [switch]$Build, [ValidateSet('full','end','design','flow','launch','focus','doll')][string]$Mode = 'focus')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
Push-Location $projectRoot
$previousAppData = $env:APPDATA
try {
    if ($Build) { & "$PSScriptRoot/build.ps1" }
    $runtime = Join-Path $projectRoot 'tools/runtime'
    if (-not (Test-Path -LiteralPath (Join-Path $runtime 'SlayTheSpire2.pck'))) {
        python tools/prepare_runtime.py
        if ($LASTEXITCODE -ne 0) { throw 'Runtime preparation failed.' }
    }
    foreach ($name in @('PunchConstruct.dll', 'PunchConstruct.pck', 'PunchConstruct.json')) {
        Copy-Item -LiteralPath (Join-Path $projectRoot "dist/PunchConstruct/$name") -Destination (Join-Path $runtime "mods/PunchConstruct/$name")
    }
    $localBuild = Join-Path $projectRoot 'PunchConstruct/bin/Release/net9.0/PunchConstruct.dll'
    if (Test-Path -LiteralPath $localBuild) { Copy-Item -LiteralPath $localBuild -Destination (Join-Path $runtime 'mods/PunchConstruct/PunchConstruct.dll') }
    python tools/sync_runtime_art.py
    if ($LASTEXITCODE -ne 0) { throw 'Local artwork synchronization failed.' }
    $env:APPDATA = Join-Path $runtime $(if ($Mode -eq 'launch') { 'clean-launch-userdata-' + [DateTime]::UtcNow.ToString('yyyyMMddHHmmss') } else { 'userdata' })
    $settingsPath = Join-Path $env:APPDATA 'PunchConstructTest/default/1/settings.save'
    New-Item -ItemType Directory -Force -Path (Split-Path $settingsPath) | Out-Null
    if (Test-Path -LiteralPath $settingsPath) { $settings = Get-Content -LiteralPath $settingsPath -Raw -Encoding UTF8 | ConvertFrom-Json }
    else { $settings = [PSCustomObject]@{schema_version=8;language='zhs'} }
    $settings | Add-Member -Force -NotePropertyName mod_settings -NotePropertyValue @{mods_enabled=$true;mod_list=@()}
    $settings | Add-Member -Force -NotePropertyName seen_ea_disclaimer -NotePropertyValue $true
    $settings | Add-Member -Force -NotePropertyName skip_intro_logo -NotePropertyValue $true
    foreach ($qaVolume in @('volume_master', 'volume_bgm', 'volume_sfx', 'volume_ambience')) {
        $settings | Add-Member -Force -NotePropertyName $qaVolume -NotePropertyValue 0
    }
    [IO.File]::WriteAllText($settingsPath, ($settings | ConvertTo-Json -Depth 20))
    $logPath = Join-Path $runtime $(if ($Mode -ne 'full') { "$Mode-check.log" } elseif ($Visual) { 'visual-playtest.log' } else { 'playtest.log' })
    $modeFlag = switch ($Mode) { 'doll' { '--punchconstruct-dollcheck' } 'focus' { '--punchconstruct-focuscheck' } 'end' { '--punchconstruct-endcheck' } 'design' { '--punchconstruct-designcheck' } 'flow' { '--punchconstruct-flowcheck' } 'launch' { '--punchconstruct-launchcheck' } default { '--punchconstruct-playtest' } }
    $gameArgs = @('--force-steam=off', $modeFlag, '--quit-after', '30000', '--log-file', ('"' + $logPath + '"'))
    if ($Visual) { $gameArgs += @('--punchconstruct-visual', '--rendering-method', 'gl_compatibility', '--resolution', '1280x720', '--position', '20,20') }
    else { $gameArgs += '--headless' }
    # Windows PowerShell does not wait for GUI executables invoked with &.
    # Wait explicitly before reading the report; never accept a stale PASS.
    if (Test-Path -LiteralPath $logPath) { Remove-Item -LiteralPath $logPath }
    $gameProcess = Start-Process -FilePath (Join-Path $runtime 'SlayTheSpire2.exe') -ArgumentList $gameArgs -WindowStyle Hidden -PassThru `
        -RedirectStandardOutput (Join-Path $runtime 'playtest-stdout.log') -RedirectStandardError (Join-Path $runtime 'playtest-stderr.log')
    [IO.File]::WriteAllText((Join-Path $runtime 'active-qa.pid'), [string]$gameProcess.Id)
    if (-not $gameProcess.WaitForExit(90000)) {
        # This process was created above from the workspace runtime; never stop the user's installed game.
        Stop-Process -Id $gameProcess.Id
        throw "Targeted check timed out after 90 seconds: $logPath"
    }
    $report = Get-Content -LiteralPath $logPath -Raw -Encoding UTF8
    if ($report -notmatch '\[Construct (QA|LAUNCH|FOCUS|DOLL) PASS\]' -or $report -match '\[Construct QA FAIL\]') { throw "Playtest failed: $logPath" }
    Write-Output "PASS: $Mode checks only; see $logPath."
} finally {
    $env:APPDATA = $previousAppData
    Pop-Location
}
