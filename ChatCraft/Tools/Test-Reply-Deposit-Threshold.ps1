$ErrorActionPreference='Stop'
Add-Type -AssemblyName PresentationFramework,PresentationCore,WindowsBase,System.Windows.Forms
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$assembly=[Reflection.Assembly]::LoadFrom((Join-Path $root 'ChatCraft/Bleeding-Edge/App/ChatCraft.exe'))
$flags=[Reflection.BindingFlags]'Instance,Public,NonPublic'
$scene=[Activator]::CreateInstance($assembly.GetType('CloudSmokeView'),$true)
function SetField($name,$value){$scene.GetType().GetField($name,$flags).SetValue($scene,$value)}
function Field($name){$scene.GetType().GetField($name,$flags).GetValue($scene)}
function Call($name,[object[]]$values){$scene.GetType().GetMethod($name,$flags).Invoke($scene,$values)}
function Check($condition,$message){if(!$condition){throw $message}}
try {
 Call BeginReply
 SetField lastReplySmoke 1000.0
 Call TypeReplyLetter @(0,'a','a')
 Call TypeReplyLetter @(0,'ab','b')
 Call TypeReplyLetter @(0,'abc','c')
 Check ((Field replyPrefix) -eq '') 'Early deposit escaped threshold'
 Call FlushReplySmokeAt @(1000.049)
 Check ((Field replyPrefix) -eq '') 'Deposit fired before50ms'
 Call FlushReplySmokeAt @(1000.051)
 Check ((Field replyPrefix) -eq 'abc') 'Latest state was not deposited'
 Check ($null -eq (Field pendingReplyPrefix)) 'Backlog survived flush'
 Call FlushReplySmokeAt @(1000.2)
 Check ((Field lastReplySmoke) -eq 1000.051) 'Empty buffer deposited again'

 # Slow input: every letter arrives after the threshold and is retained individually.
 for($i=1;$i -le 20;$i++) {
  Call TypeReplyLetter @(0,('slow'+$i),'w')
  Call FlushReplySmokeAt @((1001.0+$i*0.080))
  Check ((Field replyPrefix) -eq ('slow'+$i)) 'Slow letter was skipped'
 }
 # Fast input: retained state is bounded to one newest letter, not an event queue.
 SetField lastReplySmoke 2000.0
 $deposits=0
 for($i=1;$i -le 1000;$i++) {
  Call TypeReplyLetter @(0,('fast'+$i),'t')
  $before=Field lastReplySmoke
  Call FlushReplySmokeAt @((2000.0+$i*0.001))
  if((Field lastReplySmoke) -ne $before){$deposits++}
 }
 Check ($deposits -le 20) 'Fast deposits exceeded20Hz'
 Call FlushReplySmokeAt @(2001.1)
 Check ((Field replyPrefix) -eq 'fast1000') 'Final letter state was lost'
 Call TypeReplyLetter @(1,'cancel','l')
 Call KeepReplyPigment
 Check ($null -eq (Field pendingReplyPrefix)) 'Leaving Reply retained stale work'
 Call BeginReply
 Check ([double]::IsNegativeInfinity((Field lastReplySmoke))) 'Replay threshold did not reset'
 'PASS:50ms gate, latest-only coalescing,20 slow letters retained,1000 fast events bounded to20Hz, final-state flush and phase cancellation. No window/audio/chat opened.'
}finally{$scene.Dispose()}
