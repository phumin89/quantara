Set-StrictMode -Version Latest

function Get-QuantaraRuntimePlan {
    param([Parameter(Mandatory)][string] $ArtifactsPath)

    $repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
    $buildRoot = [IO.Path]::GetFullPath($ArtifactsPath)
    $prefix = $repoRoot.TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
    if (!$buildRoot.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Runtime artifacts must stay inside this repository.'
    }

    $rootHash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData(
        [Text.Encoding]::UTF8.GetBytes($repoRoot.ToLowerInvariant()))).Substring(0, 12)
    $nodePath = (Get-Command node -ErrorAction Stop).Source
    foreach ($serviceName in @('MarketData', 'Portfolio', 'Team', 'Web')) {
        $web = $serviceName -eq 'Web'
        $executable = if ($web) { $nodePath } else {
            Join-Path $buildRoot "bin/$serviceName.Api/release/$serviceName.Api.exe"
        }
        $arguments = if ($web) {
            '"' + (Join-Path $repoRoot 'node_modules/next/dist/bin/next') +
                '" start --hostname 127.0.0.1 --port 3000'
        } else { '' }
        [pscustomobject]@{
            Name = $serviceName
            TaskName = "Quantara-$rootHash-$serviceName"
            RepoRoot = $repoRoot
            Executable = $executable
            Arguments = $arguments
            WorkingDirectory = if ($web) { Join-Path $repoRoot 'apps/web' } else { $repoRoot }
            Port = @{ MarketData = 5051; Portfolio = 5050; Team = 5052; Web = 3000 }[$serviceName]
        }
    }
}

function Get-QuantaraServiceOwner {
    param([Parameter(Mandatory)] $Service)

    $listeners = @(Get-NetTCPConnection -State Listen -LocalPort $Service.Port -ErrorAction SilentlyContinue)
    if ($listeners.Count -eq 0) {
        return [pscustomobject]@{ Listening = $false; Managed = $false; ProcessId = $null }
    }

    $ownerIds = @($listeners.OwningProcess | Sort-Object -Unique)
    if ($ownerIds.Count -ne 1) {
        return [pscustomobject]@{ Listening = $true; Managed = $false; ProcessId = $null }
    }

    $owner = Get-CimInstance Win32_Process -Filter "ProcessId=$($ownerIds[0])" -ErrorAction SilentlyContinue
    $managed = $false
    if ($owner -and $owner.ExecutablePath) {
        if ($Service.Name -eq 'Web') {
            $nextScript = Join-Path $Service.RepoRoot 'node_modules/next/dist/bin/next'
            $managed = $owner.ExecutablePath -eq $Service.Executable -and
                $owner.CommandLine -and $owner.CommandLine.Contains($nextScript,
                    [StringComparison]::OrdinalIgnoreCase)
        } else {
            $repoPrefix = $Service.RepoRoot.TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
            $managed = $owner.ExecutablePath.StartsWith($repoPrefix, [StringComparison]::OrdinalIgnoreCase) -and
                [IO.Path]::GetFileName($owner.ExecutablePath) -eq "$($Service.Name).Api.exe"
        }
    }

    [pscustomobject]@{ Listening = $true; Managed = [bool]$managed; ProcessId = $ownerIds[0] }
}

function Get-QuantaraSqlPipe {
    param(
        [Parameter(Mandatory)][string] $MasterFile,
        [Parameter(Mandatory)][string] $OwnerSid,
        [Parameter(Mandatory)][AllowEmptyCollection()][object[]] $Candidates
    )

    $expected = [IO.Path]::GetFullPath($MasterFile)
    $matching = @($Candidates | Where-Object {
        $_.OwnerSid -eq $OwnerSid -and
            [IO.Path]::GetFullPath($_.MasterFile).Equals($expected, [StringComparison]::OrdinalIgnoreCase)
    })
    if ($matching.Count -eq 1) { return $matching[0].Pipe }
    return $null
}

function Get-QuantaraSqlCandidates {
    $processes = @(Get-CimInstance Win32_Process -Filter 'Name="sqlservr.exe"' -ErrorAction Stop)
    foreach ($process in $processes) {
        if (!$process.CommandLine -or $process.CommandLine -notmatch '-d"([^"]+)"') { continue }
        $masterFile = $Matches[1]
        if ($process.CommandLine -notmatch '-s(LOCALDB#[0-9A-Fa-f]+)\b') { continue }
        $pipe = 'np:\\.\pipe\' + $Matches[1] + '\tsql\query'
        $owner = $process | Invoke-CimMethod -MethodName GetOwnerSid -ErrorAction Stop
        if ($owner.ReturnValue -ne 0 -or !$owner.Sid) { continue }
        [pscustomobject]@{ MasterFile = $masterFile; OwnerSid = $owner.Sid; Pipe = $pipe }
    }
}

function Get-QuantaraSqlEnvironment {
    param([Parameter(Mandatory)] $Service)

    $statePath = Join-Path $Service.RepoRoot '.runtime/sql-instance.json'
    if ($Service.Name -eq 'Web' -or !(Test-Path -LiteralPath $statePath)) {
        return [pscustomobject]@{ Ready = $true; Environment = @{} }
    }

    $state = Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json
    $currentOwner = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
    if ($state.version -ne 1 -or $state.ownerSid -ne $currentOwner) {
        throw 'Runtime SQL binding does not belong to the current Windows user.'
    }

    $pipe = Get-QuantaraSqlPipe -MasterFile $state.masterFile -OwnerSid $currentOwner `
        -Candidates @(Get-QuantaraSqlCandidates)
    if (!$pipe) { return [pscustomobject]@{ Ready = $false; Environment = @{} } }
    $catalog = @{ MarketData = 'QuantaraMarketData'; Portfolio = 'Quantara'; Team = 'QuantaraTeam' }[$Service.Name]
    if (!$catalog) { throw 'Unknown database owner.' }
    $environment = @{}
    $key = @{ MarketData = 'MarketData'; Portfolio = 'Quantara'; Team = 'Team' }[$Service.Name]
    $environment["ConnectionStrings__$key"] =
        "Server=$pipe;Database=$catalog;Integrated Security=true;Encrypt=true;TrustServerCertificate=true;"
    return [pscustomobject]@{ Ready = $true; Environment = $environment }
}

function Get-QuantaraSqlStartPlan {
    param(
        [Parameter(Mandatory)][string] $MasterFile,
        [Parameter(Mandatory)][string] $OwnerSid,
        [Parameter(Mandatory)][string] $CurrentOwnerSid,
        [Parameter(Mandatory)][AllowEmptyCollection()][object[]] $Registrations,
        [Parameter(Mandatory)][string] $BinRoot
    )

    if ($OwnerSid -ne $CurrentOwnerSid) { throw 'Original SQL owner does not match this Windows user.' }
    $master = [IO.Path]::GetFullPath($MasterFile)
    if ([IO.Path]::GetFileName($master) -ne 'master.mdf') { throw 'An existing SQL master file is required.' }
    $directory = Split-Path -Parent $master
    $matching = @($Registrations | Where-Object {
        $_.DataDirectory -and [IO.Path]::GetFullPath($_.DataDirectory).TrimEnd('\').Equals(
            $directory.TrimEnd('\'), [StringComparison]::OrdinalIgnoreCase)
    })
    if ($matching.Count -ne 1) { throw 'Original LocalDB registration is missing or ambiguous.' }
    $instance = $matching[0]
    if ($instance.InstanceName -notmatch '^LOCALDB#[0-9A-Fa-f]+$' -or
        $instance.ParentInstance -notmatch '^MSSQL[0-9]+E\.LOCALDB$') {
        throw 'Original LocalDB registration is invalid.'
    }

    $bin = [IO.Path]::GetFullPath($BinRoot)
    [pscustomobject]@{
        Executable = Join-Path $bin 'sqlservr.exe'
        WorkingDirectory = $bin
        Arguments = '-c -S{0} -s{1} -d"{2}" -l"{3}" -e"{4}"' -f $instance.ParentInstance,
            $instance.InstanceName, $master, (Join-Path $directory 'mastlog.ldf'), (Join-Path $directory 'error.log')
        RequiredFiles = @('master.mdf', 'mastlog.ldf', 'model.mdf', 'modellog.ldf', 'msdbdata.mdf', 'msdblog.ldf') |
            ForEach-Object { Join-Path $directory $_ }
    }
}

Export-ModuleMember -Function Get-QuantaraRuntimePlan, Get-QuantaraServiceOwner, Get-QuantaraSqlPipe,
    Get-QuantaraSqlCandidates, Get-QuantaraSqlEnvironment, Get-QuantaraSqlStartPlan
