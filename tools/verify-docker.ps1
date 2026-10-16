[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$compose = Get-Content (Join-Path $root 'compose.yaml') -Raw
$module = Join-Path $PSScriptRoot 'QuantaraDocker.psm1'
Import-Module $module -Force

function Assert-Text([string] $Text, [string] $Pattern, [string] $Message) {
    if ($Text -notmatch $Pattern) { throw $Message }
}

Assert-Text $compose '127\.0\.0\.1:3000:3000' 'Web must publish only localhost port 3000.'
Assert-Text $compose 'networks:\s+application:' 'Compose must define the application network.'
Assert-Text $compose 'database:\s+internal:\s+true' 'Database network must be internal.'
Assert-Text $compose 'Codex__AuthHome:\s*/codex-auth' 'Codex auth must stay in the Team container.'
Assert-Text $compose 'source: \.?/?\.runtime/docker/auth' 'Codex auth must use an external bind mount.'
Assert-Text $compose 'restart: unless-stopped' 'Application services must restart unless stopped.'
Assert-Text $compose 'healthcheck:' 'Compose services must define health checks.'
Assert-Text $compose 'verified' 'Compose must enforce the readiness marker.'
if (([regex]::Matches($compose, '127\.0\.0\.1:\d+:\d+')).Count -ne 1) { throw 'Only the web service may publish a host port.' }
Assert-Text $compose 'mcr\.microsoft\.com/mssql/server@sha256:[a-f0-9]{64}' 'SQL upgrades must require an explicit image digest change.'
$portfolioBlock = [regex]::Match($compose, '(?ms)^  portfolio:\r?\n.*?(?=^  team:)').Value
$teamBlock = [regex]::Match($compose, '(?ms)^  team:\r?\n.*?(?=^  web:)').Value
$webBlock = [regex]::Match($compose, '(?ms)^  web:\r?\n.*?(?=^networks:)').Value
Assert-Text $portfolioBlock 'Trading__OwnerToken: \$\{QUANTARA_TRADING_CONTROL_TOKEN:\?' 'Portfolio requires the private owner token.'
Assert-Text $webBlock 'QUANTARA_TRADING_CONTROL_TOKEN: \$\{QUANTARA_TRADING_CONTROL_TOKEN:\?' 'The web server requires the same private owner token.'
if ($teamBlock -match 'TRADING_CONTROL_TOKEN|Trading__OwnerToken' -or $compose -match 'NEXT_PUBLIC_.*TRADING') { throw 'Trading authority must never enter Team or browser configuration.' }
if (([regex]::Matches($compose, '\$\{QUANTARA_TRADING_CONTROL_TOKEN:\?')).Count -ne 2) { throw 'Only Portfolio and the web server may receive the trading control token.' }

$dockerFiles = @(
    (Join-Path $root 'docker/Api.Dockerfile'),
    (Join-Path $root 'docker/Team.Dockerfile'),
    (Join-Path $root 'docker/Web.Dockerfile'),
    (Join-Path $root 'docker/http-healthcheck.mjs')
)
foreach ($file in $dockerFiles) { if (!(Test-Path $file)) { throw "Missing Docker file: $file" } }

$psFiles = @('Start-QuantaraDocker.ps1','QuantaraDocker.psm1','Run-QuantaraService.ps1','Run-QuantaraSql.ps1','Start-Quantara.ps1') | ForEach-Object { Join-Path $PSScriptRoot $_ }
foreach ($file in $psFiles) {
    $text = Get-Content -LiteralPath $file -Raw
    $approvalPath = '/' + 'approve'
    $dropDatabase = 'DROP' + ' DATABASE'
    $restoreReplace = 'RESTORE' + ' DATABASE.*REPLACE'
    $composeWipe = 'docker\s+down\s+-' + 'v'
    foreach ($bad in @($approvalPath, $dropDatabase, $restoreReplace, $composeWipe)) {
        if ($text -match $bad) { throw "Unsafe command pattern '$bad' in $file." }
    }
    $tokens = $null; $errors = $null
    [System.Management.Automation.Language.Parser]::ParseFile($file, [ref]$tokens, [ref]$errors) | Out-Null
    if ($errors.Count) { throw "PowerShell parse error in $file." }
}

function Expect-Throw([scriptblock] $Action, [string] $Message) {
    try { & $Action; throw $Message } catch { if ($_.Exception.Message -eq $Message) { throw } }
}

$plan = Get-QuantaraDockerPlan
$nonce = 'A' * 64
$fence = Get-QuantaraMigrationFenceSql -Nonce $nonce
Assert-Text $fence ('SET CONTEXT_INFO 0x' + $nonce + ';\s*(?:--[^\r\n]*\s*)?GO\s+WAITFOR') 'Fence identity must become visible before waiting with its transaction open.'
Assert-Text $fence 'BEGIN TRANSACTION;[\s\S]*TABLOCKX,HOLDLOCK[\s\S]*ROLLBACK TRANSACTION;' 'Fence must retain locks until shutdown or rollback.'
Expect-Throw { Get-QuantaraMigrationFenceSql -Nonce 'invalid' } 'Invalid fence identity was accepted.'
$launcher = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'Start-QuantaraDocker.ps1') -Raw
Assert-Text $launcher "'up', '-d', '--no-recreate', '--wait'" 'Routine startup must preserve existing containers.'
Assert-Text $launcher "\`$state.Phase -eq 'Active' -and \`$Action -ne 'Prepare'" 'Prepare must not start an already active runtime.'
Assert-Text $launcher 'Initialize-QuantaraTradingControlToken -Path \$runtimeEnv' 'Prepare must initialize and preserve the private token.'
Import-Module (Join-Path $PSScriptRoot 'QuantaraRuntime.psm1') -Force
$fixtureRoot = Join-Path $root ('work/docker-ownership-' + [Guid]::NewGuid().ToString('N'))
$fixtureDirectory = Join-Path $fixtureRoot '.runtime/docker'
New-Item -ItemType Directory -Path $fixtureDirectory -Force | Out-Null
$fixtureEnvironment = Join-Path $fixtureDirectory 'runtime.env'
$initialEnvironment = "# Retained fixture settings`nQUANTARA_PORTFOLIO_PASSWORD=unchanged-fixture`n"
$privateOutput = @(Initialize-QuantaraTradingControlToken -Path $fixtureEnvironment -InitialContent $initialEnvironment)
if ($privateOutput.Count) { throw 'Token initialization must not emit secrets.' }
$firstEnvironment = [IO.File]::ReadAllText($fixtureEnvironment)
if (!$firstEnvironment.StartsWith($initialEnvironment) -or $firstEnvironment -notmatch '(?m)^QUANTARA_TRADING_CONTROL_TOKEN=[A-F0-9]{64}\r?$') {
    throw 'Token generation must preserve existing settings and create a cryptographically random token.'
}
Initialize-QuantaraTradingControlToken -Path $fixtureEnvironment
if ([IO.File]::ReadAllText($fixtureEnvironment) -cne $firstEnvironment) { throw 'Repeated preparation rotated the token or changed retained credentials.' }
if ($IsWindows) {
    $fileAccess = Get-Acl -LiteralPath $fixtureEnvironment
    $ownerSid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
    $rules = @($fileAccess.GetAccessRules($true, $true, [Security.Principal.SecurityIdentifier]))
    if (!$fileAccess.AreAccessRulesProtected -or $rules.Count -ne 1 -or $rules[0].IdentityReference.Value -ne $ownerSid) {
        throw 'The runtime environment must be readable only by its owner.'
    }
} elseif ([IO.File]::GetUnixFileMode($fixtureEnvironment) -ne ([IO.UnixFileMode]::UserRead -bor [IO.UnixFileMode]::UserWrite)) {
    throw 'The runtime environment must have owner-only Unix permissions.'
}
[IO.File]::WriteAllText($fixtureEnvironment, $initialEnvironment)
Initialize-QuantaraTradingControlToken -Path $fixtureEnvironment
if (![IO.File]::ReadAllText($fixtureEnvironment).StartsWith($initialEnvironment)) { throw 'Adding a token changed retained runtime credentials.' }
foreach ($invalidToken in @('invalid-fixture', (('A' * 64) + "`nQUANTARA_TRADING_CONTROL_TOKEN=" + ('B' * 64)))) {
    $invalidEnvironment = $initialEnvironment + 'QUANTARA_TRADING_CONTROL_TOKEN=' + $invalidToken + "`n"
    [IO.File]::WriteAllText($fixtureEnvironment, $invalidEnvironment)
    Expect-Throw { Initialize-QuantaraTradingControlToken -Path $fixtureEnvironment } 'Invalid or duplicate runtime tokens were accepted.'
    if ([IO.File]::ReadAllText($fixtureEnvironment) -cne $invalidEnvironment) { throw 'Invalid token state must remain preserved for review.' }
}
$fixtureState = Join-Path $fixtureDirectory 'state.json'
foreach ($phase in @('Prepared', 'Pausing', 'SourcePaused', 'Restored', 'Active', 'Native')) {
    @{ Version = 1; Phase = $phase } | ConvertTo-Json | Set-Content -LiteralPath $fixtureState
    $owned = $phase -in @('Pausing', 'SourcePaused', 'Restored', 'Active')
    if ((Test-QuantaraDockerOwnership -RepoRoot $fixtureRoot) -ne $owned -or
        (Test-QuantaraDockerOwnership -RepoRoot $fixtureRoot -Sql) -ne ($phase -eq 'Active')) {
        throw 'Native startup ownership guard does not match the migration phase.'
    }
}
@{ Version = 1; Phase = 'Unknown' } | ConvertTo-Json | Set-Content -LiteralPath $fixtureState
Expect-Throw { Test-QuantaraDockerOwnership -RepoRoot $fixtureRoot } 'Unknown migration phase was accepted.'
@{ Version = 2; Phase = 'Active' } | ConvertTo-Json | Set-Content -LiteralPath $fixtureState
Expect-Throw { Test-QuantaraDockerOwnership -RepoRoot $fixtureRoot } 'Unknown migration version was accepted.'
if ($plan.Databases.Count -ne 3 -or ($plan.Databases.Login | Sort-Object -Unique).Count -ne 3) { throw 'Database ownership/logins must remain isolated.' }
if ((Get-QuantaraBackupSql -Database Quantara -File '/backups/test.bak') -notmatch 'COPY_ONLY,CHECKSUM') { throw 'Backup SQL must use COPY_ONLY and CHECKSUM.' }
Expect-Throw { Get-QuantaraBackupSql -Database Unknown -File '/backups/test.bak' } 'Unknown database was accepted.'
try { Get-QuantaraBackupSql -Database Quantara -File "bad'path" | Out-Null; throw 'Quoted backup path was accepted.' } catch { if ($_.Exception.Message -notmatch 'SQL quotes') { throw } }
try { Get-QuantaraRestoreSql -Database Quantara -BackupId 'invalid' -Files @([pscustomobject]@{ type = 0; id = 1; name = 'data' }) | Out-Null; throw 'Invalid restore identity was accepted.' } catch { if ($_.Exception.Message -notmatch 'identity') { throw } }
Expect-Throw { Get-QuantaraRestoreSql -Database Quantara -BackupId '20261015000000000' -Files @([pscustomobject]@{ type = 9; id = 1; name = 'data' }, [pscustomobject]@{ type = 1; id = 2; name = 'log' }) } 'Unsupported file type was accepted.'
Expect-Throw { Get-QuantaraRestoreSql -Database Quantara -BackupId '20261015000000000' -Files @([pscustomobject]@{ type = 0; id = 0; name = 'data' }, [pscustomobject]@{ type = 1; id = 2; name = 'log' }) } 'Unsupported file id was accepted.'
$files = @([pscustomobject]@{ type = 0; id = 1; name = "primary'o" }, [pscustomobject]@{ type = 1; id = 2; name = 'log' })
$restore = Get-QuantaraRestoreSql -Database Quantara -BackupId '20261015000000000' -Files $files
Assert-Text $restore "primary''o" 'Restore SQL must escape logical names.'
if ($restore -match 'WITH[^;]*REPLACE') { throw 'Restore SQL must never replace an existing database.' }

$db = $plan.Databases[0]
$fixture = @($db.Tables | ForEach-Object { [pscustomobject]@{ Table = $_; Rows = 1; SchemaHash = 'schema'; DataHash = 'data' } })
$json = $fixture | ConvertTo-Json -Depth 5
$source = $json | ConvertFrom-Json
Assert-QuantaraSnapshotMatch -Database $db -Source $source -Destination (($source | Sort-Object Table))
$changed = @($json | ConvertFrom-Json | ForEach-Object { $_.Rows = 2; $_ })
Expect-Throw { Assert-QuantaraSnapshotMatch -Database $db -Source $source -Destination $changed } 'Changed fingerprint was accepted.'
foreach ($field in @('SchemaHash', 'DataHash')) {
    $changed = @($json | ConvertFrom-Json)
    $changed[0].$field = 'changed'
    Expect-Throw { Assert-QuantaraSnapshotMatch -Database $db -Source $source -Destination $changed } 'Changed hash was accepted.'
}
$missing = @($source | Select-Object -Skip 1)
Expect-Throw { Assert-QuantaraSnapshotMatch -Database $db -Source $source -Destination $missing } 'Missing table was accepted.'
$extra = @($source) + [pscustomobject]@{ Table = '[dbo].[Extra]'; Rows = 0; SchemaHash = 'x'; DataHash = 'x' }
Expect-Throw { Assert-QuantaraSnapshotMatch -Database $db -Source $source -Destination $extra } 'Extra table was accepted.'
Expect-Throw { Invoke-QuantaraTool -Executable 'pwsh' -Arguments @('-NoProfile','-Command','exit 7') } 'Nonzero process exit was accepted.'

Write-Output 'Docker verifier passed deterministic source and helper checks.'
