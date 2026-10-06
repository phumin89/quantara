# Starts the protected app. Live/AI stay opt-in and require verified private configuration.
param([switch]$Rebuild, [switch]$NoBrowser)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$runtimeRoot = Join-Path $projectRoot '.runtime'
$tlsRuntime = Join-Path $runtimeRoot 'owner-tls'
$composeFile = Join-Path $projectRoot 'docker-compose.owner-readonly.yml'
$liveEnabled = $false
if (Test-Path -LiteralPath (Join-Path $tlsRuntime 'appsettings.Production.json')) {
    $launchSettings = Get-Content -LiteralPath (Join-Path $tlsRuntime 'appsettings.Production.json') -Raw | ConvertFrom-Json
    $liveEnabled = $launchSettings.LiveExecution.Enabled -eq $true
    $launchSettings = $null
}

& docker info --format '{{.ServerVersion}}'
if ($LASTEXITCODE -ne 0) { throw 'Docker is unavailable. Open Docker Desktop, then open Quantara again.' }
$ownedLabelsJson = & docker inspect --format '{{json .Config.Labels}}' quantara-owner-readonly-owner-1 2>$null
$ownedHostLabels = if ($LASTEXITCODE -eq 0) { $ownedLabelsJson | ConvertFrom-Json } else { $null }
$hostRunning = & docker inspect --format '{{.State.Running}}' quantara-owner-readonly-owner-1 2>$null
$ownedHostRunning = $LASTEXITCODE -eq 0 -and $hostRunning.Trim() -eq 'true' -and
    $ownedHostLabels.'com.docker.compose.project' -eq 'quantara-owner-readonly' -and $ownedHostLabels.'com.docker.compose.service' -eq 'owner'
if ($ownedHostRunning -and -not $Rebuild -and -not $liveEnabled) {
    try { $alreadyReady = (Invoke-WebRequest 'https://localhost:3000/' -UseBasicParsing -TimeoutSec 3).StatusCode -eq 200 }
    catch { $alreadyReady = $false }
    if ($alreadyReady) {
        if (-not $NoBrowser) { Start-Process 'https://localhost:3000/' }
        Write-Output 'Quantara is already running. Existing browser access is retained.'
        return
    }
}
foreach ($localPort in @(2080,3000,7443)) {
    if ((Get-NetTCPConnection -State Listen -LocalPort $localPort -ErrorAction SilentlyContinue) -and -not $ownedHostRunning) {
        throw "Port $localPort is in use. No process was stopped. Stop the existing app before starting this viewer."
    }
}
if (-not (Test-Path -LiteralPath (Join-Path $projectRoot '.env'))) { throw 'Project .env is missing.' }
if (-not (Test-Path -LiteralPath (Join-Path $tlsRuntime 'localhost.pfx')) -or -not (Test-Path -LiteralPath (Join-Path $tlsRuntime 'appsettings.Production.json'))) {
& dotnet dev-certs https --check --trust
if ($LASTEXITCODE -ne 0) { throw 'A trusted localhost HTTPS certificate is required. No certificate or security policy was changed.' }
New-Item -ItemType Directory -Force -Path $tlsRuntime | Out-Null
$certificate = Get-ChildItem Cert:\CurrentUser\My | Where-Object {
    $_.HasPrivateKey -and $_.Subject -eq 'CN=localhost' -and $_.NotAfter -gt (Get-Date) -and
    ($_.Extensions.Oid.Value -contains '1.3.6.1.4.1.311.84.1.1') -and $_.Verify()
} | Sort-Object NotAfter -Descending | Select-Object -First 1
if (-not $certificate) { throw 'Trusted localhost development certificate not found.' }
$certificatePasswordBytes = New-Object byte[] 32
$randomGenerator = [Security.Cryptography.RandomNumberGenerator]::Create()
try { $randomGenerator.GetBytes($certificatePasswordBytes) } finally { $randomGenerator.Dispose() }
$certificatePassword = [Convert]::ToBase64String($certificatePasswordBytes)
[IO.File]::WriteAllBytes((Join-Path $tlsRuntime 'localhost.pfx'), $certificate.Export([Security.Cryptography.X509Certificates.X509ContentType]::Pfx, $certificatePassword))
@{ Kestrel = @{ Certificates = @{ Default = @{ Path = '/tls/localhost.pfx'; Password = $certificatePassword } } } } |
    ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $tlsRuntime 'appsettings.Production.json')
$certificatePassword = $null
}
& docker image inspect quantara-web:latest quantara-owner-readonly:latest --format '{{.Id}}' *> $null
if ($Rebuild -or $LASTEXITCODE -ne 0) {
    & docker compose --env-file NUL -p quantara-owner-readonly -f $composeFile build
    if ($LASTEXITCODE -ne 0) { throw 'Read-only owner image build failed.' }
}
& docker compose --env-file NUL -p quantara-owner-readonly -f $composeFile up -d owner-web
if ($LASTEXITCODE -ne 0) { throw 'Public frontend startup failed.' }
if ($liveEnabled) {
    if (-not (Test-Path -LiteralPath (Join-Path $runtimeRoot 'live/bitkub-verification.json')) -or
        -not (Test-Path -LiteralPath (Join-Path $runtimeRoot 'live-db/database.env'))) {
        throw 'Verified broker profile and private Live database configuration are required.'
    }
    & docker compose --env-file NUL -p quantara-owner-readonly -f $composeFile --profile live-execution up -d --wait ledger
    if ($LASTEXITCODE -ne 0) { throw 'Private Live ledger is not healthy.' }
}
& docker compose --env-file NUL -p quantara-owner-readonly -f $composeFile up -d owner
if ($LASTEXITCODE -ne 0) { throw 'Read-only owner service startup failed.' }
if ($liveEnabled -and (Test-Path -LiteralPath (Join-Path $runtimeRoot 'live-company/advisory.json'))) {
    $companySettings = Get-Content -LiteralPath (Join-Path $runtimeRoot 'live-company/advisory.json') -Raw | ConvertFrom-Json
    $companyEnabled = $companySettings.LiveCompany.Enabled -eq $true
    $companySettings = $null
    if ($companyEnabled) {
        $codexAuthRoot = if ($env:CODEX_HOME) { $env:CODEX_HOME } else { Join-Path ([Environment]::GetFolderPath('UserProfile')) '.codex' }
        $companyAuthFile = Join-Path $codexAuthRoot 'auth.json'
        if (-not (Test-Path -LiteralPath $companyAuthFile -PathType Leaf)) { throw 'Codex sign-in is missing. No AI meeting was started.' }
        $previousCompanyAuthFile = $env:QUANTARA_CODEX_AUTH_FILE
        $env:QUANTARA_CODEX_AUTH_FILE = $companyAuthFile
        try {
            & docker compose --env-file NUL -p quantara-owner-readonly -f $composeFile --profile live-company up -d company
            if ($LASTEXITCODE -ne 0) { throw 'Isolated Live company startup failed.' }
        } finally {
            if ($previousCompanyAuthFile) { $env:QUANTARA_CODEX_AUTH_FILE = $previousCompanyAuthFile }
            else { Remove-Item Env:\QUANTARA_CODEX_AUTH_FILE }
        }
    }
}
$frontendDeadline = (Get-Date).AddSeconds(45)
$frontendReady = $false
do {
    try {
        $frontendReady = (Invoke-WebRequest 'https://localhost:3000/' -UseBasicParsing -TimeoutSec 3).StatusCode -eq 200
    } catch { $frontendReady = $false }
    if (-not $frontendReady) { Start-Sleep -Seconds 1 }
} while (-not $frontendReady -and (Get-Date) -lt $frontendDeadline)
if (-not $frontendReady) { throw 'The HTTPS owner frontend did not start. Inspect its startup error.' }
& docker exec quantara-owner-readonly-owner-1 test -f /owner-state/session.bin
if ($LASTEXITCODE -ne 0) {
    & (Join-Path $PSScriptRoot 'pair-live-owner.ps1')
    if ($LASTEXITCODE -ne 0) { throw 'Owner pairing console failed to start.' }
} else {
    Write-Output 'Remembered viewing retained. If this browser requires pairing, run tools/pair-live-owner.ps1.'
}
Write-Output 'Open https://localhost:3000 and pair this browser if needed. Remembering the browser permits viewing only; it does not enable trading.'
if (-not $NoBrowser) { Start-Process 'https://localhost:3000/' }
