[CmdletBinding()]
param([ValidateSet('Prepare', 'Migrate', 'Start', 'Status', 'Rollback')][string] $Action = 'Prepare')

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Import-Module (Join-Path $PSScriptRoot 'QuantaraDocker.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'QuantaraRuntime.psm1') -Force
$plan = Get-QuantaraDockerPlan
$statePath = Join-Path $plan.Directory 'state.json'
$runtimeEnv = Join-Path $plan.Directory 'runtime.env'
$ready = Join-Path $plan.Directory 'ready/verified'
$mutex = [Threading.Mutex]::new($false, ('Local\' + $plan.Project + '-migration'))
try { $claimed = $mutex.WaitOne(0) } catch [Threading.AbandonedMutexException] { $claimed = $true }
if (!$claimed) { $mutex.Dispose(); throw 'Another Docker maintenance operation is running.' }

function Save-State {
    $temporary = $statePath + '.tmp'
    $script:state | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $temporary -Encoding utf8
    [IO.File]::Move($temporary, $statePath, $true)
}

function Read-EnvironmentFile([string] $Path) {
    $values = @{}
    foreach ($line in Get-Content -LiteralPath $Path) {
        $separator = $line.IndexOf('=')
        if ($separator -le 0) { continue }
        $name = $line.Substring(0, $separator).Trim()
        if ($name.StartsWith('#')) { continue }
        $values[$name] = $line.Substring($separator + 1).Trim().Trim('"', "'")
    }
    return $values
}

function Assert-NativeStopped {
    foreach ($name in @('Portfolio', 'Team', 'MarketData', 'Web', 'Sql')) {
        $task = Get-ScheduledTask -TaskName ($plan.TaskPrefix + $name) -ErrorAction SilentlyContinue
        if ($task -and $task.State -ne 'Disabled') { throw 'Native tasks must stay disabled while Docker owns the data.' }
    }
    if (@(Get-NetTCPConnection -State Listen -LocalPort 5050, 5051, 5052 -ErrorAction SilentlyContinue).Count) {
        throw 'A native API is still listening; do not run two trading runtimes.'
    }
}

function Start-Containers {
    Assert-NativeStopped
    if ($script:state.Phase -ne 'Active') { throw 'A verified migration is required before starting the containers.' }
    $taskName = $plan.TaskPrefix + 'Docker'
    $description = 'Quantara Docker Desktop startup: ' + $plan.Root
    $existing = Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue
    if ($existing -and $existing.Description -ne $description) { throw 'Docker startup task is owned by another application.' }
    $dockerDirectory = Split-Path -Parent (Get-Command docker -ErrorAction Stop).Source
    $desktop = [IO.Path]::GetFullPath((Join-Path $dockerDirectory '../../Docker Desktop.exe'))
    if (!(Test-Path -LiteralPath $desktop -PathType Leaf)) { throw 'The installed Docker Desktop application was not found.' }
    $sid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
    $taskAction = New-ScheduledTaskAction -Execute $desktop
    $taskPrincipal = New-ScheduledTaskPrincipal -UserId $sid -LogonType Interactive -RunLevel Limited
    $taskTrigger = New-ScheduledTaskTrigger -AtLogOn -User $sid
    $taskSettings = New-ScheduledTaskSettingsSet -ExecutionTimeLimit ([TimeSpan]::Zero) -MultipleInstances IgnoreNew `
        -StartWhenAvailable -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries
    Register-ScheduledTask -TaskName $taskName -Description $description -Action $taskAction -Principal $taskPrincipal `
        -Trigger $taskTrigger -Settings $taskSettings -Force | Out-Null
    if (!(Test-Path -LiteralPath $ready)) { Set-Content -LiteralPath $ready -Value $script:state.BackupId }
    Write-Output 'Starting the verified Docker runtime.'
    Invoke-QuantaraCompose -Arguments @('up', '-d', '--no-recreate', '--wait', '--wait-timeout', '240') | Out-Null
    $overview = Invoke-RestMethod 'http://127.0.0.1:3000/api/team' -TimeoutSec 20
    if (!$overview.configured) { throw 'The Docker Codex worker is not authenticated; inspect the isolated auth cache.' }
    $date = [TimeZoneInfo]::ConvertTimeBySystemTimeZoneId([DateTimeOffset]::UtcNow, 'Asia/Bangkok').ToString('yyyy-MM-dd', [Globalization.CultureInfo]::InvariantCulture)
    $brief = Invoke-RestMethod ("http://127.0.0.1:3000/api/team/brief?date=$date") -TimeoutSec 20
    Invoke-RestMethod 'http://127.0.0.1:3000/api/crypto/portfolio' -TimeoutSec 20 | Out-Null
    Write-Output ("Docker runtime ready. Retained daily rounds: $(@($brief.runs).Count). No order was approved by maintenance.")
}

try {
    if (!$IsWindows) { throw 'This cutover tool imports the existing Windows LocalDB; Compose itself runs Linux containers.' }
    New-Item -ItemType Directory -Path $plan.Directory -Force | Out-Null
    $script:state = if (Test-Path -LiteralPath $statePath) {
        Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json -AsHashtable
    } else {
        @{ Version = 1; Project = $plan.Project; Phase = 'Prepared'; BackupId = ''; NativeTasks = @(); Snapshots = @{} }
    }
    if ($state.Version -ne 1 -or $state.Project -ne $plan.Project) { throw 'Docker deployment identity mismatch.' }

    if ($Action -eq 'Status') {
        Write-Output ('Migration state: ' + $state.Phase)
        if (Test-Path -LiteralPath $runtimeEnv) { Invoke-QuantaraCompose -Arguments @('ps') }
        return
    }
    if ($Action -eq 'Rollback') {
        if ($state.Phase -notin @('Pausing', 'SourcePaused', 'Restored')) {
            throw 'Rollback is allowed only before activation. After activation, preserve the latest Docker databases and perform a verified reverse migration; never resume the stale native copy.'
        }
        if (Test-Path -LiteralPath $ready) { throw 'An activation marker exists; automatic rollback is unsafe.' }
        $state.Phase = 'Native'
        Save-State
        foreach ($task in $state.NativeTasks) {
            if ($task.Enabled) {
                Enable-ScheduledTask -TaskName $task.Name | Out-Null
                Start-ScheduledTask -TaskName $task.Name
            }
        }
        Write-Output 'Native watchers resumed using their unchanged original databases. Staging backups and SQL volume were retained.'
        return
    }
    if ($Action -eq 'Start' -or ($state.Phase -eq 'Active' -and $Action -ne 'Prepare')) { Start-Containers; return }
    if ($state.Phase -eq 'Native') { throw 'A rolled-back staging database is retained. Review/archive the staging deployment before preparing a new migration; it will not be overwritten.' }

    foreach ($directory in @('auth', 'backups', 'ready')) {
        New-Item -ItemType Directory -Path (Join-Path $plan.Directory $directory) -Force | Out-Null
    }
    if (!(Test-Path -LiteralPath $runtimeEnv)) {
        $values = @{}
        $values.QUANTARA_SQL_ADMIN_PASSWORD = [Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(32)) + 'aA1!'
        foreach ($database in $plan.Databases) {
            $password = [Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(32)) + 'aA1!'
            $values['QUANTARA_' + $database.Key + '_PASSWORD'] = $password
            $values['QUANTARA_' + $database.Key + '_CONNECTION'] =
                "Server=sql,1433;Database=$($database.Name);User ID=$($database.Login);Password=$password;Encrypt=true;TrustServerCertificate=true;"
        }
        $initialContent = ($values.Keys | Sort-Object | ForEach-Object { "$_=$($values[$_])" }) -join "`n"
        Initialize-QuantaraTradingControlToken -Path $runtimeEnv -InitialContent $initialContent
    } else {
        Initialize-QuantaraTradingControlToken -Path $runtimeEnv
    }
    Initialize-QuantaraTradingControlToken -Path $runtimeEnv -Name 'QUANTARA_MARKET_EVENTS_TOKEN'
    $credentialsPath = Join-Path $plan.Root '.env'
    $credentials = Read-EnvironmentFile $credentialsPath
    foreach ($required in @('BITKUB_API_KEY', 'BITKUB_API_SECRET')) {
        if (!$credentials.ContainsKey($required) -or !$credentials[$required]) { throw 'The existing Bitkub account credentials are required for an account-preserving cutover.' }
    }
    $credentialHash = (Get-FileHash -LiteralPath $credentialsPath -Algorithm SHA256).Hash
    if ($state.ContainsKey('SourceEnvironmentHash') -and $state.SourceEnvironmentHash -ne $credentialHash) {
        throw 'The source environment changed after preparation; review account identity before continuing.'
    }
    $allowed = @('BITKUB_API_KEY', 'BITKUB_API_SECRET', 'TELEGRAM_BOT_TOKEN', 'TELEGRAM_USER_ID', 'TELEGRAM_CHAT_ID')
    @(foreach ($name in $allowed) {
        if ($credentials.ContainsKey($name)) { "$name=$($credentials[$name])" }
    }) | Set-Content -LiteralPath (Join-Path $plan.Directory 'portfolio.env') -Encoding utf8
    $authPath = Join-Path $plan.Directory 'auth/auth.json'
    if (!(Test-Path -LiteralPath $authPath)) {
        $authHome = if ($env:CODEX_HOME) { $env:CODEX_HOME } else { Join-Path $env:USERPROFILE '.codex' }
        $sourceAuth = Join-Path $authHome 'auth.json'
        $auth = Get-Content -LiteralPath $sourceAuth -Raw | ConvertFrom-Json
        if ($auth.auth_mode -ne 'chatgpt' -or !$auth.tokens.access_token) { throw 'The existing Codex ChatGPT login is not available.' }
        Copy-Item -LiteralPath $sourceAuth -Destination $authPath
    }
    $state.SourceEnvironmentHash = $credentialHash
    Save-State
    Invoke-QuantaraCompose -Arguments @('config', '--quiet') | Out-Null
    if ((Invoke-QuantaraTool -Executable 'docker' -Arguments @('info', '--format', '{{.OSType}}') -TimeoutSeconds 10) -ne 'linux') {
        throw 'Start the local Linux Docker Engine before migration.'
    }
    Write-Output 'Building images before interrupting any native service.'
    Invoke-QuantaraCompose -Arguments @('build', '--pull') -TimeoutSeconds 1200 |
        Set-Content -LiteralPath (Join-Path $plan.Directory 'build.log')
    Invoke-QuantaraCompose -Arguments @('pull', 'sql') | Out-Null
    if ($Action -eq 'Prepare') { Write-Output 'Images and private configuration prepared. Running services and live-trading authority are unchanged.'; return }

    $binding = Get-Content -LiteralPath (Join-Path $plan.Root '.runtime/sql-instance.json') -Raw | ConvertFrom-Json
    $sid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
    if ($binding.version -ne 1 -or $binding.ownerSid -ne $sid) { throw 'The original SQL owner is not verified.' }
    $sourcePipe = Get-QuantaraSqlPipe -MasterFile $binding.masterFile -OwnerSid $sid -Candidates @(Get-QuantaraSqlCandidates)
    if (!$sourcePipe) { throw 'Start the verified original SQL instance before migration.' }
    $accountKey = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData(
        [Text.Encoding]::UTF8.GetBytes($credentials.BITKUB_API_KEY))).ToLowerInvariant()
    $accountExists = Invoke-QuantaraSql -Database Quantara -SourcePipe $sourcePipe -Query (
        "SELECT COUNT_BIG(*) FROM dbo.CryptoPortfolioLedger WHERE AccountKey='$accountKey';")
    if ($accountExists.Trim() -ne '1') { throw 'The staged Bitkub account does not match the retained source ledger.' }
    Invoke-QuantaraSql -Database Quantara -SourcePipe $sourcePipe -Query (Get-QuantaraTradingMaintenanceSql) | Out-Null
    if ($state.ContainsKey('SourceMasterFile') -and $state.SourceMasterFile -ne $binding.masterFile) {
        throw 'The source SQL identity changed during migration.'
    }
    $state.SourceMasterFile = $binding.masterFile
    Save-State
    $fingerprint = Get-Content -LiteralPath (Join-Path $plan.Root 'docker/fingerprint.sql') -Raw
    if ($state.Phase -in @('Prepared', 'Pausing')) {
        if (!$state.BackupId) { $state.BackupId = [DateTimeOffset]::UtcNow.ToString('yyyyMMddHHmmssfff', [Globalization.CultureInfo]::InvariantCulture) }
        $services = @(Get-QuantaraRuntimePlan -ArtifactsPath (Join-Path $plan.Root '.runtime/inspection'))
        $owners = foreach ($service in $services) {
            $owner = Get-QuantaraServiceOwner -Service $service
            if ($owner.Listening -and !$owner.Managed) { throw 'A port is occupied by an unverified process; migration stopped.' }
            if ($owner.Managed) { Get-CimInstance Win32_Process -Filter "ProcessId=$($owner.ProcessId)" }
        }
        if (!$state.NativeTasks.Count) {
            $state.NativeTasks = @(foreach ($name in @('Sql', 'Web', 'Team', 'Portfolio', 'MarketData')) {
                $task = Get-ScheduledTask -TaskName ($plan.TaskPrefix + $name) -ErrorAction SilentlyContinue
                if ($task) {
                    if ($task.Description -ne ('Quantara managed runtime: ' + $plan.Root)) { throw 'Native task ownership mismatch.' }
                    @{ Name = $task.TaskName; Enabled = $task.State -ne 'Disabled' }
                }
            })
        }
        $fence = $null
        for ($attempt = 0; $attempt -lt 20; $attempt++) {
            try { $fence = Start-QuantaraMigrationFence -SourcePipe $sourcePipe; break }
            catch {
                if ($attempt -eq 19) { throw }
                Write-Output 'Waiting for the current order/report to settle before pausing services.'
                Start-Sleep -Seconds 15
            }
        }
        try {
            $state.Phase = 'Pausing'
            Save-State
            foreach ($task in $state.NativeTasks | Where-Object { $_.Name -notlike '*-Sql' }) {
                Disable-ScheduledTask -TaskName $task.Name | Out-Null
                Stop-ScheduledTask -TaskName $task.Name
            }
            foreach ($owner in $owners) {
                $current = Get-CimInstance Win32_Process -Filter "ProcessId=$($owner.ProcessId)" -ErrorAction SilentlyContinue
                if ($current -and $current.CreationDate -eq $owner.CreationDate -and $current.ExecutablePath -eq $owner.ExecutablePath) {
                    Stop-Process -Id $owner.ProcessId -ErrorAction SilentlyContinue
                }
            }
            if (@(Get-NetTCPConnection -State Listen -LocalPort 3000, 5050, 5051, 5052 -ErrorAction SilentlyContinue).Count) {
                throw 'Native service ports did not close; preserve the source and retry maintenance.'
            }
            $state.Phase = 'SourcePaused'
            Save-State
        } finally {
            if (!$fence.HasExited) { $fence.Kill() }
            $fence.Dispose()
        }
    }
    if ($state.Phase -eq 'SourcePaused') {
        Write-Output 'Backing up all three retained databases; the original files remain in place.'
        $backupDirectory = Join-Path $plan.Directory ('backups/' + $state.BackupId)
        New-Item -ItemType Directory -Path $backupDirectory -Force | Out-Null
        foreach ($database in $plan.Databases) {
            $snapshot = @(Invoke-QuantaraSql -Database $database.Name -Query $fingerprint -SourcePipe $sourcePipe | ConvertFrom-Json)
            Assert-QuantaraSnapshotMatch -Database $database -Source $snapshot -Destination $snapshot
            if ($state.Snapshots.ContainsKey($database.Name)) {
                Assert-QuantaraSnapshotMatch -Database $database -Source @($state.Snapshots[$database.Name].Fingerprint) -Destination $snapshot
            } else {
                $files = @(Invoke-QuantaraSql -Database $database.Name -SourcePipe $sourcePipe -Query (
                    "SELECT name,file_id AS id,type FROM sys.database_files ORDER BY file_id FOR JSON PATH;") | ConvertFrom-Json)
                $state.Snapshots[$database.Name] = @{ Fingerprint = $snapshot; Files = $files }
                Save-State
            }
            $backupFile = Join-Path $backupDirectory ($database.Name + '.bak')
            if (!(Test-Path -LiteralPath $backupFile)) {
                Invoke-QuantaraSql -Database master -SourcePipe $sourcePipe -Query (
                    Get-QuantaraBackupSql -Database $database.Name -File $backupFile) | Out-Null
            } else {
                Invoke-QuantaraSql -Database master -SourcePipe $sourcePipe -Query (
                    "RESTORE VERIFYONLY FROM DISK=N'$($backupFile.Replace("'", "''"))' WITH CHECKSUM;") | Out-Null
            }
        }
        Invoke-QuantaraCompose -Arguments @('up', '-d', '--wait', '--wait-timeout', '240', 'sql') | Out-Null
        foreach ($database in $plan.Databases) {
            $exists = Invoke-QuantaraSql -Database master -Query ("SELECT CASE WHEN DB_ID(N'$($database.Name)') IS NULL THEN 0 ELSE 1 END;")
            if ($exists.Trim() -eq '0') {
                Invoke-QuantaraSql -Database master -Query (Get-QuantaraRestoreSql -Database $database.Name -BackupId $state.BackupId -Files @($state.Snapshots[$database.Name].Files)) | Out-Null
            }
            $restored = @(Invoke-QuantaraSql -Database $database.Name -Query $fingerprint | ConvertFrom-Json)
            Assert-QuantaraSnapshotMatch -Database $database -Source @($state.Snapshots[$database.Name].Fingerprint) -Destination $restored
        }
        Write-Output 'Verifying retained data survives a SQL container restart before activation.'
        Invoke-QuantaraCompose -Arguments @('restart', 'sql') | Out-Null
        Invoke-QuantaraCompose -Arguments @('up', '-d', '--wait', '--wait-timeout', '180', 'sql') | Out-Null
        foreach ($database in $plan.Databases) {
            $restored = @(Invoke-QuantaraSql -Database $database.Name -Query $fingerprint | ConvertFrom-Json)
            Assert-QuantaraSnapshotMatch -Database $database -Source @($state.Snapshots[$database.Name].Fingerprint) -Destination $restored
        }
        $state.Phase = 'Restored'
        Save-State
    }
    if ($state.Phase -ne 'Restored') { throw 'Unrecognized migration phase; no runtime was activated.' }
    Invoke-QuantaraSql -Database Quantara -Query (Get-QuantaraTradingMaintenanceSql) | Out-Null
    $runtimeValues = Read-EnvironmentFile $runtimeEnv
    foreach ($database in $plan.Databases) {
        $password = $runtimeValues['QUANTARA_' + $database.Key + '_PASSWORD']
        if ($password -notmatch '^[A-F0-9]{64}aA1!$') { throw 'Generated SQL credential is invalid.' }
        Invoke-QuantaraSql -Database master -Query (
            "IF SUSER_ID(N'$($database.Login)') IS NULL CREATE LOGIN [$($database.Login)] WITH PASSWORD=N'$password',CHECK_POLICY=ON;") | Out-Null
        Invoke-QuantaraSql -Database $database.Name -Query (
            "IF USER_ID(N'$($database.Login)') IS NULL CREATE USER [$($database.Login)] FOR LOGIN [$($database.Login)]; ALTER ROLE db_owner ADD MEMBER [$($database.Login)];") | Out-Null
    }
    foreach ($task in $state.NativeTasks) {
        Disable-ScheduledTask -TaskName $task.Name | Out-Null
        Stop-ScheduledTask -TaskName $task.Name
    }
    Assert-NativeStopped
    $state.Phase = 'Active'
    $state.ActivatedAt = [DateTimeOffset]::UtcNow.ToString('o')
    Save-State
    Set-Content -LiteralPath $ready -Value $state.BackupId
    Start-Containers
} finally {
    $mutex.ReleaseMutex()
    $mutex.Dispose()
}
