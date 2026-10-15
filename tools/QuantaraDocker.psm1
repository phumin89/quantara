Set-StrictMode -Version Latest

function Get-QuantaraDockerPlan {
    $root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
    $hash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData(
        [Text.Encoding]::UTF8.GetBytes($root.ToLowerInvariant()))).Substring(0, 12)
    [pscustomobject]@{
        Root = $root
        Directory = Join-Path $root '.runtime/docker'
        Project = 'quantara-' + $hash.ToLowerInvariant()
        TaskPrefix = "Quantara-$hash-"
        Databases = @(
            [pscustomobject]@{ Name = 'Quantara'; Login = 'quantara_portfolio'; Key = 'PORTFOLIO'; Tables = @('[dbo].[CryptoPortfolioLedger]', '[dbo].[OrderProposals]', '[dbo].[OrderNotifications]', '[dbo].[TelegramCursors]') },
            [pscustomobject]@{ Name = 'QuantaraTeam'; Login = 'quantara_team'; Key = 'TEAM'; Tables = @('[dbo].[TeamRuns]', '[dbo].[CompanySchedule]') },
            [pscustomobject]@{ Name = 'QuantaraMarketData'; Login = 'quantara_market'; Key = 'MARKET'; Tables = @('[dbo].[IntradayCandles]') }
        )
    }
}

function Invoke-QuantaraTool {
    param(
        [Parameter(Mandatory)][string] $Executable,
        [Parameter(Mandatory)][string[]] $Arguments,
        [string] $InputText,
        [int] $TimeoutSeconds = 600
    )
    $start = [Diagnostics.ProcessStartInfo]::new($Executable)
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $start.RedirectStandardInput = $true
    $start.WorkingDirectory = (Get-QuantaraDockerPlan).Root
    foreach ($argument in $Arguments) { $start.ArgumentList.Add($argument) }
    $process = [Diagnostics.Process]::Start($start)
    try {
        $output = $process.StandardOutput.ReadToEndAsync()
        $errors = $process.StandardError.ReadToEndAsync()
        if ($InputText) { $process.StandardInput.WriteLine($InputText) }
        $process.StandardInput.Close()
        if (!$process.WaitForExit($TimeoutSeconds * 1000)) {
            $process.Kill($true)
            throw ('Command timed out: ' + [IO.Path]::GetFileName($Executable))
        }
        $text = $output.GetAwaiter().GetResult()
        $errorText = $errors.GetAwaiter().GetResult()
        if ($process.ExitCode -ne 0) {
            $details = ($errorText + $text).Trim()
            if ($details.Length -gt 5000) { $details = $details.Substring($details.Length - 5000) }
            throw ('Command failed: ' + [IO.Path]::GetFileName($Executable) + '. ' + $details)
        }
        return $text.Trim()
    } finally { $process.Dispose() }
}

function Invoke-QuantaraCompose {
    param([Parameter(Mandatory)][string[]] $Arguments, [string] $InputText, [int] $TimeoutSeconds = 900)
    $plan = Get-QuantaraDockerPlan
    Invoke-QuantaraTool -Executable 'docker' -TimeoutSeconds $TimeoutSeconds -InputText $InputText -Arguments (
        @('compose', '--project-name', $plan.Project, '--file', (Join-Path $plan.Root 'compose.yaml'),
            '--env-file', (Join-Path $plan.Directory 'runtime.env')) + $Arguments)
}

function Invoke-QuantaraSql {
    param([Parameter(Mandatory)][string] $Database, [Parameter(Mandatory)][string] $Query, [string] $SourcePipe)
    if ($Database -notin @('master', 'Quantara', 'QuantaraTeam', 'QuantaraMarketData')) { throw 'Unrecognized database owner.' }
    $arguments = @('-d', $Database, '-C', '-b', '-x', '-y', '0', '-w', '65535', '-l', '15', '-t', '120')
    $inputText = "SET NOCOUNT ON;`n$Query`nGO"
    if ($SourcePipe) {
        Invoke-QuantaraTool -Executable (Get-Command sqlcmd -ErrorAction Stop).Source -Arguments (
            @('-S', $SourcePipe, '-E') + $arguments) -InputText $inputText -TimeoutSeconds 180
    } else {
        Invoke-QuantaraCompose -Arguments (@('exec', '-T', 'sql', '/bin/sh', '-c',
            'SQLCMDPASSWORD="$MSSQL_SA_PASSWORD" exec /opt/mssql-tools18/bin/sqlcmd "$@"', '--',
            '-S', 'localhost', '-U', 'sa') + $arguments) -InputText $inputText -TimeoutSeconds 180
    }
}

function Get-QuantaraBackupSql {
    param([Parameter(Mandatory)][string] $Database, [Parameter(Mandatory)][string] $File)
    if ($Database -notin (Get-QuantaraDockerPlan).Databases.Name) { throw 'Unrecognized database owner.' }
    if ($File.Contains("'")) { throw 'Backup paths must not contain SQL quotes.' }
    "BACKUP DATABASE [$Database] TO DISK=N'$File' WITH COPY_ONLY,CHECKSUM; RESTORE VERIFYONLY FROM DISK=N'$File' WITH CHECKSUM;"
}

function Get-QuantaraRestoreSql {
    param([Parameter(Mandatory)][string] $Database, [Parameter(Mandatory)][string] $BackupId,
        [Parameter(Mandatory)][object[]] $Files)
    if ($Database -notin (Get-QuantaraDockerPlan).Databases.Name -or $BackupId -notmatch '^\d{17}$') {
        throw 'Unverified restore identity.'
    }
    if ($Files.Count -lt 2 -or @($Files | Where-Object { $_.type -notin @(0, 1) -or $_.id -le 0 }).Count) {
        throw 'Unsupported database files; restore stopped.'
    }
    $moves = foreach ($file in $Files) {
        $fileId = 0
        if (![int]::TryParse([string]$file.id, [ref]$fileId) -or $fileId -le 0) { throw 'Unsupported database file ID.' }
        $name = ([string]$file.name).Replace("'", "''")
        $extension = if ($file.type -eq 1) { 'ldf' } else { 'mdf' }
        "MOVE N'$name' TO N'/var/opt/mssql/data/$($Database)_$fileId.$extension'"
    }
    "IF DB_ID(N'$Database') IS NOT NULL THROW 51000,'Destination database already exists; refusing replacement.',1; " +
        "RESTORE VERIFYONLY FROM DISK=N'/backups/$BackupId/$Database.bak' WITH CHECKSUM; " +
        "RESTORE DATABASE [$Database] FROM DISK=N'/backups/$BackupId/$Database.bak' WITH CHECKSUM,RECOVERY," + ($moves -join ',') + ';'
}

function Assert-QuantaraSnapshotMatch {
    param([Parameter(Mandatory)][object] $Database, [Parameter(Mandatory)][object[]] $Source,
        [Parameter(Mandatory)][object[]] $Destination)
    foreach ($table in $Database.Tables) {
        if ($table -notin $Source.Table) { throw ('Required retained table is missing: ' + $Database.Name + '.' + $table) }
    }
    $expected = $Source | Sort-Object Table | ConvertTo-Json -Depth 5 -Compress
    $actual = $Destination | Sort-Object Table | ConvertTo-Json -Depth 5 -Compress
    if ($expected -cne $actual) { throw ('Retained data/schema fingerprint mismatch: ' + $Database.Name) }
}

function Get-QuantaraMigrationFenceSql {
    param([Parameter(Mandatory)][string] $Nonce)
    if ($Nonce -notmatch '^[A-F0-9]{64}$') { throw 'Invalid migration fence identity.' }
    (@'
SET NOCOUNT ON;
SET LOCK_TIMEOUT 10000;
USE [Quantara];
BEGIN TRANSACTION;
DECLARE @count bigint;
SELECT @count=COUNT_BIG(*) FROM dbo.OrderProposals WITH (TABLOCKX,HOLDLOCK);
IF EXISTS(SELECT 1 FROM dbo.OrderProposals WHERE State='Submitting')
    THROW 51000,'An order is in flight; migration must wait.',1;
USE [QuantaraTeam];
SELECT @count=COUNT_BIG(*) FROM dbo.TeamRuns WITH (TABLOCKX,HOLDLOCK);
IF EXISTS(SELECT 1 FROM dbo.TeamRuns WHERE ActiveSlot=1)
    THROW 51000,'An AI report is active; migration must wait.',1;
SELECT @count=COUNT_BIG(*) FROM dbo.CompanySchedule WITH (TABLOCKX,HOLDLOCK);
SET CONTEXT_INFO 0x__NONCE__;
-- Publish session context at the batch boundary while the transaction keeps its locks.
GO
WAITFOR DELAY '00:10:00';
ROLLBACK TRANSACTION;
'@).Replace('__NONCE__', $Nonce)
}

function Start-QuantaraMigrationFence {
    param([Parameter(Mandatory)][string] $SourcePipe)
    $start = [Diagnostics.ProcessStartInfo]::new((Get-Command sqlcmd -ErrorAction Stop).Source)
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardInput = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    foreach ($argument in @('-S', $SourcePipe, '-E', '-C', '-b', '-x', '-h', '-1', '-r', '1', '-l', '15', '-t', '610')) {
        $start.ArgumentList.Add($argument)
    }
    $process = [Diagnostics.Process]::Start($start)
    $nonce = [Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
    $process.StandardInput.WriteLine((Get-QuantaraMigrationFenceSql -Nonce $nonce) + "`nGO")
    $process.StandardInput.Close()
    try {
        $deadline = [DateTimeOffset]::UtcNow.AddSeconds(20)
        while ([DateTimeOffset]::UtcNow -lt $deadline) {
            if ($process.HasExited) { break }
            $ready = Invoke-QuantaraSql -Database master -SourcePipe $SourcePipe -Query (
                "SELECT COUNT_BIG(*) FROM sys.dm_exec_sessions WHERE context_info=0x$nonce AND open_transaction_count>0;")
            if ($ready.Trim() -eq '1' -and !$process.HasExited) { return $process }
            Start-Sleep -Milliseconds 100
        }
        throw 'Could not fence live order/report writes; keep the native services running and retry when idle.'
    } catch {
        if (!$process.HasExited) { $process.Kill() }
        $process.WaitForExit()
        $details = ($process.StandardError.ReadToEnd() + $process.StandardOutput.ReadToEnd()).Trim()
        $process.Dispose()
        throw ('Migration fence was not confirmed. ' + $details)
    }
}

Export-ModuleMember -Function Get-QuantaraDockerPlan, Invoke-QuantaraTool, Invoke-QuantaraCompose,
    Invoke-QuantaraSql, Get-QuantaraBackupSql, Get-QuantaraRestoreSql, Assert-QuantaraSnapshotMatch,
    Get-QuantaraMigrationFenceSql, Start-QuantaraMigrationFence
