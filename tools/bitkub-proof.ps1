# Capture private broker observations, or verify three before/buy/sell captures.
# No broker placement, cancellation, activation, model or AI sign-in is available here.
param([string[]]$Captures = @(), [switch]$OrderReceipts)
$ErrorActionPreference = 'Stop'
if (($OrderReceipts -and $Captures.Count -ne 1) -or (!$OrderReceipts -and $Captures.Count -notin @(0, 3))) {
    throw 'OrderReceipts requires one fresh capture; legacy proof requires three captures.'
}
$proofRoot = Split-Path -Parent $PSScriptRoot
$proofRuntime = Join-Path $proofRoot '.runtime'
$proofOutput = Join-Path $proofRuntime 'bitkub-preflight-linux'
$proofLog = Join-Path $proofRoot 'work/live-account-connect/proof-build.log'
New-Item -ItemType Directory -Force -Path $proofOutput, (Split-Path -Parent $proofLog) | Out-Null
$mounts = @('--mount', "type=bind,source=$proofRoot/src,target=/inputs/src,readonly",
    '--mount', "type=bind,source=$proofRoot/tools/BitkubAccountCheck,target=/inputs/checker,readonly",
    '--mount', "type=bind,source=$proofOutput,target=/output")
$publish = 'mkdir -p /app/tools; cp -a /inputs/src /app/src; cp -a /inputs/checker /app/tools/BitkubAccountCheck; find /app -type d \( -name bin -o -name obj -o -name data \) -prune -exec rm -rf {} +; dotnet publish /app/tools/BitkubAccountCheck/BitkubAccountCheck.csproj -c Release -o /output --nologo'
& docker run --rm @mounts mcr.microsoft.com/dotnet/sdk:10.0 sh -c $publish *> $proofLog
if ($LASTEXITCODE -ne 0) { throw 'Broker reader build failed. Inspect work/live-account-connect/proof-build.log.' }
$readerMounts = @('--mount', "type=bind,source=$proofRoot/.env,target=/check/.env,readonly",
    '--mount', "type=bind,source=$proofOutput,target=/app")
$readerArguments = @('--capture-evidence-env')
if ($Captures.Count -gt 0) {
    $readerArguments = @(if ($OrderReceipts) { '--verify-order-receipts-env' } else { '--verify-evidence-env' })
    $index = 0
    foreach ($capture in $Captures) {
        $captureFullPath = (Resolve-Path -LiteralPath $capture).Path
        if (!$captureFullPath.StartsWith($proofRuntime + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or
            !(Split-Path -Leaf $captureFullPath).StartsWith('broker-evidence-', [StringComparison]::Ordinal)) {
            throw 'Captures must be private broker-evidence files under this project .runtime.'
        }
        $containerCapture = '/check/.runtime/evidence/' + $index + '/' + (Split-Path -Leaf $captureFullPath)
        $readerMounts += @('--mount', "type=bind,source=$captureFullPath,target=$containerCapture,readonly")
        $readerArguments += $containerCapture
        $index++
    }
    $profileOutput = Join-Path $proofRuntime 'live'
    New-Item -ItemType Directory -Force -Path $profileOutput | Out-Null
    $readerMounts += @('--mount', "type=bind,source=$proofRuntime/owner-tls/appsettings.Production.json,target=/check/.runtime/owner-tls/appsettings.Production.json,readonly",
        '--mount', "type=bind,source=$profileOutput,target=/check/.runtime/live")
}
& docker run --rm --log-driver none @readerMounts --workdir /check mcr.microsoft.com/dotnet/aspnet:10.0 dotnet /app/BitkubAccountCheck.dll @readerArguments
if ($LASTEXITCODE -ne 0) { throw 'Broker evidence did not verify. Execution was not enabled.' }
if ($Captures.Count -eq 0) {
    Get-ChildItem -LiteralPath $proofOutput -Filter 'broker-evidence-*.json' |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1 -ExpandProperty FullName
}
