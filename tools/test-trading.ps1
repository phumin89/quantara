# Headless deterministic strategy/risk/accounting checks. No broker credentials or AI sign-in are mounted.
$ErrorActionPreference = 'Stop'
$verificationRoot = Split-Path -Parent $PSScriptRoot
$filter = 'FullyQualifiedName~StrategyValidationTests|FullyQualifiedName~TradingTests|FullyQualifiedName~ForwardCryptoCostsTests|FullyQualifiedName~AiCompany|FullyQualifiedName~Coin|FullyQualifiedName~Live|FullyQualifiedName~Bitkub|FullyQualifiedName~Owner'
$mountArguments = @()
foreach ($source in @('src', 'tests', 'tools/ForwardSandboxRehearsal')) {
    $destination = if ($source -eq 'tools/ForwardSandboxRehearsal') { 'rehearsal' } else { $source }
    $mountArguments += @('--mount', "type=bind,source=$(Join-Path $verificationRoot $source),target=/inputs/$destination,readonly")
}
$testCommand = 'mkdir -p /app/tools; cp -a /inputs/src /app/src; cp -a /inputs/tests /app/tests; cp -a /inputs/rehearsal /app/tools/ForwardSandboxRehearsal; find /app -type d \( -name bin -o -name obj -o -name data \) -prune -exec rm -rf {} +; dotnet test /app/tests/TradingCompany.Domain.Tests/TradingCompany.Domain.Tests.csproj --filter ''' + $filter + ''' --verbosity minimal'
& docker run --rm @mountArguments mcr.microsoft.com/dotnet/sdk:10.0 sh -c $testCommand
if ($LASTEXITCODE -ne 0) { throw 'Headless trading verification failed; inspect the test output.' }
