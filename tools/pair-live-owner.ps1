$ErrorActionPreference = 'Stop'
$container = 'quantara-owner-readonly-owner-1'
$running = & docker inspect --format '{{.State.Running}}' $container 2>$null
if ($LASTEXITCODE -ne 0 -or $running.Trim() -ne 'true') { throw "Owned owner container is not running: $container" }
$labelsJson = & docker inspect --format '{{json .Config.Labels}}' $container
if ($LASTEXITCODE -ne 0) { throw 'Could not inspect owner container labels.' }
$labels = $labelsJson | ConvertFrom-Json
if ($labels.'com.docker.compose.project' -ne 'quantara-owner-readonly' -or $labels.'com.docker.compose.service' -ne 'owner') { throw 'Owner container labels do not match the expected compose project and service.' }
$repository = Split-Path -Parent $PSScriptRoot
$windowTitle = 'Quantara - Bitkub pairing - ' + [DateTimeOffset]::UtcNow.ToOffset([TimeSpan]::FromHours(7)).ToString('HH:mm:ss')
$ownerCommand = '/d /k docker exec -it ' + $container + ' dotnet TradingCompany.LiveExecution.dll --pair-owner-client'
$terminal = Get-Command wt.exe -ErrorAction SilentlyContinue
if ($terminal) {
    $terminalCommand = '-w new new-tab --title "' + $windowTitle + '" --suppressApplicationTitle --startingDirectory "' + $repository + '" cmd.exe ' + $ownerCommand
    Start-Process -FilePath $terminal.Source -ArgumentList $terminalCommand -WorkingDirectory $repository -WindowStyle Normal
} else {
    Start-Process -FilePath 'cmd.exe' -ArgumentList $ownerCommand -WorkingDirectory $repository -WindowStyle Normal
}
Write-Output "Pairing window: $windowTitle. Closing this window does not stop Quantara."
