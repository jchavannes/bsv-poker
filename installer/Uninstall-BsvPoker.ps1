#requires -version 5
<#
  Uninstall-BsvPoker.ps1 — remove BSV Poker (per-user). No administrator rights needed.
  Close the app first. Your wallet/profile data is stored separately and is NOT removed.
#>
$ErrorActionPreference = 'SilentlyContinue'
$dest = Join-Path $env:LOCALAPPDATA 'Programs\BSV Poker'

# stop a running copy that we installed (only the one under our install dir; never your other windows)
Get-Process poker -ErrorAction SilentlyContinue | Where-Object { $_.Path -and $_.Path.StartsWith($dest, [System.StringComparison]::OrdinalIgnoreCase) } | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 300

Remove-Item (Join-Path (Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs') 'BSV Poker.lnk') -Force
Remove-Item (Join-Path ([Environment]::GetFolderPath('Desktop')) 'BSV Poker.lnk') -Force
Remove-Item 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\BSVPoker' -Recurse -Force
Remove-Item $dest -Recurse -Force

Write-Host "BSV Poker uninstalled. Your wallet/profile data was NOT removed."
