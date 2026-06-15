# BSV Poker — packaging & installer

Three ways to install, from simplest to fanciest. All are **per-user** (no administrator rights) and install to
`%LOCALAPPDATA%\Programs\BSV Poker`, with Start-Menu and Desktop shortcuts and an entry in **Settings → Apps**.
Your wallet/profile data lives separately and is never touched by install or uninstall.

## 1. Portable ZIP (no installer at all)
1. Download `BSV-Poker-<version>-win-x64.zip` from the GitHub Release and unzip it anywhere.
2. Either run `poker.exe` straight from the folder (it's fully self-contained — no .NET needed), **or**
3. install shortcuts + an uninstall entry:
   ```powershell
   powershell -ExecutionPolicy Bypass -File Install-BsvPoker.ps1
   ```
   Uninstall any time from **Settings → Apps**, or `Uninstall-BsvPoker.ps1`.

## 2. Double-click `Setup.exe`
Download `BSV-Poker-Setup-<version>.exe` from the Release and run it. Next → next → done; it makes the shortcuts and
the uninstall entry for you.

## Building the artifacts yourself

```powershell
# from the repo root
powershell -ExecutionPolicy Bypass -File installer\package.ps1      # -> release\BSV-Poker-<ver>-win-x64(.zip)
powershell -ExecutionPolicy Bypass -File installer\build-setup.ps1  # -> a double-click Setup.exe
```

`build-setup.ps1` uses **Inno Setup** if it's installed (the nicest installer, from `BsvPoker.iss`), otherwise it
falls back to Windows' built-in **IExpress** to make a self-extracting `Setup.exe`. If neither is available, the
portable ZIP + `Install-BsvPoker.ps1` is the install path.

## Files here
| file | what it is |
|---|---|
| `Install-BsvPoker.ps1`   | the simple per-user installer (copy + shortcuts + Add/Remove entry) |
| `Uninstall-BsvPoker.ps1` | the matching uninstaller (preserves wallet/profile data) |
| `package.ps1`            | publishes `poker.exe` and assembles the portable folder + ZIP |
| `build-setup.ps1`        | builds a double-click `Setup.exe` (Inno Setup → IExpress fallback) |
| `BsvPoker.iss`           | Inno Setup script for the pro installer |

## Releases
Pushing a `v*` tag runs the GitHub Actions release workflow, which builds + tests the solution, publishes the
single-file `poker.exe`, builds the installer and the portable ZIP, and attaches all three to the GitHub Release.
