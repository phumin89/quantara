[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet('MarketData', 'Portfolio', 'Team', 'Web')][string] $ServiceName,
    [Parameter(Mandatory)][string] $ArtifactsPath,
    [switch] $Inspect
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Import-Module (Join-Path $PSScriptRoot 'QuantaraRuntime.psm1') -Force
$service = Get-QuantaraRuntimePlan -ArtifactsPath $ArtifactsPath | Where-Object Name -eq $ServiceName
if ($Inspect) {
    $owner = Get-QuantaraServiceOwner -Service $service
    [pscustomobject]@{
        Service = $service.Name
        Port = $service.Port
        Running = $owner.Listening
        Managed = $owner.Managed
        ProcessId = $owner.ProcessId
        TargetExists = Test-Path -LiteralPath $service.Executable
    }
    return
}

if (!(Test-Path -LiteralPath $service.Executable)) { throw 'Build the runtime before starting it.' }
if (Test-QuantaraDockerOwnership -RepoRoot $service.RepoRoot) { return }
$mutex = [Threading.Mutex]::new($false, "Local\$($service.TaskName)")
try { $claimed = $mutex.WaitOne(0) }
catch [Threading.AbandonedMutexException] { $claimed = $true }
if (!$claimed) { $mutex.Dispose(); return }

try {
    $logRoot = Join-Path $service.RepoRoot '.runtime/logs'
    New-Item -ItemType Directory -Path $logRoot -Force | Out-Null
    Add-Type -Path (Join-Path $PSScriptRoot 'WindowsPowerRequest.cs')
    $awake = [Quantara.Tools.WindowsPowerRequest]::KeepAwake()
    $lastState = ''
    while ($true) {
        if (Test-QuantaraDockerOwnership -RepoRoot $service.RepoRoot) { return }
        $owner = Get-QuantaraServiceOwner -Service $service
        $database = if (!$owner.Listening) { Get-QuantaraSqlEnvironment -Service $service } else { $null }
        $state = if ($owner.Managed) { "Adopted existing process $($owner.ProcessId)." }
            elseif ($owner.Listening) { 'Port occupied by an unverified process; waiting.' }
            elseif (!$database.Ready) { 'Waiting for the original SQL instance; no replacement database will be created.' }
            else { 'Starting managed service.' }
        if ($state -ne $lastState) {
            Add-Content -LiteralPath (Join-Path $logRoot "$ServiceName.host.log") -Value (
                "{0:o} {1} IdleSleepBlocked={2}" -f [DateTimeOffset]::UtcNow, $state, $awake)
            $lastState = $state
        }

        if (!$owner.Listening) {
            if (!$database.Ready) {
                Start-Sleep -Seconds 15
                continue
            }
            $attempt = [DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds()
            $launch = @{
                FilePath = $service.Executable
                WorkingDirectory = $service.WorkingDirectory
                WindowStyle = 'Hidden'
                PassThru = $true
                RedirectStandardOutput = Join-Path $logRoot "$ServiceName.$attempt.out.log"
                RedirectStandardError = Join-Path $logRoot "$ServiceName.$attempt.err.log"
            }
            if ($service.Arguments) { $launch.ArgumentList = $service.Arguments }
            if ($database.Environment.Count -gt 0) { $launch.Environment = $database.Environment }
            try {
                $child = Start-Process @launch
                $child.WaitForExit()
                $child.Dispose()
            } catch {
                Add-Content -LiteralPath (Join-Path $logRoot "$ServiceName.host.log") -Value (
                    "{0:o} Service launch failed; retrying after delay." -f [DateTimeOffset]::UtcNow)
            }
        }

        Start-Sleep -Seconds 15
    }
} finally {
    if ('Quantara.Tools.WindowsPowerRequest' -as [type]) {
        [Quantara.Tools.WindowsPowerRequest]::Release() | Out-Null
    }
    $mutex.ReleaseMutex()
    $mutex.Dispose()
}
