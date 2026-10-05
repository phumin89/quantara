param(
    [ValidateSet('Start', 'Stop', 'Status', 'Restart')][string]$Action = 'Start',
    [switch]$SkipBuild,
    [switch]$OwnerWeb,
    [ValidateRange(0, 1000000000)][decimal]$InitialPaperCapital = 100000
)
$ErrorActionPreference = 'Stop'
$repository = $PSScriptRoot
$runtime = Join-Path $repository '.runtime'
$manifest = Join-Path $runtime 'sandbox-api.json'
$compose = Join-Path $repository 'docker-compose.sandbox.yml'
$dll = Join-Path $runtime 'api\TradingCompany.Api.dll'
$data = Join-Path $repository 'src\TradingCompany.Api\data'

function Read-Health([string]$Url) {
    try { return Invoke-RestMethod -Uri $Url -TimeoutSec 3 } catch { return $null }
}
function Wait-Ready([string]$Url) {
    $deadline = (Get-Date).AddSeconds(45)
    do {
        if (Read-Health $Url) { return }
        Start-Sleep -Seconds 1
    } while ((Get-Date) -lt $deadline)
    throw "Service did not become ready: $Url. Inspect .runtime logs and Docker container logs."
}
function Stop-OwnedApi {
    if (-not (Test-Path -LiteralPath $manifest)) { return }
    $saved = Get-Content -LiteralPath $manifest -Raw | ConvertFrom-Json
    $running = Get-Process -Id $saved.pid -ErrorAction SilentlyContinue
    if ($running -and $running.StartTime.ToUniversalTime().Ticks -eq $saved.startedTicks) {
        $command = (Get-CimInstance Win32_Process -Filter "ProcessId=$($saved.pid)").CommandLine
        if ($command -notlike "*$dll*") { throw 'API process identity changed; no process was stopped.' }
        Stop-Process -Id $saved.pid
        $running.WaitForExit(10000) | Out-Null
    }
    Remove-Item -LiteralPath $manifest
}

if ($Action -in @('Stop', 'Restart')) {
    $containerOwner = & docker inspect quantara-sandbox-api-1 --format '{{ index .Config.Labels "com.docker.compose.project" }}' 2>$null
    if ($LASTEXITCODE -eq 0 -and $containerOwner -eq 'quantara-sandbox') {
        & docker stop quantara-sandbox-api-1
        if ($LASTEXITCODE -ne 0) { throw 'Could not stop the owned Sandbox API.' }
    }
    & docker compose -p quantara-sandbox -f $compose stop
    if ($LASTEXITCODE -ne 0) { throw 'Could not stop sandbox containers.' }
    Stop-OwnedApi
    if ($Action -eq 'Stop') { Write-Output 'Sandbox stopped. Saved accounts are unchanged.'; return }
}
if ($Action -eq 'Status') {
    & docker compose -p quantara-sandbox -f $compose ps
    $company = Read-Health 'http://localhost:8080/forward-company/status'
    if ($company) { $company | Select-Object status, modelEnabled, model, quoteCredentialsConfigured }
    else { Write-Output 'API is not reachable.' }
    return
}

& docker info --format '{{.ServerVersion}}'
if ($LASTEXITCODE -ne 0) { throw 'Start Docker Desktop, then run start-sandbox.ps1 again.' }
$codex = Join-Path $env:APPDATA 'npm\node_modules\@openai\codex\node_modules\@openai\codex-win32-x64\vendor\x86_64-pc-windows-msvc\bin\codex.exe'
if (-not (Test-Path -LiteralPath $codex)) { throw 'Codex CLI is missing. Install it and sign in with ChatGPT.' }
& $codex login status
if ($LASTEXITCODE -ne 0) { throw 'Sign in to Codex CLI before starting the AI sandbox.' }
New-Item -ItemType Directory -Force -Path $runtime | Out-Null
$reportEnvironment = Join-Path $runtime 'report-db.env'
$reportConnection = Join-Path $runtime 'report-db.json'
if (-not (Test-Path -LiteralPath $reportEnvironment) -and -not (Test-Path -LiteralPath $reportConnection)) {
    $reportPassword = [Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
    "POSTGRES_USER=quantara_reports`nPOSTGRES_DB=quantara_reports`nPOSTGRES_PASSWORD=$reportPassword" | Set-Content -LiteralPath $reportEnvironment
    @{ connectionString = "Host=localhost;Port=55435;Database=quantara_reports;Username=quantara_reports;Password=$reportPassword;Timeout=3;Command Timeout=5" } | ConvertTo-Json | Set-Content -LiteralPath $reportConnection
} elseif (-not (Test-Path -LiteralPath $reportEnvironment) -or -not (Test-Path -LiteralPath $reportConnection)) {
    throw 'Report database configuration is incomplete. Restore the missing file; credentials were not regenerated.'
}
& docker compose -p quantara-sandbox -f $compose up -d --wait report-db
if ($LASTEXITCODE -ne 0) { throw 'Report database startup failed; saved data was preserved.' }

if ($OwnerWeb) {
    $authRoot = if ($env:CODEX_HOME) { $env:CODEX_HOME } else { Join-Path $env:USERPROFILE '.codex' }
    $authFile = Join-Path $authRoot 'auth.json'
    if (-not (Test-Path -LiteralPath $authFile -PathType Leaf)) { throw 'Existing Codex sign-in is required; no new credentials were created.' }
    $settings = Get-Content -LiteralPath $reportConnection -Raw | ConvertFrom-Json
    $connectionBuilder = New-Object System.Data.Common.DbConnectionStringBuilder
    $connectionBuilder.set_ConnectionString($settings.connectionString)
    $connectionBuilder.set_Item('Host', 'report-db')
    $connectionBuilder.set_Item('Port', 5432)
    @{ connectionString = $connectionBuilder.get_ConnectionString() } | ConvertTo-Json |
        Set-Content -LiteralPath (Join-Path $runtime 'report-db.container.json') -Encoding UTF8
    $connectionBuilder = $null; $settings = $null
    $hostProxy = & docker network inspect quantara-sandbox_default --format '{{(index .IPAM.Config 0).Gateway}}'
    $parsedHostProxy = $null
    if ($LASTEXITCODE -ne 0 -or -not [Net.IPAddress]::TryParse($hostProxy, [ref]$parsedHostProxy)) { throw 'Sandbox host proxy address is unavailable.' }
    $previousAuth = $env:QUANTARA_CODEX_AUTH_FILE
    $previousProxy = $env:QUANTARA_SANDBOX_HOST_PROXY
    $previousCapital = $env:SANDBOX_INITIAL_CAPITAL
    try {
        $env:QUANTARA_CODEX_AUTH_FILE = (Resolve-Path -LiteralPath $authFile).Path
        $env:QUANTARA_SANDBOX_HOST_PROXY = $hostProxy
        $env:SANDBOX_INITIAL_CAPITAL = $InitialPaperCapital.ToString([Globalization.CultureInfo]::InvariantCulture)
        $ownerCompose = Join-Path $repository 'docker-compose.sandbox-owner.yml'
        if (Get-NetTCPConnection -State Listen -LocalPort 8080 -ErrorAction SilentlyContinue) {
            $apiOwner = & docker inspect quantara-sandbox-api-1 --format '{{ index .Config.Labels "com.docker.compose.project" }}' 2>$null
            if ($LASTEXITCODE -ne 0 -or $apiOwner -ne 'quantara-sandbox') { throw 'Port 8080 is occupied by an unowned service. No process was stopped.' }
        }
        & docker image inspect quantara-sandbox-api:latest --format '{{.Id}}' *> $null
        $imageExists = $LASTEXITCODE -eq 0
        if (-not $SkipBuild -or -not $imageExists) {
            & docker compose --env-file NUL -p quantara-sandbox -f $compose -f $ownerCompose build api
            if ($LASTEXITCODE -ne 0) { throw 'Docker Sandbox API build failed.' }
        }
        & docker compose --env-file NUL -p quantara-sandbox -f $compose -f $ownerCompose up -d --wait api pixel-office
        if ($LASTEXITCODE -ne 0) { throw 'Docker Sandbox services did not become healthy.' }
    } finally {
        $env:QUANTARA_CODEX_AUTH_FILE = $previousAuth
        $env:QUANTARA_SANDBOX_HOST_PROXY = $previousProxy
        $env:SANDBOX_INITIAL_CAPITAL = $previousCapital
    }
    Wait-Ready 'http://localhost:8080/health'
    Wait-Ready 'http://localhost:4242/'
    $company = Read-Health 'http://localhost:8080/forward-company/status'
    if (-not $company -or $company.mode -ne 'forward-paper' -or -not $company.modelEnabled) { throw 'Docker API is not the enabled forward-paper company.' }
    Write-Output 'Sandbox API, reports and Pixel are ready for the HTTPS owner frontend.'
    Write-Output "Company: $($company.status). AI uses the signed-in Codex quota. No live orders."
    return
}

$existing = Read-Health 'http://localhost:8080/health'
if (-not $existing) {
    if (Get-NetTCPConnection -State Listen -LocalPort 8080 -ErrorAction SilentlyContinue) {
        throw 'Port 8080 is occupied by another service. No process was stopped.'
    }
    if (-not $SkipBuild -or -not (Test-Path -LiteralPath $dll)) {
        & dotnet publish (Join-Path $repository 'src\TradingCompany.Api\TradingCompany.Api.csproj') -c Release -o (Join-Path $runtime 'api') --nologo
        if ($LASTEXITCODE -ne 0) { throw 'API build failed.' }
    }
    $arguments = @(
        $dll, '--urls=http://localhost:8080', '--Logging:LogLevel:Default=Warning',
        "--Reports:ConnectionFile=$reportConnection",
        '--ForwardFunding:Enabled=true', "--ForwardFunding:StatePath=$(Join-Path $data 'forward-funding-v1.json')",
        "--ForwardFunding:InitialPaperCapital=$($InitialPaperCapital.ToString([Globalization.CultureInfo]::InvariantCulture))",
        '--TradingAgents:Company:Gateway=codex-cli', "--TradingAgents:Company:CodexExecutable=$codex",
        '--TradingAgents:Company:Enabled=true', '--TradingAgents:Company:Model=codex-default',
        '--TradingAgents:Company:TimeoutSeconds=120', '--TradingAgents:Company:MaxModelCallsPerMeeting=8',
        '--TradingAgents:Company:MaxTotalTokensPerMeeting=250000',
        "--TradingAgents:Company:ArtifactPath=$(Join-Path $data 'forward-agent-artifacts')"
    )
    # Start-Process joins arguments on Windows; quote each complete argument.
    $quoted = ($arguments | ForEach-Object { '"' + $_ + '"' }) -join ' '
    $process = Start-Process -FilePath (Get-Command dotnet).Source -ArgumentList $quoted -WorkingDirectory (Split-Path $dll) -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $runtime 'api.stdout.log') -RedirectStandardError (Join-Path $runtime 'api.stderr.log')
    @{ pid = $process.Id; startedTicks = $process.StartTime.ToUniversalTime().Ticks } | ConvertTo-Json | Set-Content -LiteralPath $manifest
    Wait-Ready 'http://localhost:8080/health'
}
$company = Read-Health 'http://localhost:8080/forward-company/status'
if (-not $company -or $company.mode -ne 'forward-paper' -or -not $company.modelEnabled) {
    throw 'The existing API is not the enabled forward-paper company. Stop its owner before starting this sandbox.'
}
if ($SkipBuild) { & docker compose -p quantara-sandbox -f $compose up -d }
else { & docker compose -p quantara-sandbox -f $compose up -d --build }
if ($LASTEXITCODE -ne 0) { throw 'Sandbox container startup failed. API may still be running; inspect Status.' }
Wait-Ready 'http://localhost:3000/'
Wait-Ready 'http://localhost:4242/'
Write-Output 'Quantara sandbox ready: http://localhost:3000/'
Write-Output "Company: $($company.status). AI uses the signed-in Codex quota. No live orders."
