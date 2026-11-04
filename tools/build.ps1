param([ValidateSet('Debug','Release')][string]$Configuration = 'Release')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$env:DOTNET_CLI_HOME = Join-Path $projectRoot '.dotnet-local'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
Push-Location $projectRoot
try {
    python tools/prepare_art.py
    if ($LASTEXITCODE -ne 0) { throw 'Artwork export failed.' }
    python tools/prepare_text.py
    if ($LASTEXITCODE -ne 0) { throw 'Character text export failed.' }
    dotnet build PunchConstruct/PunchConstruct.csproj --nologo --configuration $Configuration
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
    python tools/package.py --configuration $Configuration
    if ($LASTEXITCODE -ne 0) { throw 'Packaging failed.' }
} finally {
    Pop-Location
}
