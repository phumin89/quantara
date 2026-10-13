[CmdletBinding()]
param(
 [ValidateRange(1,600)][int]$DurationSeconds=300,
 [switch]$SendMock,
 [string]$EnvironmentPath=(Join-Path (Split-Path $PSScriptRoot -Parent) '.env'),
 [string]$StateDirectory=(Join-Path (Split-Path $PSScriptRoot -Parent) '.runtime')
)
$ErrorActionPreference='Stop'
$telegramSettings=@{}
foreach($line in [IO.File]::ReadLines($EnvironmentPath)) {
 if($line -match '^\s*(TELEGRAM_BOT_TOKEN|TELEGRAM_USER_ID|TELEGRAM_CHAT_ID)\s*=(.*)$') { $telegramSettings[$Matches[1]]=$Matches[2].Trim().Trim([char]34,[char]39) }
}
foreach($name in @('TELEGRAM_BOT_TOKEN','TELEGRAM_USER_ID','TELEGRAM_CHAT_ID')) { if(-not $telegramSettings[$name]) { throw "Missing $name." } }
function Invoke-TelegramMockApi {
 param([string]$Method,[hashtable]$Payload,[int]$TimeoutSeconds=15)
 try {
  $body=$Payload|ConvertTo-Json -Depth 10 -Compress
  $result=Invoke-RestMethod -Method Post -Uri ('https://api.telegram.org/bot'+$telegramSettings['TELEGRAM_BOT_TOKEN']+'/'+$Method) -ContentType 'application/json; charset=utf-8' -Body ([Text.Encoding]::UTF8.GetBytes($body)) -TimeoutSec $TimeoutSeconds
  if(-not $result.ok) { throw 'Rejected' }
  return $result.result
 } catch { throw "Telegram mock $Method failed; credentials withheld." }
}
function Save-TelegramMockState {
 [IO.File]::WriteAllText(($statePath+'.tmp'),($state|ConvertTo-Json -Depth 10 -Compress))
 [IO.File]::Move(($statePath+'.tmp'),$statePath,$true)
}
$null=[IO.Directory]::CreateDirectory($StateDirectory)
$statePath=Join-Path $StateDirectory ('telegram-mock-v2-'+$telegramSettings['TELEGRAM_CHAT_ID']+'.json')
# One listener per owner; no concurrent decisions or competing polls.
$listenerLock=[IO.File]::Open(($statePath+'.lock'),'OpenOrCreate','ReadWrite','None')
try {
 $state=@{Offset=0;Messages=@{}}
 if(Test-Path -LiteralPath $statePath) { $state=[IO.File]::ReadAllText($statePath)|ConvertFrom-Json -AsHashtable }
 if($SendMock) {
  $text="🧪 <b>TEST · No real order</b>`n`n<b>Buy BTC</b> · ฿1,000`nLimit · ฿2,800,000 / BTC`nSignal · Breakout + volume <i>(mock)</i>`n`n⏳ Awaiting approval · 5 min"
  $keyboard='{"inline_keyboard":[[{"text":"✅ Approve","callback_data":"mock:v2:approve"},{"text":"✖ Reject","callback_data":"mock:v2:reject"}]]}'|ConvertFrom-Json -AsHashtable
  # Uncertain delivery never triggers an automatic resend.
  $sent=Invoke-TelegramMockApi 'sendMessage' @{chat_id=$telegramSettings['TELEGRAM_CHAT_ID'];text=$text;parse_mode='HTML';reply_markup=$keyboard;protect_content=$true} -TimeoutSeconds 30
  $messageKey=[string]$sent.message_id
  $state.Messages[$messageKey]=@{Text=$text;Status='Pending';Displayed=$false;ExpiresAt=[DateTimeOffset]::UtcNow.AddMinutes(5).ToString('O')}
  Save-TelegramMockState
  Write-Output "Mock delivered (message $messageKey)."
 }
 $deadline=[DateTimeOffset]::UtcNow.AddSeconds($DurationSeconds)
 Write-Output 'Mock listener ready; registered messages and owner only.'
 while([DateTimeOffset]::UtcNow -lt $deadline) {
  try { $updates=Invoke-TelegramMockApi 'getUpdates' @{offset=$state.Offset;timeout=5;limit=100;allowed_updates=@('message','callback_query')} }
  catch { Write-Output 'Polling unavailable; retrying read only.'; Start-Sleep -Seconds 1; continue }
  foreach($update in $updates) {
   $callback=$update.callback_query
   $messageKey=[string]$callback.message.message_id
   if($null -ne $callback -and [string]$callback.from.id -eq $telegramSettings['TELEGRAM_USER_ID'] -and [string]$callback.message.chat.id -eq $telegramSettings['TELEGRAM_CHAT_ID'] -and $callback.message.chat.type -eq 'private' -and $state.Messages.ContainsKey($messageKey) -and $callback.data -in @('mock:v2:approve','mock:v2:reject')) {
    $message=$state.Messages[$messageKey]
    if($message.Status -eq 'Pending') {
     $message.Status=if([DateTimeOffset]::UtcNow -ge [DateTimeOffset]::Parse($message.ExpiresAt)) {'Expired'} elseif($callback.data -eq 'mock:v2:approve') {'Approved'} else {'Rejected'}
     Save-TelegramMockState
    }
    if(-not $message.Displayed) {
     $icon=switch($message.Status) {'Approved' {'✅'} 'Rejected' {'✖'} default {'⌛'}}
     $resultText=$message.Text -replace '⏳ Awaiting approval · 5 min',"$icon <b>$($message.Status)</b> · Mock only"
     try {
      $null=Invoke-TelegramMockApi 'editMessageText' @{chat_id=$telegramSettings['TELEGRAM_CHAT_ID'];message_id=[long]$messageKey;text=$resultText;parse_mode='HTML';reply_markup=@{inline_keyboard=@()}}
      $message.Displayed=$true
      Save-TelegramMockState
      Write-Output "$($message.Status) (message $messageKey); buttons removed; no broker calls."
     } catch { Write-Output 'Display unconfirmed; retained decision for retry.'; break }
    }
    try { $null=Invoke-TelegramMockApi 'answerCallbackQuery' @{callback_query_id=$callback.id;text="$($message.Status) · Test only. No real order."} -TimeoutSeconds 3 }
    catch { Write-Output 'Popup unavailable; message contains result.' }
   }
   $state.Offset=[long]$update.update_id+1
   Save-TelegramMockState
  }
 }
 Write-Output 'Mock listener stopped.'
} finally { $listenerLock.Dispose() }
