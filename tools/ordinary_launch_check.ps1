$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
Push-Location $projectRoot
try {
    # Shared isolated fixture: latest build, BaseLib only, no persistent test profile.
    python tools/check_current.py startup --rendered
    if ($LASTEXITCODE -ne 0) { throw 'Ordinary startup check failed; inspect tools/runtime/startup-current-check.log.' }
} finally { Pop-Location }
