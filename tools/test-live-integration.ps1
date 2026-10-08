$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$testNetwork = 'quantara-live-tests-' + [Guid]::NewGuid().ToString('N')
if ($testNetwork -notmatch '^quantara-live-tests-[a-f0-9]{32}$') { throw 'Invalid isolated test network.' }
& docker network create $testNetwork | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Test network failed.' }
try {
    $testMounts = @('--mount', "type=bind,source=$(Join-Path $repoRoot 'src'),target=/inputs/src,readonly",
        '--mount', "type=bind,source=$(Join-Path $repoRoot 'tests'),target=/inputs/tests,readonly",
        '--mount', 'type=bind,source=/var/run/docker.sock,target=/var/run/docker.sock')
    $testCommand = 'mkdir -p /app; cp -a /inputs/src /app/src; cp -a /inputs/tests /app/tests; find /app -type d \( -name bin -o -name obj -o -name data \) -prune -exec rm -rf {} +; dotnet test /app/tests/TradingCompany.LiveIntegration.Tests/TradingCompany.LiveIntegration.Tests.csproj --nologo -- RunConfiguration.TreatNoTestsAsError=true'
    & docker run --rm --network $testNetwork -e "QUANTARA_TEST_DOCKER_NETWORK=$testNetwork" @testMounts quantara-live-test-sdk:latest sh -c $testCommand
    if ($LASTEXITCODE -ne 0) { throw 'Integration tests failed.' }
} finally {
    & docker network rm $testNetwork | Out-Null
    if ($LASTEXITCODE -ne 0) { Write-Warning 'Isolated test network was not removed.' }
}
