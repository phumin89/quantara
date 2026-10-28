$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Import-Module (Join-Path $PSScriptRoot 'QuantaraDocker.psm1') -Force
$node = (Get-Command node -ErrorAction Stop).Source
$failures = [Collections.Generic.List[string]]::new()

function Test-ProcessCase([string] $Name, [scriptblock] $Check) {
    try { & $Check } catch { $failures.Add($Name + ': ' + $_.Exception.Message) }
}

# Only harmless Node fixtures are launched; no Docker, SQL or app service runs.
Test-ProcessCase 'Blocked input honors the whole command deadline' {
    $watch = [Diagnostics.Stopwatch]::StartNew()
    $message = $null
    try {
        Invoke-QuantaraTool -Executable $node -Arguments @('-e', 'setTimeout(() => {}, 3500)') `
            -InputText ('x' * 1048576) -TimeoutSeconds 1 | Out-Null
    } catch { $message = $_.Exception.Message }
    if ($message -notlike 'Command timed out:*') { throw ('Expected timeout, received: ' + $message) }
    if ($watch.ElapsedMilliseconds -gt 3000) { throw 'The input write escaped its one-second deadline.' }
}

Test-ProcessCase 'UTF-8 input receives EOF and output remains trimmed' {
    $text = 'Synthetic ข้อมูล'
    $script = 'let text="";process.stdin.setEncoding("utf8");process.stdin.on("data",v=>text+=v);process.stdin.on("end",()=>process.stdout.write("  "+text.trim()+"  "));'
    $result = Invoke-QuantaraTool -Executable $node -Arguments @('-e', $script) -InputText $text -TimeoutSeconds 5
    if ($result -cne $text) { throw 'Input/output content changed or stdin was not closed.' }
}

Test-ProcessCase 'Argument boundaries do not invoke a shell' {
    $literal = 'literal space & $(ignored)'
    $result = Invoke-QuantaraTool -Executable $node -Arguments @('-e', 'process.stdout.write(process.argv[1])', $literal) -TimeoutSeconds 5
    if ($result -cne $literal) { throw 'Literal arguments changed.' }
}

Test-ProcessCase 'Nonzero exit remains a failure' {
    $message = $null
    try {
        Invoke-QuantaraTool -Executable $node -Arguments @('-e', 'process.stderr.write("synthetic fixture failure");process.exit(7)') -TimeoutSeconds 5 | Out-Null
    } catch { $message = $_.Exception.Message }
    if ($message -notlike 'Command failed:*synthetic fixture failure*') { throw ('Unexpected failure: ' + $message) }
}

Test-ProcessCase 'Process execution after EOF remains bounded' {
    $message = $null
    try {
        Invoke-QuantaraTool -Executable $node -Arguments @('-e', 'process.stdin.resume();process.stdin.on("end",()=>setTimeout(()=>{},3500))') `
            -InputText 'synthetic input' -TimeoutSeconds 1 | Out-Null
    } catch { $message = $_.Exception.Message }
    if ($message -notlike 'Command timed out:*') { throw ('Unexpected timeout failure: ' + $message) }
}

if ($failures.Count) { throw ($failures -join "`n") }
Write-Output 'Docker process helper: blocked-input deadline, UTF-8/EOF, literal arguments, failed exit and execution timeout passed; harmless Node fixtures only.'
