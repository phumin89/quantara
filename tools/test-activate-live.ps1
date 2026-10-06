# Isolated launcher regression check. No real Docker, broker, auth file or activation.
$ErrorActionPreference = 'Stop'
$testScript = Join-Path $PSScriptRoot 'activate-live.ps1'
$testDirectory = Join-Path ([IO.Path]::GetTempPath()) ('quantara-activation-test-' + [Guid]::NewGuid().ToString('N'))
$testPreviousCompanyAuth = $env:QUANTARA_CODEX_AUTH_FILE
$testCases = @(
    @{ Name='native-progress-success'; Failure=''; Activation=$true; Changed=$true },
    @{ Name='build-fails'; Failure='build'; Activation=$false; Changed=$false },
    @{ Name='verification-fails'; Failure='verify'; Activation=$false; Changed=$false },
    @{ Name='shutdown-fails'; Failure='stop'; Activation=$false; Changed=$false },
    @{ Name='ledger-fails'; Failure='ledger'; Activation=$false; Changed=$false },
    @{ Name='frontend-fails'; Failure='web'; Activation=$false; Changed=$false },
    @{ Name='executor-fails'; Failure='owner'; Activation=$false; Changed=$false },
    @{ Name='activation-fails'; Failure='activate'; Activation=$true; Changed=$true },
    @{ Name='company-fails'; Failure='company'; Activation=$true; Changed=$true },
    @{ Name='rollback-stderr-and-failure'; Failure='ledger'; RollbackFailure=$true; Activation=$false; Changed=$false }
    @{ Name='launch-fails-without-exit'; Failure='build'; NoNativeExit=$true; Activation=$false; Changed=$false }
    @{ Name='log-write-fails'; Failure='log'; InvalidLogPath=$true; Activation=$false; Changed=$false }
)

function Assert-ActivationTest([bool]$Condition, [string]$Message) {
    if (!$Condition) { throw $Message }
}

function global:docker {
    $arguments = @($args)
    $stage = if ($arguments -contains 'build') { 'build' }
        elseif ($arguments -contains '--verify-live-configuration') { 'verify' }
        elseif ($arguments -contains '--activate-full-spot-live') { 'activate' }
        elseif ($arguments -contains 'stop') { 'stop' }
        elseif ($arguments[-1] -eq 'ledger') { 'ledger' }
        elseif ($arguments[-1] -eq 'owner-web') { 'web' }
        elseif ($arguments[-1] -eq 'owner') { 'owner' }
        elseif ($arguments[-1] -eq 'company') { 'company' }
        else { throw 'Unexpected Docker call in isolated test.' }
    $global:activationTestCalls.Add($stage)
    if ($global:activationTestNoNativeExit) { Write-Output 'Synthetic launch failure without a native result.'; return }
    $failed = $global:activationTestFailure -eq $stage
    if ($stage -eq 'stop') {
        $global:activationTestStops++
        $failed = $failed -or ($global:activationTestRollbackFailure -and $global:activationTestStops -gt 1)
    }
    $exitCode = if ($failed) { 23 } else { 0 }
    # A real native stderr stream reproduces Windows PowerShell NativeCommandError.
    & $env:ComSpec /d /c ('echo Native stdout: ' + $stage + ' & echo Image quantara-live-company:latest Building 1>&2 & exit /b ' + $exitCode)
    # The stub is a function; propagate the real child exit exactly as docker.exe would.
    Set-Variable -Name LASTEXITCODE -Scope 1 -Value $global:LASTEXITCODE
}

function global:Invoke-WebRequest {
    param([string]$Uri, [int]$TimeoutSec, [switch]$UseBasicParsing)
    if (!$UseBasicParsing) { throw 'Windows health check must use basic parsing.' }
    [pscustomobject]@{StatusCode=200}
}
function global:Test-Path {
    param([string]$LiteralPath, [string]$PathType)
    if ($LiteralPath -and [IO.Path]::GetFileName($LiteralPath) -eq 'auth.json') { return $true }
    if ($PathType) { return Microsoft.PowerShell.Management\Test-Path -LiteralPath $LiteralPath -PathType $PathType }
    return Microsoft.PowerShell.Management\Test-Path -LiteralPath $LiteralPath
}

try {
    foreach ($case in $testCases) {
        $caseRoot = Join-Path $testDirectory $case.Name
        $caseTools = Join-Path $caseRoot 'tools'
        $tls = Join-Path $caseRoot '.runtime/owner-tls'
        $company = Join-Path $caseRoot '.runtime/live-company'
        New-Item -ItemType Directory -Force -Path $caseTools,$tls,$company | Out-Null
        $scriptCopy = Join-Path $caseTools 'activate-live.ps1'
        Copy-Item -LiteralPath $testScript -Destination $scriptCopy
        [IO.File]::WriteAllText((Join-Path $tls 'localhost.cer'), 'synthetic certificate')
        $account = [Guid]::NewGuid().ToString()
        $settingsPath = Join-Path $tls 'appsettings.Production.json'
        $companyPath = Join-Path $company 'advisory.json'
        $settingsOriginal = @{LiveExecution=@{AccountId=$account;CoordinatorToken=('x'*64);Enabled=$false}} | ConvertTo-Json -Depth 5
        $companyOriginal = @{LiveCompany=@{AccountId=$account;CoordinatorToken=('x'*64);Enabled=$false;
            DataPath='/company-data';Model='synthetic-model';ExecutorCertificateFile='/trust/localhost.cer'}} | ConvertTo-Json -Depth 5
        [IO.File]::WriteAllText($settingsPath, $settingsOriginal)
        [IO.File]::WriteAllText($companyPath, $companyOriginal)
        $global:activationTestCalls = New-Object 'Collections.Generic.List[string]'
        $global:activationTestFailure = $case.Failure
        $global:activationTestRollbackFailure = $case.RollbackFailure -eq $true
        $global:activationTestNoNativeExit = $case.NoNativeExit -eq $true
        $global:activationTestStops = 0
        if ($case.InvalidLogPath) {
            New-Item -ItemType Directory -Force -Path (Join-Path $caseRoot 'work/live-account-connect/activation-build.log') | Out-Null
        }
        $failure = $null
        try { & $scriptCopy -NoBrowser | Out-Null }
        catch { $failure = $_ }
        Assert-ActivationTest (($null -eq $failure) -eq ($case.Failure -eq '')) ($case.Name + ': incorrect failure handling. ' + $failure.Exception.Message)
        if ($failure) {
            Assert-ActivationTest ($failure.FullyQualifiedErrorId -ne 'NativeCommandError') ($case.Name + ': native progress caused an error.')
            if ($case.NoNativeExit) {
                Assert-ActivationTest ($failure.Exception.Message -eq 'Docker command could not be started.') 'Missing native exit code was accepted.'
            } elseif ($case.Failure -eq 'build') {
                Assert-ActivationTest ($failure.Exception.Message -like 'Live build failed*') 'Build failure was not reported from its exit code.'
            }
        }
        $activationCount = @($global:activationTestCalls | Where-Object { $_ -eq 'activate' }).Count
        Assert-ActivationTest ($activationCount -eq [int]$case.Activation) ($case.Name + ': wrong activation count.')
        $settingsAfter = [IO.File]::ReadAllText($settingsPath)
        $companyAfter = [IO.File]::ReadAllText($companyPath)
        if (!$case.Changed) {
            Assert-ActivationTest ($settingsAfter -ceq $settingsOriginal) ($case.Name + ': initial executor configuration was not preserved.')
            Assert-ActivationTest (!(($companyAfter | ConvertFrom-Json).LiveCompany.Enabled)) ($case.Name + ': CEO was enabled after failure.')
        } else {
            Assert-ActivationTest (($settingsAfter | ConvertFrom-Json).LiveExecution.Enabled) ($case.Name + ': activation attempt was not preserved.')
            Assert-ActivationTest ((($companyAfter | ConvertFrom-Json).LiveCompany.Enabled) -eq ($case.Failure -eq '')) ($case.Name + ': unsafe CEO configuration.')
        }
        Assert-ActivationTest ($env:QUANTARA_CODEX_AUTH_FILE -ceq $testPreviousCompanyAuth) ($case.Name + ': launcher environment was not restored.')
        if (!$case.NoNativeExit -and !$case.InvalidLogPath) {
            $buildLog = [IO.File]::ReadAllText((Join-Path $caseRoot 'work/live-account-connect/activation-build.log'))
            Assert-ActivationTest ($buildLog -match 'Native stdout: build' -and $buildLog -match 'Building') ($case.Name + ': build output was not retained.')
            Assert-ActivationTest ($buildLog -notmatch 'NativeCommandError|CategoryInfo') ($case.Name + ': native progress retained PowerShell error formatting.')
        }
        Write-Output ('PASS: ' + $case.Name)
    }
    Write-Output ('PASSED: ' + $testCases.Count + ' isolated launcher cases. No real Docker or financial action.')
} finally {
    Remove-Item -LiteralPath Function:\docker,Function:\Invoke-WebRequest,Function:\Test-Path -ErrorAction SilentlyContinue
    Remove-Variable activationTestCalls,activationTestFailure,activationTestRollbackFailure,activationTestNoNativeExit,activationTestStops -Scope Global -ErrorAction SilentlyContinue
    $cleanupPath = [IO.Path]::GetFullPath($testDirectory)
    $expectedPrefix = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()) + 'quantara-activation-test-'
    if (!$cleanupPath.StartsWith($expectedPrefix, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe test cleanup path.' }
    if (Microsoft.PowerShell.Management\Test-Path -LiteralPath $cleanupPath) { Remove-Item -LiteralPath $cleanupPath -Recurse -Force }
}
