# Explicit owner activation. Uses the approved full Spot policy and verified broker profile.
# No placement/cancellation is performed here. AI starts only after successful activation.
param([switch]$NoBrowser)
$ErrorActionPreference = 'Stop'
$activationRoot = Split-Path -Parent $PSScriptRoot
$composePath = Join-Path $activationRoot 'docker-compose.owner-readonly.yml'
$settingsPath = Join-Path $activationRoot '.runtime/owner-tls/appsettings.Production.json'
$advisoryPath = Join-Path $activationRoot '.runtime/live-company/advisory.json'
$logRoot = Join-Path $activationRoot 'work/live-account-connect'
New-Item -ItemType Directory -Force -Path $logRoot | Out-Null
$configurationChanged = $false
$activationAttempted = $false
$previousCompanyAuth = $env:QUANTARA_CODEX_AUTH_FILE
function Invoke-ActivationDocker {
    param(
        [Parameter(Mandatory)][string[]]$Arguments,
        [string]$OutputPath
    )
    $previousErrorActionPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        $global:LASTEXITCODE = $null
        $output = @(& docker @Arguments 2>&1)
        $nativeExitCode = $global:LASTEXITCODE
    } finally {
        $ErrorActionPreference = $previousErrorActionPreference
    }
    if ($null -eq $nativeExitCode) { throw 'Docker command could not be started.' }
    # Windows PowerShell wraps native stderr in ErrorRecord objects. Render the
    # original text; progress must not look like a PowerShell exception.
    $lines = @($output | ForEach-Object { $_.ToString() })
    if ($OutputPath) { $lines | Out-File -FilePath $OutputPath -Encoding utf8 }
    elseif ($lines) { $lines | Out-Host }
    return [int]$nativeExitCode
}
Push-Location $activationRoot
try {
    $settingsOriginal = [IO.File]::ReadAllText($settingsPath)
    $advisoryOriginal = [IO.File]::ReadAllText($advisoryPath)
    $settings = $settingsOriginal | ConvertFrom-Json
    $advisory = $advisoryOriginal | ConvertFrom-Json
    $activationAccount = [Guid]::Empty
    $activationToken = $settings.LiveExecution.CoordinatorToken
    if (![Guid]::TryParse($settings.LiveExecution.AccountId, [ref]$activationAccount) -or $activationAccount -eq [Guid]::Empty -or
        $settings.LiveExecution.AccountId -ne $advisory.LiveCompany.AccountId -or
        $activationToken -ne $advisory.LiveCompany.CoordinatorToken -or
        !$activationToken -or $activationToken.Length -lt 32 -or $activationToken.Length -gt 256 -or
        $activationToken -match '[\x00-\x1f\x7f]' -or
        $advisory.LiveCompany.DataPath -ne '/company-data' -or
        [string]::IsNullOrWhiteSpace($advisory.LiveCompany.Model) -or
        $advisory.LiveCompany.ExecutorCertificateFile -ne '/trust/localhost.cer' -or
        !(Test-Path -LiteralPath (Join-Path $activationRoot '.runtime/owner-tls/localhost.cer') -PathType Leaf)) {
        throw 'Account/advisory binding mismatch. Configuration was preserved.'
    }
    $activationAuthRoot = if ($env:CODEX_HOME) { $env:CODEX_HOME } else { Join-Path ([Environment]::GetFolderPath('UserProfile')) '.codex' }
    $activationAuthFile = Join-Path $activationAuthRoot 'auth.json'
    if (!(Test-Path -LiteralPath $activationAuthFile -PathType Leaf)) { throw 'Codex sign-in is required for the CEO. Configuration was preserved.' }
    $env:QUANTARA_CODEX_AUTH_FILE = $activationAuthFile
    # Validate/build before changing the currently working application's configuration.
    $dockerExitCode = Invoke-ActivationDocker -Arguments @('compose', '--env-file', 'NUL', '-p', 'quantara-owner-readonly', '-f', $composePath, '--profile', 'live-company', 'build', 'owner', 'company', 'owner-web') -OutputPath (Join-Path $logRoot 'activation-build.log')
    if ($dockerExitCode -ne 0) { throw 'Live build failed. Current configuration was preserved.' }
    $dockerExitCode = Invoke-ActivationDocker @('compose', '--env-file', 'NUL', '-p', 'quantara-owner-readonly', '-f', $composePath, 'run', '--rm', '--no-deps', '-T', 'owner', '--verify-live-configuration')
    if ($dockerExitCode -ne 0) { throw 'Broker profile/key/Trade verification did not pass. Live was not opened.' }
    $dockerExitCode = Invoke-ActivationDocker @('compose', '--env-file', 'NUL', '-p', 'quantara-owner-readonly', '-f', $composePath, '--profile', 'live-company', 'stop', 'company')
    if ($dockerExitCode -ne 0) { throw 'Could not stop existing CEO activity. Configuration was preserved.' }
    $profileDirectory = Join-Path $activationRoot '.runtime/live'
    $dockerExitCode = Invoke-ActivationDocker @('compose', '--env-file', 'NUL', '-p', 'quantara-owner-readonly', '-f', $composePath,
        'run', '--rm', '--no-deps', '-T', '--volume', "${profileDirectory}:/profile-output", 'owner', '--prepare-multi-asset-profile')
    if ($dockerExitCode -ne 0) { throw 'BTC/ETH/SOL market verification failed. Existing account and mandate were preserved.' }
    $configurationChanged = $true
    $settings.LiveExecution.Enabled = $true
    [IO.File]::WriteAllText($settingsPath, ($settings | ConvertTo-Json -Depth 12))
    # Leave the advisory disabled until the owner command commits verified activation.
    $advisory.LiveCompany.Enabled = $false
    [IO.File]::WriteAllText($advisoryPath, ($advisory | ConvertTo-Json -Depth 12))
    $dockerExitCode = Invoke-ActivationDocker @('compose', '--env-file', 'NUL', '-p', 'quantara-owner-readonly', '-f', $composePath, '--profile', 'live-execution', 'up', '-d', '--wait', 'ledger')
    if ($dockerExitCode -ne 0) { throw 'Private ledger is not healthy. AI was not started.' }
    $dockerExitCode = Invoke-ActivationDocker @('compose', '--env-file', 'NUL', '-p', 'quantara-owner-readonly', '-f', $composePath, 'up', '-d', 'owner-web')
    if ($dockerExitCode -ne 0) { throw 'Frontend startup failed. AI was not started.' }
    $dockerExitCode = Invoke-ActivationDocker @('compose', '--env-file', 'NUL', '-p', 'quantara-owner-readonly', '-f', $composePath, 'up', '-d', '--force-recreate', '--no-deps', 'owner')
    if ($dockerExitCode -ne 0) { throw 'Executor startup failed. AI was not started.' }
    $executorReady = $false
    for ($attempt = 0; $attempt -lt 15; $attempt++) {
        try { $executorReady = (Invoke-WebRequest 'https://localhost:3000/' -UseBasicParsing -TimeoutSec 3).StatusCode -eq 200; if ($executorReady) { break } }
        catch { Start-Sleep -Milliseconds 500 }
    }
    if (!$executorReady) { throw 'Executor did not become ready. AI was not started.' }
    $activationAttempted = $true
    $dockerExitCode = Invoke-ActivationDocker @('compose', '--env-file', 'NUL', '-p', 'quantara-owner-readonly', '-f', $composePath, 'run', '--rm', '--no-deps', '-T', 'owner', '--activate-full-spot-live')
    if ($dockerExitCode -ne 0) { throw 'Owner activation did not pass. AI remains disabled; review the reported reason.' }
    $advisory.LiveCompany.Enabled = $true
    [IO.File]::WriteAllText($advisoryPath, ($advisory | ConvertTo-Json -Depth 12))
    $dockerExitCode = Invoke-ActivationDocker @('compose', '--env-file', 'NUL', '-p', 'quantara-owner-readonly', '-f', $composePath, '--profile', 'live-company', 'up', '-d', 'company')
    if ($dockerExitCode -ne 0) { throw 'CEO startup failed. Inspect owner activation in the app before retrying.' }
    if (!$NoBrowser) { Start-Process 'https://localhost:3000/trading' }
    Write-Output 'Live owner activation completed. The separate CEO process may submit Spot proposals within the saved mandate.'
} catch {
    if ($configurationChanged) {
        $advisory.LiveCompany.Enabled = $false
        [IO.File]::WriteAllText($advisoryPath, ($advisory | ConvertTo-Json -Depth 12))
        $dockerExitCode = Invoke-ActivationDocker -Arguments @('compose', '--env-file', 'NUL', '-p', 'quantara-owner-readonly', '-f', $composePath, '--profile', 'live-company', 'stop', 'company') -OutputPath $null
        if ($dockerExitCode -ne 0) { Write-Warning 'Could not verify CEO shutdown. Stop the company container in Docker and inspect /trading.' }
        if (!$activationAttempted) {
            [IO.File]::WriteAllText($settingsPath, $settingsOriginal)
            $dockerExitCode = Invoke-ActivationDocker -Arguments @('compose', '--env-file', 'NUL', '-p', 'quantara-owner-readonly', '-f', $composePath, 'up', '-d', '--force-recreate', '--no-deps', 'owner') -OutputPath $null
        } else {
            Write-Warning 'CEO configuration is disabled. Activation may have committed; inspect /trading. No balances, orders or owner Stop were reset.'
        }
    }
    throw
} finally {
    Pop-Location
    if ($previousCompanyAuth) { $env:QUANTARA_CODEX_AUTH_FILE = $previousCompanyAuth }
    else { Remove-Item Env:\QUANTARA_CODEX_AUTH_FILE -ErrorAction SilentlyContinue }
    $settings = $null; $advisory = $null; $settingsOriginal = $null; $advisoryOriginal = $null; $activationToken = $null
}
