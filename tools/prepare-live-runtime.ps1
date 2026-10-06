# Prepares private runtime paths. It does not verify the broker, enable execution, arm or send an order.
$ErrorActionPreference = 'Stop'
$liveRoot = Split-Path -Parent $PSScriptRoot
$tlsSettingsPath = Join-Path $liveRoot '.runtime/owner-tls/appsettings.Production.json'
if (-not (Test-Path -LiteralPath $tlsSettingsPath)) { throw 'Start the HTTPS app once before preparing Live.' }
$settings = Get-Content -LiteralPath $tlsSettingsPath -Raw | ConvertFrom-Json -AsHashtable
if ($settings.LiveExecution.Enabled) { throw 'Existing Live execution configuration is active. Preparation will not overwrite it.' }
foreach ($folder in @('live','live-db','live-company')) {
    New-Item -ItemType Directory -Force -Path (Join-Path $liveRoot ".runtime/$folder") | Out-Null
}
$databasePath = Join-Path $liveRoot '.runtime/live-db/database.env'
if (-not (Test-Path -LiteralPath $databasePath)) {
    $databasePassword = [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
    [IO.File]::WriteAllText($databasePath, "POSTGRES_DB=quantara_live`nPOSTGRES_USER=quantara`nPOSTGRES_PASSWORD=$databasePassword`n")
} else {
    $databaseLines = Get-Content -LiteralPath $databasePath
    $passwordLines = @($databaseLines | Where-Object { $_.StartsWith('POSTGRES_PASSWORD=') })
    if ($passwordLines.Count -ne 1) { throw 'Private database configuration is invalid.' }
    $databasePassword = $passwordLines[0].Substring('POSTGRES_PASSWORD='.Length)
}
if ($databasePassword.Length -lt 32 -or $databasePassword.Length -gt 256 -or $databasePassword -notmatch '^[A-Za-z0-9+/=]+$') {
    throw 'Private database password format is invalid. Existing credentials were preserved.'
}
if (-not $settings.LiveExecution) {
    $settings.LiveExecution = @{ Enabled = $false; AccountId = [guid]::NewGuid().ToString();
        CoordinatorToken = [Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(32));
        VerificationFile = '/live-config/bitkub-verification.json' }
}
if (-not $settings.ConnectionStrings) { $settings.ConnectionStrings = @{} }
$settings.ConnectionStrings.LiveTrading = "Host=ledger;Database=quantara_live;Username=quantara;Password=$databasePassword;Pooling=false"
$settings | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $tlsSettingsPath
$profilePath = Join-Path $liveRoot '.runtime/live/bitkub-verification.json'
if (-not (Test-Path -LiteralPath $profilePath)) {
    @{ BrokerAccountReference = ''; CredentialFingerprint = ''; ConfirmationReference = ''; EvidenceHash = '';
        AccountBindingVerified = $false; TradePermissionVerified = $false; Rules = $null } |
        ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $profilePath
}
$advisoryPath = Join-Path $liveRoot '.runtime/live-company/advisory.json'
if (-not (Test-Path -LiteralPath $advisoryPath)) {
    @{ LiveCompany = @{ Enabled = $false; AccountId = $settings.LiveExecution.AccountId;
        CoordinatorToken = $settings.LiveExecution.CoordinatorToken; DataPath = '/company-data';
        Model = 'codex-default'; ExecutorCertificateFile = '/trust/localhost.cer' } } |
        ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $advisoryPath
}
$certificate = [Security.Cryptography.X509Certificates.X509Certificate2]::new(
    (Join-Path $liveRoot '.runtime/owner-tls/localhost.pfx'), $settings.Kestrel.Certificates.Default.Password)
try { [IO.File]::WriteAllBytes((Join-Path $liveRoot '.runtime/owner-tls/localhost.cer'),
    $certificate.Export([Security.Cryptography.X509Certificates.X509ContentType]::Cert)) }
finally { $certificate.Dispose(); $databasePassword = $null; $settings = $null; $databaseLines = $null; $passwordLines = $null }
Write-Output 'Private Live storage and advisory configuration prepared. Broker verification, execution and AI remain disabled.'
