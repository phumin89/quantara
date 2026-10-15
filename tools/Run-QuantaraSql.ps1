[CmdletBinding()]
param([Parameter(Mandatory)][string] $ArtifactsPath)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Import-Module (Join-Path $PSScriptRoot 'QuantaraRuntime.psm1') -Force
$service = @(Get-QuantaraRuntimePlan -ArtifactsPath $ArtifactsPath)[0]
if (Test-QuantaraDockerOwnership -RepoRoot $service.RepoRoot -Sql) { return }
$state = Get-Content -LiteralPath (Join-Path $service.RepoRoot '.runtime/sql-instance.json') -Raw | ConvertFrom-Json
$sid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
if ($state.version -ne 1 -or $state.ownerSid -ne $sid) { throw 'Original SQL owner is not verified.' }
$mutexHash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData(
    [Text.Encoding]::UTF8.GetBytes($sid + ':' + $state.masterFile.ToLowerInvariant()))).Substring(0, 20)
$mutex = [Threading.Mutex]::new($false, "Local\Quantara-Sql-$mutexHash")
try { $claimed = $mutex.WaitOne(0) }
catch [Threading.AbandonedMutexException] { $claimed = $true }
if (!$claimed) { $mutex.Dispose(); return }

try {
    $logRoot = Join-Path $service.RepoRoot '.runtime/logs'
    New-Item -ItemType Directory -Path $logRoot -Force | Out-Null
    $lastState = ''
    while ($true) {
        if (Test-QuantaraDockerOwnership -RepoRoot $service.RepoRoot -Sql) { return }
        $candidates = @(Get-QuantaraSqlCandidates)
        $pipe = Get-QuantaraSqlPipe -MasterFile $state.masterFile -OwnerSid $sid -Candidates $candidates
        $message = if ($pipe) { 'Original SQL instance is running.' } else { 'Starting original registered SQL instance.' }
        if ($message -ne $lastState) {
            Add-Content -LiteralPath (Join-Path $logRoot 'Sql.host.log') -Value (
                '{0:o} {1}' -f [DateTimeOffset]::UtcNow, $message)
            $lastState = $message
        }
        if (!$pipe) {
            $matching = @($candidates | Where-Object {
                [IO.Path]::GetFullPath($_.MasterFile).Equals($state.masterFile, [StringComparison]::OrdinalIgnoreCase)
            })
            if ($matching.Count -ne 0) { throw 'Original SQL process ownership is ambiguous; startup stopped.' }
            $registrations = @(Get-ChildItem -LiteralPath 'HKCU:/Software/Microsoft/Microsoft SQL Server/UserInstances' |
                ForEach-Object { Get-ItemProperty -LiteralPath $_.PSPath })
            $original = @($registrations | Where-Object {
                $_.DataDirectory -and [IO.Path]::GetFullPath($_.DataDirectory).TrimEnd('\').Equals(
                    (Split-Path -Parent $state.masterFile).TrimEnd('\'), [StringComparison]::OrdinalIgnoreCase)
            })
            if ($original.Count -ne 1 -or $original[0].ParentInstance -notmatch '^MSSQL[0-9]+E\.LOCALDB$') {
                throw 'Original LocalDB registration is missing or ambiguous.'
            }
            $setup = Get-ItemProperty -LiteralPath (
                'HKLM:/SOFTWARE/Microsoft/Microsoft SQL Server/' + $original[0].ParentInstance + '/Setup')
            $plan = Get-QuantaraSqlStartPlan -MasterFile $state.masterFile -OwnerSid $state.ownerSid `
                -CurrentOwnerSid $sid -Registrations $registrations -BinRoot $setup.SQLBinRoot
            foreach ($file in @($plan.Executable) + $plan.RequiredFiles) {
                if (!(Test-Path -LiteralPath $file -PathType Leaf)) { throw 'Original SQL files are incomplete; startup stopped.' }
            }
            $signature = Get-AuthenticodeSignature -LiteralPath $plan.Executable
            if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'Microsoft Corporation') {
                throw 'SQL executable publisher is not verified.'
            }
            $attempt = [DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds()
            $child = Start-Process -FilePath $plan.Executable -WorkingDirectory $plan.WorkingDirectory `
                -ArgumentList $plan.Arguments -WindowStyle Hidden -PassThru `
                -RedirectStandardOutput (Join-Path $logRoot "Sql.$attempt.out.log") `
                -RedirectStandardError (Join-Path $logRoot "Sql.$attempt.err.log")
            try { $child.WaitForExit() } finally { $child.Dispose() }
        }
        Start-Sleep -Seconds 15
    }
} finally { $mutex.ReleaseMutex(); $mutex.Dispose() }
