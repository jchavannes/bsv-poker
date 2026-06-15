; BsvPoker.iss — Inno Setup script for a proper double-click BSV Poker Setup.exe.
; Build it with Inno Setup 6 (https://jrsoftware.org/isinfo.php):
;     "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" installer\BsvPoker.iss
; It expects the single-file poker.exe to already be published at:
;     dotnet\src\BsvPoker.App\bin\Release\net8.0-windows\win-x64\publish\poker.exe
; (run installer\package.ps1 first). Output: installer\Output\BSV-Poker-Setup-<ver>.exe
;
; Per-user install (no admin), Start-Menu + optional Desktop icon, clean uninstall that PRESERVES wallet data.

#define AppName "BSV Poker"
#define AppVer  "1.0.0"
#define AppExe  "poker.exe"
#define Pub     "BSV Poker"

[Setup]
AppId={{8E2C9A14-7E2B-4C2A-9C3D-BSVP0KER1000}
AppName={#AppName}
AppVersion={#AppVer}
AppPublisher={#Pub}
DefaultDirName={localappdata}\Programs\BSV Poker
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir=Output
OutputBaseFilename=BSV-Poker-Setup-{#AppVer}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\{#AppExe}
LicenseFile=..\LICENSE.txt
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

[Tasks]
Name: "desktopicon"; Description: "Create a &Desktop shortcut"; GroupDescription: "Additional shortcuts:"

[Files]
Source: "..\dotnet\src\BsvPoker.App\bin\Release\net8.0-windows\win-x64\publish\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\LICENSE.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\README.md";   DestDir: "{app}"; Flags: ignoreversion
Source: "..\docs\*";      DestDir: "{app}\docs"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}";        Filename: "{app}\{#AppExe}"
Name: "{group}\Uninstall {#AppName}"; Filename: "{uninstallexe}"
Name: "{userdesktop}\{#AppName}";  Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "Launch {#AppName} now"; Flags: nowait postinstall skipifsilent

; NOTE: uninstall removes only {app}; the wallet/profile data lives elsewhere and is intentionally preserved.
