# BSV Poker — Troubleshooting

Common problems and how to fix them, in plain language. Each entry lists the symptom, the likely
cause, and the steps to resolve it. If your issue isn't here, check the [FAQ.md](FAQ.md) and the
[USER_GUIDE.md](USER_GUIDE.md).

> A good first move for almost anything: **close the app completely and reopen it.** Closing the
> main window shuts everything down cleanly (it ends your tables, stops bots, and closes network
> connections), so a fresh start often clears transient issues.

---

## Table of contents

1. [The app won't start, or starts then closes](#1-the-app-wont-start-or-starts-then-closes)
2. [The wallet won't unlock / wrong password](#2-the-wallet-wont-unlock--wrong-password)
3. [My wallet shows zero / my coins are missing](#3-my-wallet-shows-zero--my-coins-are-missing)
4. [I can't fund / a payment isn't showing up](#4-i-cant-fund--a-payment-isnt-showing-up)
5. [A payment is stuck "unconfirmed"](#5-a-payment-is-stuck-unconfirmed)
6. [No players found / I can't see anyone](#6-no-players-found--i-cant-see-anyone)
7. [Other players can't see me (firewall)](#7-other-players-cant-see-me-firewall)
8. [A table won't start](#8-a-table-wont-start)
9. [The blackjack pot never becomes "ready"](#9-the-blackjack-pot-never-becomes-ready)
10. [I left a table but nothing happened](#10-i-left-a-table-but-nothing-happened)
11. [Broadcast failures / a move or send didn't go out](#11-broadcast-failures--a-move-or-send-didnt-go-out)
12. ["Publish my node" shows no transaction](#12-publish-my-node-shows-no-transaction)
13. [I can't chat / "set up your identity first"](#13-i-cant-chat--set-up-your-identity-first)
14. [The window is hard to read / contrast problems](#14-the-window-is-hard-to-read--contrast-problems)
15. [High CPU usage](#15-high-cpu-usage)
16. [Two copies behaving strangely](#16-two-copies-behaving-strangely)
17. [Collecting information before asking for help](#17-collecting-information-before-asking-for-help)

---

## 1. The app won't start, or starts then closes

- **SmartScreen/antivirus blocked it.** The first run of new software can be flagged. If you
  trust your download source (the official Release), choose **More info → Run anyway**, or allow
  it in your antivirus. See [INSTALL.md](INSTALL.md), section 11.
- **A previous copy is still running.** Check the Windows Task Manager for a `poker` process and
  end it, then relaunch.
- **The data folder is unwritable.** The app stores data under `%LOCALAPPDATA%\BsvPoker`. Make
  sure that location exists and isn't blocked by a policy or a full disk.

---

## 2. The wallet won't unlock / wrong password

- **Double-check the password.** It is case-sensitive. There is no reset email — the password is
  local encryption only.
- **You may be opening the wrong wallet.** The "Select your wallet" dialog lists every wallet the
  app has seen. Make sure you picked the right one before entering the password.
- **You genuinely forgot the password.** Recover from your **seed**: choose **Open (restore from
  seed)…**, type the seed, and set a new password. Without the seed, the wallet cannot be
  recovered — this is the trade-off of self-custody. (Always keep your seed written down; see
  [USER_GUIDE.md](USER_GUIDE.md), section 6.5.)

---

## 3. My wallet shows zero / my coins are missing

- **It may be a brand-new wallet** — those start empty by design. Fund it, or switch to
  Testnet/Regtest to practice.
- **You may be on the wrong network.** Coins on Testnet do not show on Mainnet, and vice versa.
  Check the network selector at the top. Your selection is remembered per profile; make sure it
  matches where the coins are.
- **Discovery hasn't caught up yet.** The app detects coins automatically (mempool + recent-block
  rescan), but give it a little time after connecting. If coins still don't appear, use **Rescan
  / Find my coins** to force a full scan; it works on mainnet by downloading and validating
  blocks.
- **You opened a different wallet/profile.** Each profile and each wallet file is separate.
  Confirm you opened the one that holds the coins.

---

## 4. I can't fund / a payment isn't showing up

- **Confirm the address and network.** You must send to the **receive address for the network you
  are on** (Mainnet/Testnet/Regtest). Sending mainnet coins to a testnet address (or vice versa)
  will not show up.
- **Wait for detection.** A just-sent payment first appears as **pending** when the app sees it
  in the mempool, then **confirmed** once mined and SPV-proven. This can take from seconds to a
  while depending on the network.
- **Force a rescan.** Use **Rescan / Find my coins**. This is especially useful if the payment
  confirmed before your app was connected.
- **Check you actually have peers.** SPV needs to be connected to the network to see anything.
  The status line shows SPV state; give it time to connect, and check your internet connection.

---

## 5. A payment is stuck "unconfirmed"

- **Unconfirmed simply means "received but not yet mined."** It is a valid coin; it just hasn't
  been included in a block yet. The app will mark it **confirmed** once it has an SPV proof.
  0-confirmation coins can still be used in play.
- **If it never confirms,** the original sender may have set a very low fee, or there may be a
  conflict. Check the **Coins (UTXOs)** sub-tab: a coin proven to conflict is shown as
  **DoubleSpent** (and is never silently deleted). If the sending side controls the payment, they
  may need to re-send.

---

## 6. No players found / I can't see anyone

The Lobby's status line shows how many nodes you're connected to and how many tables are visible,
so start there.

- **Give it a moment.** Same-machine peers connect instantly; same-network within a few seconds;
  internet peers via the on-chain directory take a little longer.
- **Are there actually players online?** If no one else is hosting a table, there is nothing to
  see. Try hosting one yourself, or use **Play a bot** / **Play MY bot** to play without others.
- **Same network but still nothing?** A firewall is the usual cause — see
  [section 7](#7-other-players-cant-see-me-firewall).
- **Across the internet?** For others to find you automatically over the internet, use **Publish
  my node on-chain** (see [section 12](#12-publish-my-node-shows-no-transaction)). As a fallback,
  one of you can use the Lobby's **Advanced: manually add an internet peer** box to dial the
  other's `host:port`.

---

## 7. Other players can't see me (firewall)

For people on your **same network** to reach your tables, your machine must accept the incoming
connection.

- **Allow the firewall prompt.** On first run the app tries to open its port and Windows may
  prompt — allow BSV Poker on **private** networks.
- **Add the rule manually if needed.** In Windows Defender Firewall, allow the BSV Poker app (and
  its port) for **private/domain** networks. (Adding the rule may need administrator rights; if
  it can't, Windows shows its own one-time allow prompt.)
- **Same-machine play is unaffected** — it uses loopback and never needs the firewall.

---

## 8. A table won't start

- **Not enough players have joined.** A table needs its configured number of seats to fill before
  the first hand deals. The status shows "Waiting for players… (n/N)".
- **Seat-order step in progress.** After the seats fill there's a brief "Agreeing a fair seat
  order…" step (commit-reveal). It completes on its own once every seat has committed and
  revealed.
- **A player dropped during seating.** If someone left mid-setup, the table re-derives the active
  set; it should recover. If it doesn't, everyone leaving and rejoining a fresh table is the
  clean reset.
- **The host left.** If the host walked away, their table ends. Host a new one or join another.

---

## 9. The blackjack pot never becomes "ready"

"Ready" means every stake is escrowed, miner-verified, and the refund is co-signed. If it never
gets there:

- **A stake failed the miner check (double-spend).** If a player's funding transaction was a
  double-spend, the miner rejects it and the pot won't become ready. This is the safety check
  working. **The game still plays** — it will simply settle on tracked bankroll rather than fully
  on-chain. The offending stake is flagged.
- **A player isn't funded.** If someone's wallet couldn't fund their stake, their pot coin never
  arrives. Again, play continues on bankroll.
- **This does not freeze the game.** The pot work is entirely in the background; an unready pot
  only affects whether the **final payout** is fully on-chain, never whether the cards deal.

If you want a fully on-chain settlement, make sure every player is funded with a clean (not
double-spent) stake before joining.

---

## 10. I left a table but nothing happened

- **You finish the current hand first.** Leaving takes effect at the **hand boundary** — you play
  out the hand in progress, then you're cashed out. The status will say "Leaving after this
  hand…".
- **A split or settlement may be running.** If 3+ players remain, leaving triggers an on-chain
  **split** (you're cashed out, the rest play on); if too few remain, a full **settlement**. You
  may see "co-signing the on-chain split…" or "settling the pot on-chain…". Let it complete.
- **Once it's done,** the **Leave table** button becomes **Close** — click it to close the
  window.

---

## 11. Broadcast failures / a move or send didn't go out

Every move and send is broadcast on a **redundant dual path** (directly to peers **and** to the
miners), so a single failing path usually doesn't matter. If something truly didn't land:

- **Check the Transactions list.** If the action is there, it was built and recorded; the app
  keeps re-sending where appropriate.
- **Check connectivity.** No internet or no peers means nothing can be broadcast. Wait for SPV to
  connect.
- **The wallet may be unfunded.** A move that needs a satoshi can't be built if the wallet has no
  spendable coin. Fund the wallet (see [section 4](#4-i-cant-fund--a-payment-isnt-showing-up)).
- **Retry the action** after connectivity returns; for a send, try again from Wallet → Send.

---

## 12. "Publish my node" shows no transaction

- **The wallet must be funded and unlocked.** Publishing spends ~3 satoshis. If the wallet is
  empty or locked, it can't build the transaction. Fund/unlock it first.
- **It lands via the SPV server plus the dual path.** When it works, the app reports the txid and
  the action appears in **Transactions**. If you saw nothing, check connectivity and that the
  wallet has spendable coin, then press the button again.
- **Remember it's opt-in.** Local and same-network discovery don't need this at all — only
  internet-wide automatic discovery does.

---

## 13. I can't chat / "set up your identity first"

Chat (and play) require a registered **identity**. Until you register, the only thing the wallet
can do is **receive** funds. Registration is offered **once**, the first time you try to play or
chat. To complete it you need a funded wallet (it writes a ~1-sat on-chain record). Fund the
wallet, then try the action again and accept the registration prompt.

---

## 14. The window is hard to read / contrast problems

The app uses a dark theme with light text throughout. If you ever hit an element that's hard to
read, close and reopen the app to re-apply the theme, and make sure you're on the current
version (older builds may have had contrast issues that newer ones fix). If a specific control is
unreadable, note exactly which one and report it.

---

## 15. High CPU usage

- **Initial header sync uses some CPU.** When the app first connects it downloads and validates
  block headers; this settles down once it's caught up.
- **A full rescan is heavy.** **Rescan / Find my coins** downloads and validates blocks — expect
  CPU use while it runs, then it stops.
- **Persistent high CPU** is not expected during normal idle play. If you see it, close and
  reopen the app; if it persists, report it with what you were doing at the time.

---

## 16. Two copies behaving strangely

- **Each copy is a separate player** with its own profile/wallet. That's by design. If you wanted
  them to be the **same** player, you opened two players by mistake — close one.
- **Profile slots are exclusive.** A copy holds a lock on its profile folder for its lifetime; a
  second copy takes the next slot. If a previous copy didn't close cleanly, end the leftover
  `poker` process in Task Manager before relaunching.

---

## 17. Collecting information before asking for help

If you need to report a problem, note:

- which **network** you're on (Mainnet/Testnet/Regtest),
- whether the wallet is **funded** and **unlocked**,
- what the **status line** in the Lobby/table says,
- whether the action shows up in **Transactions**,
- and the exact steps that led to the problem.

That makes it far easier to pin down. For backups before any drastic step, confirm your **seed**
is written down — it is the one thing that always lets you recover your wallet.
