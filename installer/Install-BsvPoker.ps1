#requires -version 5
<#
  Install-BsvPoker.ps1 — the SIMPLE installer for BSV Poker. Per-user, no administrator rights needed.

  Run it from the folder that contains poker.exe (the portable ZIP, or the files the Setup.exe extracted):
      powershell -ExecutionPolicy Bypass -File Install-BsvPoker.ps1

  It copies the app to %LOCALAPPDATA%\Programs\BSV Poker, makes Start-Menu and Desktop shortcuts, and registers
  an entry in Settings > Apps (Add/Remove Programs) so it can be uninstalled the normal way. Your wallet and
  profile data live separately and are never touched by install or uninstall.
#>
$ErrorActionPreference = 'Stop'
$appName = 'BSV Poker'
$version = '1.0.0'

$src = Join-Path $PSScriptRoot 'poker.exe'
if (-not (Test-Path $src)) { Write-Error "poker.exe was not found next to this script. Run it from the unzipped BSV Poker folder."; exit 1 }

$dest = Join-Path $env:LOCALAPPDATA 'Programs\BSV Poker'
Write-Host "Installing $appName $version to $dest ..."
New-Item -ItemType Directory -Force -Path $dest | Out-Null
Copy-Item $src (Join-Path $dest 'poker.exe') -Force
foreach ($f in 'LICENSE.txt','README.md','Uninstall-BsvPoker.ps1') {
    $p = Join-Path $PSScriptRoot $f
    if (Test-Path $p) { Copy-Item $p $dest -Force }
}
$docs = Join-Path $PSScriptRoot 'docs'
if (Test-Path $docs) { Copy-Item $docs (Join-Path $dest 'docs') -Recurse -Force }
$exe = Join-Path $dest 'poker.exe'

# --- shortcuts (Start Menu + Desktop) ---
$ws = New-Object -ComObject WScript.Shell
$startDir = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs'
$lnk = $ws.CreateShortcut((Join-Path $startDir 'BSV Poker.lnk'))
$lnk.TargetPath = $exe; $lnk.WorkingDirectory = $dest; $lnk.IconLocation = "$exe,0"; $lnk.Description = $appName; $lnk.Save()
$desk = $ws.CreateShortcut((Join-Path ([Environment]::GetFolderPath('Desktop')) 'BSV Poker.lnk'))
$desk.TargetPath = $exe; $desk.WorkingDirectory = $dest; $desk.IconLocation = "$exe,0"; $desk.Description = $appName; $desk.Save()

# --- Add/Remove Programs (per-user) ---
$key = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\BSVPoker'
New-Item -Path $key -Force | Out-Null
Set-ItemProperty $key 'DisplayName'     $appName
Set-ItemProperty $key 'DisplayVersion'  $version
Set-ItemProperty $key 'Publisher'       'BSV Poker'
Set-ItemProperty $key 'DisplayIcon'     $exe
Set-ItemProperty $key 'InstallLocation' $dest
Set-ItemProperty $key 'UninstallString' "powershell -ExecutionPolicy Bypass -File `"$(Join-Path $dest 'Uninstall-BsvPoker.ps1')`""
Set-ItemProperty $key 'NoModify' 1
Set-ItemProperty $key 'NoRepair' 1

Write-Host ""
Write-Host "$appName installed."
Write-Host "  Launch:    Start Menu > BSV Poker  (or the Desktop shortcut)"
Write-Host "  Uninstall: Settings > Apps > BSV Poker,  or run Uninstall-BsvPoker.ps1"
Write-Host "  Your wallet/profile data is stored separately and is never removed by uninstall."
