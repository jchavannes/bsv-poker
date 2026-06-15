#requires -version 5
<#
  build-setup.ps1 — produce a double-click BSV Poker Setup.exe.

  Strategy (best available, automatically):
    1. If Inno Setup (ISCC.exe) is installed  -> compile BsvPoker.iss into installer\Output\BSV-Poker-Setup-<ver>.exe
       (the nicest installer: wizard, licence page, Start-Menu + optional Desktop icon, clean uninstall).
    2. Otherwise, use Windows' built-in IExpress  -> a self-extracting installer\BSV-Poker-Setup.exe that unpacks
       the portable files and runs Install-BsvPoker.ps1 (same per-user install, no extra tools needed).
    3. If neither is possible, the portable ZIP from package.ps1 is still the answer (run Install-BsvPoker.ps1).

  Usage:  powershell -ExecutionPolicy Bypass -File installer\build-setup.ps1
#>
$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
$root = Split-Path -Parent $here
$ver  = '1.0.0'

# 1) always build the portable package first (publishes poker.exe + assembles the folder + zip)
& (Join-Path $here 'package.ps1')
$stage = Join-Path $root "release\BSV-Poker-$ver-win-x64"
if (-not (Test-Path (Join-Path $stage 'poker.exe'))) { throw "package.ps1 did not produce the staged folder" }

# 2) Inno Setup, if present
$iscc = (Get-Command iscc.exe -ErrorAction SilentlyContinue).Source
foreach ($p in @("${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe","$env:ProgramFiles\Inno Setup 6\ISCC.exe")) {
    if (-not $iscc -and (Test-Path $p)) { $iscc = $p }
}
if ($iscc) {
    Write-Host "==> Building Setup.exe with Inno Setup ..."
    & $iscc (Join-Path $here 'BsvPoker.iss') | Out-Host
    Write-Host "Setup.exe -> $(Join-Path $here 'Output')"
    return
}

# 3) Fallback: Windows IExpress self-extracting installer (zips the staged folder + a bootstrap that installs it)
Write-Host "==> Inno Setup not found; building a self-extracting Setup.exe with IExpress ..."
$payloadZip = Join-Path $here 'BSV-Poker-payload.zip'
if (Test-Path $payloadZip) { Remove-Item $payloadZip -Force }
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $payloadZip -Force

$bootstrap = Join-Path $here 'setup-bootstrap.ps1'
@'
$ErrorActionPreference="Stop"
$tmp = Join-Path $env:TEMP ("bsvp-setup-" + [guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Force -Path $tmp | Out-Null
Expand-Archive -Path (Join-Path $PSScriptRoot "BSV-Poker-payload.zip") -DestinationPath $tmp -Force
& (Join-Path $tmp "Install-BsvPoker.ps1")
'@ | Set-Content $bootstrap -Encoding UTF8

$outExe = Join-Path $here 'BSV-Poker-Setup.exe'
if (Test-Path $outExe) { Remove-Item $outExe -Force }
$sed = Join-Path $here '_iexpress.sed'
@"
[Version]
Class=IEXPRESS
SEDVersion=3
[Options]
PackagePurpose=InstallApp
ShowInstallProgramWindow=0
HideExtractAnimation=1
UseLongFileName=1
InsideCompressed=0
RebootMode=N
TargetName=$outExe
FriendlyName=BSV Poker Setup
AppLaunched=powershell.exe -ExecutionPolicy Bypass -File setup-bootstrap.ps1
PostInstallCmd=<None>
SourceFiles=SourceFiles
[Strings]
FILE0="setup-bootstrap.ps1"
FILE1="BSV-Poker-payload.zip"
[SourceFiles]
SourceFiles0=$here\
[SourceFiles0]
%FILE0%=
%FILE1%=
"@ | Set-Content $sed -Encoding ASCII

& "$env:SystemRoot\System32\iexpress.exe" /N /Q $sed | Out-Host
Remove-Item $sed -Force -ErrorAction SilentlyContinue
if (Test-Path $outExe) { Write-Host "Self-extracting installer -> $outExe" }
else { Write-Warning "IExpress did not produce $outExe. Use the portable ZIP + Install-BsvPoker.ps1 instead." }
