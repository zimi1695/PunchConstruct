param(
    [string]$PackageDir = 'dist/SteamWorkshop/content',
    [switch]$SkipLocal,
    [switch]$SkipCoop,
    [switch]$SkipRecovery
)
$ErrorActionPreference = 'Continue'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root
$results = @()

function Run-Step([string]$name, [string[]]$arguments) {
    Write-Host ''
    Write-Host "=== $name ==="
    $watch = [System.Diagnostics.Stopwatch]::StartNew()
    & python @arguments
    $code = $LASTEXITCODE
    $watch.Stop()
    $script:results += [pscustomobject]@{
        Step = $name
        ExitCode = $code
        Seconds = [math]::Round($watch.Elapsed.TotalSeconds, 1)
    }
    Write-Host "$name => $(if ($code -eq 0) { 'PASS' } else { 'FAIL' }) ($([math]::Round($watch.Elapsed.TotalSeconds, 1))s)"
}

if (-not $SkipLocal) {
    Run-Step 'local-35' @('tools/check_local.py', '--package-dir', $PackageDir)
}
if (-not $SkipCoop) {
    Run-Step 'coop-construct-2p' @('tools/check_coop.py', '--package-dir', $PackageDir, '--rendered', '--players', '2', '--guest', 'construct')
    Run-Step 'coop-necrobinder-3p' @('tools/check_coop.py', '--package-dir', $PackageDir, '--rendered', '--players', '3', '--guest', 'necrobinder')
    Run-Step 'coop-construct-4p' @('tools/check_coop.py', '--package-dir', $PackageDir, '--rendered', '--players', '4', '--guest', 'construct')
    Run-Step 'coop-construct-4p-network' @('tools/check_coop.py', '--package-dir', $PackageDir, '--rendered', '--players', '4', '--guest', 'construct', '--latency-ms', '100', '--jitter-ms', '40', '--loss-percent', '5', '--blackout-seconds', '5')
}
if (-not $SkipRecovery) {
    Run-Step 'recovery-menu' @('tools/check_recovery.py', '--package-dir', $PackageDir, '--players', '2')
    Run-Step 'recovery-abrupt' @('tools/check_recovery.py', '--package-dir', $PackageDir, '--players', '2', '--abrupt')
    Run-Step 'recovery-process-loss-4p' @('tools/check_recovery.py', '--package-dir', $PackageDir, '--players', '4', '--kill-client', '--resume-order', 'reverse')
    Run-Step 'recovery-host-loss-mixed-3p' @('tools/check_recovery.py', '--package-dir', $PackageDir, '--players', '3', '--party', 'mixed', '--kill-host', '--resume-order', 'reverse')
}
Run-Step 'report' @('tools/report_local.py')

Write-Host ''
Write-Host '=== Summary ==='
$results | Format-Table -AutoSize
if ($results | Where-Object { $_.ExitCode -ne 0 }) { exit 1 }
exit 0
