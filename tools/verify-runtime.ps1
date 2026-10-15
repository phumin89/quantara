$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if (!$IsWindows) { Write-Output 'Native runtime checks require Windows.'; return }
Import-Module (Join-Path $PSScriptRoot 'QuantaraRuntime.psm1') -Force
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$plan = @(Get-QuantaraRuntimePlan -ArtifactsPath (Join-Path $repoRoot '.runtime/test-build'))
if ($plan.Count -ne 4 -or @($plan.TaskName | Sort-Object -Unique).Count -ne 4) {
    throw 'Runtime must have four isolated service tasks.'
}

try {
    Get-QuantaraRuntimePlan -ArtifactsPath ([IO.Path]::GetPathRoot($repoRoot)) | Out-Null
    throw 'Outside artifacts were accepted.'
} catch { if ($_.Exception.Message -ne 'Runtime artifacts must stay inside this repository.') { throw } }

Add-Type -Path (Join-Path $PSScriptRoot 'WindowsPowerRequest.cs')
$listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
$listener.Start()
try {
    $port = $listener.LocalEndpoint.Port
    $fixture = [pscustomobject]@{
        Name = 'MarketData'; Port = $port; RepoRoot = $repoRoot
        Executable = $plan[0].Executable
    }
    $foreign = Get-QuantaraServiceOwner -Service $fixture
    if (!$foreign.Listening -or $foreign.Managed -or $foreign.ProcessId -ne $PID) {
        throw 'Foreign port ownership must block duplicate launch without adopting it.'
    }
} finally { $listener.Stop() }

$empty = Get-QuantaraServiceOwner -Service $fixture
if ($empty.Listening -or $empty.Managed) { throw 'A closed fixture port must be available.' }

$sqlMaster = Join-Path $repoRoot '.runtime/test-sql/master.mdf'
$sqlOwner = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
$sqlFixture = [pscustomobject]@{
    MasterFile = $sqlMaster; OwnerSid = $sqlOwner; Pipe = 'np:\\.\pipe\LOCALDB#TEST\tsql\query'
}
if ((Get-QuantaraSqlPipe -MasterFile $sqlMaster -OwnerSid $sqlOwner -Candidates @($sqlFixture)) -ne $sqlFixture.Pipe) {
    throw 'SQL recovery must bind the original master file and Windows owner.'
}
if (Get-QuantaraSqlPipe -MasterFile (Join-Path $repoRoot '.runtime/other-sql/master.mdf') -OwnerSid $sqlOwner -Candidates @($sqlFixture)) {
    throw 'Another SQL instance must never replace the retained database.'
}
if (Get-QuantaraSqlPipe -MasterFile $sqlMaster -OwnerSid 'another-owner' -Candidates @($sqlFixture)) {
    throw 'A SQL process owned by another Windows identity must never be selected.'
}
if (Get-QuantaraSqlPipe -MasterFile $sqlMaster -OwnerSid $sqlOwner -Candidates @($sqlFixture, $sqlFixture)) {
    throw 'Ambiguous SQL instance matches must fail closed.'
}
if (Get-QuantaraSqlPipe -MasterFile $sqlMaster -OwnerSid $sqlOwner -Candidates @()) {
    throw 'A missing original SQL instance must wait rather than choose another database.'
}

$registration = [pscustomobject]@{
    DataDirectory = Split-Path -Parent $sqlMaster
    InstanceName = 'LOCALDB#1234ABCD'; ParentInstance = 'MSSQL17E.LOCALDB'
}
$sqlStart = Get-QuantaraSqlStartPlan -MasterFile $sqlMaster -OwnerSid $sqlOwner -CurrentOwnerSid $sqlOwner `
    -Registrations @($registration) -BinRoot (Join-Path $repoRoot '.runtime/test-sql/bin')
if ($sqlStart.Executable -notlike '*sqlservr.exe' -or !$sqlStart.Arguments.Contains('-sLOCALDB#1234ABCD') -or
    !$sqlStart.RequiredFiles.Contains($sqlMaster)) { throw 'SQL bootstrap must reuse the registered instance and original files.' }
foreach ($probe in @(
    { Get-QuantaraSqlStartPlan -MasterFile $sqlMaster -OwnerSid 'another-owner' -CurrentOwnerSid $sqlOwner -Registrations @($registration) -BinRoot $repoRoot },
    { Get-QuantaraSqlStartPlan -MasterFile $sqlMaster -OwnerSid $sqlOwner -CurrentOwnerSid $sqlOwner -Registrations @() -BinRoot $repoRoot },
    { Get-QuantaraSqlStartPlan -MasterFile $sqlMaster -OwnerSid $sqlOwner -CurrentOwnerSid $sqlOwner -Registrations @($registration, $registration) -BinRoot $repoRoot }
)) {
    $rejected = $false
    try { & $probe | Out-Null } catch { $rejected = $true }
    if (!$rejected) { throw 'Unverified SQL bootstrap identity was accepted.' }
}
foreach ($runner in @('Run-QuantaraService.ps1', 'Run-QuantaraSql.ps1')) {
    $parseErrors = $null
    $runnerAst = [Management.Automation.Language.Parser]::ParseFile(
        (Join-Path $PSScriptRoot $runner), [ref]$null, [ref]$parseErrors)
    if ($parseErrors) { throw 'A runtime script could not be parsed.' }
    if ($runnerAst.FindAll({ param($node)
        $node -is [Management.Automation.Language.CommandAst] -and
            $node.GetCommandName() -in @('Stop-Process', 'Invoke-RestMethod', 'Invoke-WebRequest', 'Set-ItemProperty', 'Remove-Item')
    }, $true).Count -ne 0) { throw 'The runtime must not kill adopted processes, alter registry policy, delete data or call business endpoints.' }
}
Write-Output 'Runtime checks passed: scoped tasks/artifacts, port ownership, original SQL identity, ambiguous/missing SQL protection and native helper compilation. No broker calls or task registration.'
