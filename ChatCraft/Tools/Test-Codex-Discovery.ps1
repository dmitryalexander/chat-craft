$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$assembly=[Reflection.Assembly]::LoadFrom((Join-Path $root 'ChatCraft/Bleeding-Edge/App/ChatCraft.exe'))
$flags=[Reflection.BindingFlags]'Public,NonPublic,Instance'
$type=$assembly.GetType('CodexCraftClient')
# A deliberately obsolete explicit path must fall back to the registered package.
$stale=Join-Path $root 'ChatCraft/Bleeding-Edge/Validation/nonexistent-codex.exe'
$client=[Activator]::CreateInstance($type,$flags,$null,[object[]]@([string]$stale,[string]$root),$null)
try{
 $path=$type.GetMethod('FindExecutable',$flags).Invoke($client,$null)
 if(!(Test-Path -LiteralPath $path)){throw 'Discovery returned a missing executable'}
 if($path -eq $stale){throw 'Stale explicit path was retained'}
 "Resolved installed executable: $path"
 $task=$type.GetMethod('ListChatsAsync',$flags).Invoke($client,[object[]]@($null,$null))
 if(!$task.Wait(20000)){throw 'Read-only saved-chat listing timed out'}
 $page=$task.GetType().GetProperty('Result').GetValue($task,$null)
 $rows=$page.GetType().GetField('Chats',$flags).GetValue($page)
 "PASS: current executable discovered after stale override; read-only listing returned $($rows.Count) saved chats. No chat selected or message sent."
}finally{$client.Dispose()}
