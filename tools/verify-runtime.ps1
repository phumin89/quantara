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
$runnerAst = [Management.Automation.Language.Parser]::ParseFile(
    (Join-Path $PSScriptRoot 'Run-QuantaraService.ps1'), [ref]$null, [ref]$null)
if ($runnerAst.FindAll({ param($node)
    $node -is [Management.Automation.Language.CommandAst] -and
        $node.GetCommandName() -in @('Stop-Process', 'Invoke-RestMethod', 'Invoke-WebRequest')
}, $true).Count -ne 0) { throw 'The runtime must not kill adopted processes or call business endpoints.' }
Write-Output 'Runtime checks passed: scoped tasks/artifacts, port ownership, original SQL identity, ambiguous/missing SQL protection and native helper compilation. No broker calls or task registration.'
