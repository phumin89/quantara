$ErrorActionPreference = 'Stop'
$telegramTestRoot = Join-Path ([System.IO.Path]::GetTempPath()) ('quantara-telegram-' + [guid]::NewGuid())
$null = [System.IO.Directory]::CreateDirectory($telegramTestRoot)
$telegramTestEnvironment = Join-Path $telegramTestRoot '.env'
[System.IO.File]::WriteAllText($telegramTestEnvironment, "TELEGRAM_BOT_TOKEN=fake`nTELEGRAM_USER_ID=123`nTELEGRAM_CHAT_ID=123")

function Invoke-RestMethod {
    param($Method, $Uri, $ContentType, $Body, $TimeoutSec)
    $telegramMethod = ([uri]$Uri).Segments[-1]
    $telegramPayload = [System.Text.Encoding]::UTF8.GetString($Body) | ConvertFrom-Json -AsHashtable
    $global:telegramTestCalls.Add(@{ Method = $telegramMethod; Payload = $telegramPayload })
    if ($telegramMethod -eq 'sendMessage') { return @{ ok = $true; result = @{ message_id = 42 } } }
    if ($telegramMethod -eq 'getUpdates') {
        if ($global:telegramTestDelivered) { Start-Sleep -Milliseconds 1100; return @{ ok = $true; result = @() } }
        $global:telegramTestDelivered = $true
        return @{ ok = $true; result = $global:telegramTestUpdates }
    }
    return @{ ok = $true; result = $true }
}

function New-MockClick {
    param([int]$Update, [string]$Action, [int]$User = 123, [int]$Message = 42, [int]$Chat = 123)
    return @{ update_id = $Update; callback_query = @{ id = "click-$Update"; data = "mock:v2:$Action"; from = @{ id = $User }; message = @{ message_id = $Message; chat = @{ id = $Chat; type = 'private' } } } }
}

function Assert-Mock {
    param([bool]$Condition, [string]$Description)
    if (-not $Condition) { throw "FAILED: $Description" }
}

foreach ($telegramCase in @('approve', 'reject', 'duplicate', 'wrong-user', 'wrong-message', 'wrong-chat')) {
    $global:telegramTestCalls = [System.Collections.Generic.List[hashtable]]::new()
    $global:telegramTestDelivered = $false
    $global:telegramTestUpdates = switch ($telegramCase) {
        'approve' { New-MockClick 1 approve }
        'reject' { New-MockClick 1 reject }
        'duplicate' { New-MockClick 1 approve; New-MockClick 2 reject }
        'wrong-user' { New-MockClick 1 approve -User 999 }
        'wrong-message' { New-MockClick 1 approve -Message 99 }
        'wrong-chat' { New-MockClick 1 approve -Chat 999 }
    }
    $telegramCaseState = Join-Path $telegramTestRoot $telegramCase
    & (Join-Path $PSScriptRoot 'Test-TelegramMock.ps1') -DurationSeconds 1 -SendMock -EnvironmentPath $telegramTestEnvironment -StateDirectory $telegramCaseState
    $telegramEdits = @($global:telegramTestCalls | Where-Object Method -eq 'editMessageText')
    $telegramSend = $global:telegramTestCalls | Where-Object Method -eq 'sendMessage' | Select-Object -First 1
    Assert-Mock ($telegramSend.Payload.text.Contains('🧪') -and $telegramSend.Payload.parse_mode -eq 'HTML') 'Readable HTML test card with emoji'
    Assert-Mock ($telegramSend.Payload.reply_markup.inline_keyboard.Count -eq 1) 'One button row'
    if ($telegramCase.StartsWith('wrong-')) {
        Assert-Mock ($telegramEdits.Count -eq 0) 'Untrusted click cannot change message'
    } else {
        Assert-Mock ($telegramEdits.Count -eq 1) 'Exactly one terminal message update'
        Assert-Mock ($telegramEdits[0].Payload.message_id -eq 42) 'Update the clicked registered message'
        Assert-Mock ($telegramEdits[0].Payload.reply_markup.inline_keyboard.Count -eq 0) 'Remove buttons after decision'
        $telegramExpected = if ($telegramCase -eq 'reject') { 'Rejected' } else { 'Approved' }
        Assert-Mock ($telegramEdits[0].Payload.text.Contains($telegramExpected)) 'First decision wins'
    }
    Assert-Mock (@($global:telegramTestCalls | Where-Object { $_.Method -notin @('sendMessage','getUpdates','editMessageText','answerCallbackQuery') }).Count -eq 0) 'Telegram-only calls'
    Write-Output "PASS: $telegramCase"
    if ($telegramCase -eq 'duplicate') {
        $global:telegramTestCalls.Clear()
        $global:telegramTestDelivered = $false
        $global:telegramTestUpdates = @(New-MockClick 3 reject)
        & (Join-Path $PSScriptRoot 'Test-TelegramMock.ps1') -DurationSeconds 1 -EnvironmentPath $telegramTestEnvironment -StateDirectory $telegramCaseState
        Assert-Mock (@($global:telegramTestCalls | Where-Object Method -eq 'editMessageText').Count -eq 0) 'Restart cannot replace first decision'
        $telegramSavedState = [System.IO.File]::ReadAllText((Join-Path $telegramCaseState 'telegram-mock-v2-123.json')) | ConvertFrom-Json -AsHashtable
        Assert-Mock ($telegramSavedState.Messages['42'].Status -eq 'Approved') 'Persisted decision survives restart'
        Write-Output 'PASS: restart replay'
    }
}
Write-Output "Telegram mock checks passed. Fixtures: $telegramTestRoot"
