[CmdletBinding()]
param([switch] $Plan, [switch] $Remove, [string] $SqlMasterFile)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Import-Module (Join-Path $PSScriptRoot 'QuantaraRuntime.psm1') -Force
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$buildRoot = Join-Path $repoRoot ('.runtime/builds/' + [DateTimeOffset]::UtcNow.ToString('yyyyMMddHHmmssfff'))
$services = @(Get-QuantaraRuntimePlan -ArtifactsPath $buildRoot)
$description = "Quantara managed runtime: $repoRoot"
if ($Plan) { $services; return }

if ($SqlMasterFile) {
    $sqlMaster = [IO.Path]::GetFullPath($SqlMasterFile)
    $sqlOwner = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
    $sqlPipe = Get-QuantaraSqlPipe -MasterFile $sqlMaster -OwnerSid $sqlOwner `
        -Candidates @(Get-QuantaraSqlCandidates)
    if (!$sqlPipe) { throw 'The requested original SQL instance is not running under this Windows user.' }
}

if ($Remove) {
    foreach ($service in $services) {
        $task = Get-ScheduledTask -TaskName $service.TaskName -ErrorAction SilentlyContinue
        if (!$task) { continue }
        if ($task.Description -ne $description) { throw 'Task ownership does not match this repository.' }
        Stop-ScheduledTask -TaskName $service.TaskName -ErrorAction SilentlyContinue
        Unregister-ScheduledTask -TaskName $service.TaskName -Confirm:$false
    }
    return
}

Push-Location $repoRoot
try {
    & dotnet build Quantara.sln -c Release --artifacts-path $buildRoot -p:QuantaraAutoFormat=false --verbosity quiet
    if ($LASTEXITCODE -ne 0) { throw 'Runtime build failed.' }
    & npm run build
    if ($LASTEXITCODE -ne 0) { throw 'Web build failed.' }
} finally { Pop-Location }

if ($SqlMasterFile) {
    $stateRoot = Join-Path $repoRoot '.runtime'
    New-Item -ItemType Directory -Path $stateRoot -Force | Out-Null
    @{ version = 1; masterFile = $sqlMaster; ownerSid = $sqlOwner } | ConvertTo-Json |
        Set-Content -LiteralPath (Join-Path $stateRoot 'sql-instance.json')
}

$pwshPath = (Get-Command pwsh -ErrorAction Stop).Source
$runner = Join-Path $PSScriptRoot 'Run-QuantaraService.ps1'
$userId = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
$principal = New-ScheduledTaskPrincipal -UserId $userId -LogonType Interactive -RunLevel Limited
$trigger = New-ScheduledTaskTrigger -AtLogOn -User $userId
$settings = New-ScheduledTaskSettingsSet -ExecutionTimeLimit ([TimeSpan]::Zero) -MultipleInstances IgnoreNew `
    -RestartCount 999 -RestartInterval ([TimeSpan]::FromMinutes(1)) -StartWhenAvailable `
    -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries
foreach ($service in $services) {
    $existing = Get-ScheduledTask -TaskName $service.TaskName -ErrorAction SilentlyContinue
    if ($existing -and $existing.Description -ne $description) { throw 'Task name is owned by another application.' }
    $arguments = '-NoLogo -NoProfile -NonInteractive -WindowStyle Hidden -File "{0}" -ServiceName {1} -ArtifactsPath "{2}"' `
        -f $runner, $service.Name, $buildRoot
    $action = New-ScheduledTaskAction -Execute $pwshPath -Argument $arguments -WorkingDirectory $repoRoot
    Register-ScheduledTask -TaskName $service.TaskName -Description $description -Action $action `
        -Trigger $trigger -Settings $settings -Principal $principal -Force | Out-Null
    Start-ScheduledTask -TaskName $service.TaskName
    [pscustomobject]@{ Service = $service.Name; Task = $service.TaskName; Artifacts = $buildRoot }
}
