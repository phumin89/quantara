# Test only the activation launcher's native-output renderer. Never invoke Docker.
param([switch]$NativeChild, [int]$ChildExitCode)
if ($NativeChild) {
    [Console]::Error.WriteLine('native-progress')
    [Console]::Error.WriteLine('')
    [Console]::Error.WriteLine('native-detail')
    [Console]::Out.WriteLine('native-ready')
    exit $ChildExitCode
}
$ErrorActionPreference = 'Stop'
$consoleTestPath = $PSCommandPath
$consoleTestRoot = Split-Path -Parent $PSScriptRoot
$consoleTokens = $null
$consoleErrors = $null
$consoleAst = [System.Management.Automation.Language.Parser]::ParseFile(
    (Join-Path $PSScriptRoot 'activate-live.ps1'), [ref]$consoleTokens, [ref]$consoleErrors)
if ($consoleErrors) { throw 'Activation script parsing failed.' }
$consoleFunction = $consoleAst.FindAll({ param($node)
    $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'Invoke-ActivationDocker'
}, $true) | Select-Object -First 1
if (!$consoleFunction) { throw 'Activation output renderer is missing.' }
. ([scriptblock]::Create($consoleFunction.Extent.Text))
function docker {
    param([Parameter(ValueFromRemainingArguments = $true)][string[]]$NativeArguments)
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $consoleTestPath -NativeChild -ChildExitCode ([int]$NativeArguments[0])
}
$consoleLogRoot = Join-Path $consoleTestRoot 'work/activation-console-test'
New-Item -ItemType Directory -Force -Path $consoleLogRoot | Out-Null
foreach ($consoleExpectedExit in @(0, 3)) {
    $consoleLog = Join-Path $consoleLogRoot "native-$consoleExpectedExit.log"
    $consoleActualExit = Invoke-ActivationDocker -Arguments @("$consoleExpectedExit") -OutputPath $consoleLog
    $consoleRendered = [IO.File]::ReadAllText($consoleLog)
    if ($consoleActualExit -ne $consoleExpectedExit -or $consoleRendered -notmatch 'native-progress' -or
        $consoleRendered -notmatch 'native-detail' -or $consoleRendered -notmatch 'native-ready' -or
        $consoleRendered -match 'System.Management.Automation.RemoteException') {
        throw 'Native stderr text or exit code was not preserved.'
    }
    Write-Output "Windows PowerShell native stderr and exit $consoleExpectedExit passed."
}
