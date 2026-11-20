$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath $PSScriptRoot
& (Join-Path $PSScriptRoot 'ChatCraft/Bleeding-Edge/App/ChatCraft.exe')
