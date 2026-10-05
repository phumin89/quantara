$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$outputFolder = Join-Path $projectRoot '.runtime\bitkub-account-check'
$checker = Join-Path $outputFolder 'BitkubAccountCheck.exe'
& dotnet publish (Join-Path $PSScriptRoot 'BitkubAccountCheck\BitkubAccountCheck.csproj') -c Release -o $outputFolder --nologo
if ($LASTEXITCODE -ne 0) { throw 'Connection checker build failed.' }
# Run the executable, not a PowerShell script in the new window. No policy changes.
Start-Process -FilePath 'cmd.exe' -ArgumentList ('/d /k ""' + $checker + '""') -WorkingDirectory $projectRoot
