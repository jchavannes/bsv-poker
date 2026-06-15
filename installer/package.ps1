#requires -version 5
<#
  package.ps1 — build the BSV Poker release artifacts. No external tools required.

  Produces, under <repo>\release\ :
    * BSV-Poker-<ver>-win-x64\           a portable folder (poker.exe + installer scripts + docs + LICENSE)
    * BSV-Poker-<ver>-win-x64.zip        the portable ZIP (what users download and unzip)

  The single-file Setup.exe is built separately by build-setup.ps1 (Inno Setup or Windows IExpress).
  Usage:   powershell -ExecutionPolicy Bypass -File installer\package.ps1
#>
$ErrorActionPreference = 'Stop'
$root   = Split-Path -Parent $PSScriptRoot                  # repo root (installer\ is directly under it)
$proj   = Join-Path $root 'dotnet\src\BsvPoker.App\BsvPoker.App.csproj'
$ver    = '1.0.0'                                           # keep in sync with the csproj <Version>
$pubDir = Join-Path $root 'dotnet\src\BsvPoker.App\bin\Release\net8.0-windows\win-x64\publish'
$exe    = Join-Path $pubDir 'poker.exe'
$outRt  = Join-Path $root 'release'
$stage  = Join-Path $outRt "BSV-Poker-$ver-win-x64"

Write-Host "==> Publishing single-file poker.exe ..."
dotnet publish $proj -c Release -nologo | Out-Host
if (-not (Test-Path $exe)) { throw "publish did not produce $exe" }

Write-Host "==> Assembling portable folder $stage ..."
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Force -Path $stage | Out-Null
Copy-Item $exe (Join-Path $stage 'poker.exe') -Force
foreach ($f in 'LICENSE.txt','README.md') { Copy-Item (Join-Path $root $f) $stage -Force }
foreach ($f in 'Install-BsvPoker.ps1','Uninstall-BsvPoker.ps1') { Copy-Item (Join-Path $PSScriptRoot $f) $stage -Force }
$docsSrc = Join-Path $root 'docs'
if (Test-Path $docsSrc) { Copy-Item $docsSrc (Join-Path $stage 'docs') -Recurse -Force }
Set-Content (Join-Path $stage 'VERSION.txt') "BSV Poker $ver`r`nBuilt $(Get-Date -Format o)" -Encoding UTF8

Write-Host "==> Zipping ..."
$zip = Join-Path $outRt "BSV-Poker-$ver-win-x64.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip -Force

Write-Host ""
Write-Host "Portable folder : $stage"
Write-Host "Release ZIP     : $zip"
