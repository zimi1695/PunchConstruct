param([switch]$PrepareOnly)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
Push-Location $projectRoot
try {
    $runtime = Join-Path $projectRoot 'tools/runtime'
    $playExecutable = Join-Path $runtime 'SlayTheSpire2.exe'
    if (Get-Process -Name 'SlayTheSpire2' -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $playExecutable }) {
        throw 'Close the local play game before synchronizing its mod files.'
    }
    if (-not (Test-Path -LiteralPath (Join-Path $runtime 'SlayTheSpire2.pck'))) {
        python tools/prepare_runtime.py
        if ($LASTEXITCODE -ne 0) { throw 'Runtime preparation failed.' }
    }
    $playSources = @{
        'PunchConstruct.dll' = Join-Path $projectRoot 'PunchConstruct/bin/Release/net9.0/PunchConstruct.dll'
        'PunchConstruct.json' = Join-Path $projectRoot 'PunchConstruct/PunchConstruct.json'
    }
    foreach ($name in $playSources.Keys) {
        Copy-Item -LiteralPath $playSources[$name] -Destination (Join-Path $runtime "mods/PunchConstruct/$name")
    }
    # Remove only the three legacy dependency files from this disposable runtime.
    # The Steam workshop installation and all user saves remain untouched.
    $legacyWatcher = [IO.Path]::GetFullPath((Join-Path $runtime 'mods/Watcher'))
    $expectedWatcher = [IO.Path]::GetFullPath((Join-Path $projectRoot 'tools/runtime/mods/Watcher'))
    if ($legacyWatcher -ne $expectedWatcher) { throw 'Unexpected legacy dependency path.' }
    foreach ($legacyName in @('Watcher.dll', 'Watcher.pck', 'Watcher.json')) {
        $legacyFile = Join-Path $legacyWatcher $legacyName
        if (Test-Path -LiteralPath $legacyFile) { Remove-Item -LiteralPath $legacyFile }
    }
    if ((Test-Path -LiteralPath $legacyWatcher) -and -not (Get-ChildItem -LiteralPath $legacyWatcher -Force)) {
        Remove-Item -LiteralPath $legacyWatcher
    }
    python tools/sync_runtime_art.py
    if ($LASTEXITCODE -ne 0) { throw 'Local artwork synchronization failed.' }
    # Separate from both the user's original saves and all automated QA fixtures.
    $playData = Join-Path $runtime 'play-userdata'
    $settingsPath = Join-Path $playData 'PunchConstructTest/default/1/settings.save'
    if (-not (Test-Path -LiteralPath $settingsPath)) {
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $settingsPath) | Out-Null
        $settings = @{
            schema_version=8; language='zhs'; fullscreen=$false; fps_limit=60
            mod_settings=@{mods_enabled=$true;mod_list=@()}
            seen_ea_disclaimer=$true; skip_intro_logo=$true
            window_size=@{X=1280;Y=720}
        }
        [IO.File]::WriteAllText($settingsPath, ($settings | ConvertTo-Json -Depth 10))
    }
    if ($PrepareOnly) { Write-Output 'Prepared clean play profile.'; return }
    $previousAppData = $env:APPDATA
    try {
        $env:APPDATA = $playData
        Start-Process -FilePath (Join-Path $runtime 'SlayTheSpire2.exe') -WorkingDirectory $runtime -ArgumentList @('--force-steam=off', '--rendering-method', 'gl_compatibility', '--log-file', 'play.log')
    } finally { $env:APPDATA = $previousAppData }
} finally { Pop-Location }
