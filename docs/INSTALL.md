# Installing BSV Poker on Windows

This guide explains how to install, run, update, and uninstall **BSV Poker** on Windows. It is
written for non-technical users — you do not need to understand any of the underlying technology
to follow it.

BSV Poker ships as a **single, self-contained Windows program** called `poker.exe`. It already
includes everything it needs to run, so you do **not** have to install .NET, Java, or any other
runtime first. Just get it onto your machine using one of the three methods below.

---

## Table of contents

1. [System requirements](#1-system-requirements)
2. [Which install method should I use?](#2-which-install-method-should-i-use)
3. [Method 1: Setup.exe (recommended)](#3-method-1-setupexe-recommended)
4. [Method 2: the PowerShell installer (portable ZIP + shortcuts)](#4-method-2-the-powershell-installer-portable-zip--shortcuts)
5. [Method 3: the portable ZIP (no installation)](#5-method-3-the-portable-zip-no-installation)
6. [First launch](#6-first-launch)
7. [Where the program and your data live](#7-where-the-program-and-your-data-live)
8. [Updating to a new version](#8-updating-to-a-new-version)
9. [Uninstalling](#9-uninstalling)
10. [Running more than one copy](#10-running-more-than-one-copy)
11. [Windows SmartScreen and antivirus prompts](#11-windows-smartscreen-and-antivirus-prompts)
12. [Firewall prompt](#12-firewall-prompt)
13. [Troubleshooting installation](#13-troubleshooting-installation)

---

## 1. System requirements

- **Windows 10 or Windows 11**, 64-bit.
- A few hundred megabytes of free disk space.
- An internet connection (the app connects directly to the Bitcoin network and to other
  players).
- **No** .NET runtime, Visual C++ redistributable, or other prerequisite is required — the
  build is fully self-contained.

All install methods are **per-user**: they install into your own user profile and do **not**
require administrator rights.

---

## 2. Which install method should I use?

| If you want… | Use |
|--------------|-----|
| The simplest experience: double-click and go | **Setup.exe** (Method 1) |
| The portable build but with Start-Menu/Desktop shortcuts and an uninstall entry | **The PowerShell installer** (Method 2) |
| To run with no installation at all (for example from a USB stick or a folder you control) | **The portable ZIP** (Method 3) |

All three put the app in the same place and use the same separate data folder, so you can move
between them freely without losing your wallet.

---

## 3. Method 1: Setup.exe (recommended)

1. Download `BSV-Poker-Setup-<version>.exe` from the official GitHub Release page.
2. Double-click it.
3. Click through the prompts (next → next → done).
4. When it finishes you will have:
   - the app installed in `%LOCALAPPDATA%\Programs\BSV Poker`,
   - a **Start-Menu** shortcut,
   - a **Desktop** shortcut,
   - an entry in **Settings → Apps** (so you can uninstall later).

That's it — launch it from the Start menu or the Desktop shortcut.

> Behind the scenes the Setup.exe is built with Inno Setup when available (the nicest installer),
> and otherwise falls back to a Windows self-extracting installer. Either way the experience is
> the same: double-click and follow the prompts.

---

## 4. Method 2: the PowerShell installer (portable ZIP + shortcuts)

If you downloaded the portable ZIP but still want shortcuts and an uninstall entry, use the
included PowerShell installer.

1. Download `BSV-Poker-<version>-win-x64.zip` from the GitHub Release and unzip it anywhere.
2. Open the unzipped folder.
3. Run the installer. The simplest way is to open a PowerShell window in that folder and run:

   ```powershell
   powershell -ExecutionPolicy Bypass -File Install-BsvPoker.ps1
   ```

4. This copies the app into `%LOCALAPPDATA%\Programs\BSV Poker`, creates Start-Menu and Desktop
   shortcuts, and adds an entry to **Settings → Apps**.

To remove it later, uninstall from **Settings → Apps**, or run `Uninstall-BsvPoker.ps1`. The
uninstaller preserves your wallet and profile data.

> The `-ExecutionPolicy Bypass` part simply allows that one script to run; it does not change any
> system-wide PowerShell setting.

---

## 5. Method 3: the portable ZIP (no installation)

1. Download `BSV-Poker-<version>-win-x64.zip` from the GitHub Release.
2. Unzip it anywhere you like — a folder in your Documents, an external drive, wherever.
3. Open the folder and double-click **`poker.exe`**.

That's the whole process. There is nothing to install, no shortcuts are created, and no
uninstall entry is added. To "uninstall," just delete the folder. (Your wallet and profile data
live elsewhere — see [section 7](#7-where-the-program-and-your-data-live) — so deleting the
folder does not delete your money. Keep your **seed** backed up regardless.)

---

## 6. First launch

The first time you launch BSV Poker it walks you through a short, sequential sign-in:

1. **Select your wallet** — choose an existing wallet, or create a new one.
2. **Password** — set a password for a new wallet (this encrypts your keys on disk), or enter
   your existing password.
3. The main window titled **"BSV Poker"** opens.

For the full first-run walkthrough, see [USER_GUIDE.md](USER_GUIDE.md), section 4.

> A brand-new wallet is **empty**. To play for real you fund it (Wallet → Receive). To practice
> for free, switch the network selector to **Testnet** or **Regtest**.

---

## 7. Where the program and your data live

There are two distinct locations, and the distinction matters:

- **The program** lives in
  `%LOCALAPPDATA%\Programs\BSV Poker`
  (for the portable ZIP run in place, the program is wherever you unzipped it).

- **Your wallet and profile data** live separately in
  `%LOCALAPPDATA%\BsvPoker\profiles\`
  with one folder per profile (`p1`, `p2`, …). Each profile folder holds that profile's wallet
  file, identity, downloaded block headers, chat history, and card vault.

You can open the data folder by pasting `%LOCALAPPDATA%\BsvPoker\profiles` into the Windows
Explorer address bar.

Because the two locations are separate, **uninstalling or deleting the program never touches your
wallet or your money.** Your true backup is your **seed** (see [USER_GUIDE.md](USER_GUIDE.md),
section 6.5) — keep it written down somewhere safe.

---

## 8. Updating to a new version

To update, you simply install the newer version over the old one:

- **If you used Setup.exe:** download the new `BSV-Poker-Setup-<version>.exe` and run it. It
  replaces the existing install.
- **If you used the PowerShell installer:** download the new ZIP, unzip it, and run
  `Install-BsvPoker.ps1` again — it overwrites the installed copy.
- **If you run the portable ZIP:** download the new ZIP and replace the old `poker.exe` (or
  unzip into a fresh folder and run from there).

In every case your **wallet and profile data are untouched** by the update, because they live in
the separate data folder. After updating, just launch the app and select your existing wallet as
usual.

> Tip: it is good practice to confirm your **seed** is backed up before any update — not because
> updates are risky, but because a verified backup is always the right safety net.

---

## 9. Uninstalling

- **Setup.exe / PowerShell installer:** open **Windows Settings → Apps → Installed apps**, find
  **BSV Poker**, and choose **Uninstall**. (You can also run `Uninstall-BsvPoker.ps1` for the
  PowerShell install.)
- **Portable ZIP:** just delete the folder you unzipped.

**Your wallet and profile data are preserved** in all cases — uninstalling removes only the
program. If you later reinstall, your wallets will still be there to select.

If you genuinely want to remove everything, after uninstalling you can also delete the data
folder at `%LOCALAPPDATA%\BsvPoker`. **Do this only if you are certain** — deleting that folder
removes your wallet files. As long as you have your **seed** written down, you could still
restore the wallet on any machine, but without the seed the funds would be unrecoverable.

---

## 10. Running more than one copy

You can run **two or more copies** of BSV Poker at the same time. Each running copy claims its
own profile (its own wallet and identity), so a second copy is a genuinely **different player** —
not a clone. This is exactly what you want for testing two players on one machine, or running a
friend's seat alongside yours. Nothing special is required: just launch the app again.

---

## 11. Windows SmartScreen and antivirus prompts

Because BSV Poker is an independent application, Windows SmartScreen or your antivirus may show a
caution the first time you run it. This is normal for newly published software. If you trust the
source you downloaded it from (the official GitHub Release), you can choose **More info → Run
anyway**. If in doubt, re-download from the official source and check the file.

---

## 12. Firewall prompt

The first time the app runs it tries to open its port so players on your **same network** can
reach you. Windows may show a firewall prompt. Allow BSV Poker on **private** networks (home /
work). This is only needed so other people on your LAN can connect to your tables — same-machine
play and outgoing connections work regardless. You can always change this later in Windows
Defender Firewall settings.

---

## 13. Troubleshooting installation

- **"Windows protected your PC" (SmartScreen):** see [section 11](#11-windows-smartscreen-and-antivirus-prompts).
- **The PowerShell script won't run:** make sure you ran it with `-ExecutionPolicy Bypass` as
  shown in [section 4](#4-method-2-the-powershell-installer-portable-zip--shortcuts), and that
  you are in the unzipped folder.
- **No Desktop/Start-Menu shortcut after the portable ZIP:** the portable ZIP run directly does
  not create shortcuts. Use Method 1 or Method 2 if you want them.
- **The app starts but the wallet is empty:** that is expected for a new wallet — fund it, or
  switch to Testnet/Regtest to practice. See [USER_GUIDE.md](USER_GUIDE.md), section 6.1.
- **Other problems:** see [TROUBLESHOOTING.md](TROUBLESHOOTING.md) and [FAQ.md](FAQ.md).
