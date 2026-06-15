# BSV Poker — User Guide

The complete, beginner-friendly manual for the BSV Poker desktop app for Windows.

BSV Poker is a **peer-to-peer, fully on-chain** poker and group blackjack client built on
Bitcoin SV (BSV). There is **no server, no website to log into, and no central operator**. Your
copy of the app talks directly to other players and to the Bitcoin network. Your money, your
keys, your identity, and your card data all live on **your own device** and on the **public
blockchain** — never on someone else's computer.

This guide walks you through everything: installing and starting the app, creating and funding a
wallet, every tab in the main window, finding and joining tables, playing poker, playing group
blackjack (including exactly what happens with the on-chain pot while you play), chatting,
reading your transaction history, publishing your node, and the settings you can change.

If you just want to install the app, see [INSTALL.md](INSTALL.md). If you want the rules and the
deeper on-chain mechanics of each game, see [GAMES.md](GAMES.md). If you get stuck, see
[TROUBLESHOOTING.md](TROUBLESHOOTING.md) and the [FAQ.md](FAQ.md). Unfamiliar word? The
[GLOSSARY.md](GLOSSARY.md) defines every term in plain English.

---

## Table of contents

1. [What BSV Poker is (and what it is not)](#1-what-bsv-poker-is-and-what-it-is-not)
2. [Before you start: a few key ideas](#2-before-you-start-a-few-key-ideas)
3. [Installing and launching](#3-installing-and-launching)
4. [First run: the startup sequence](#4-first-run-the-startup-sequence)
   1. [Choosing a profile](#41-choosing-a-profile)
   2. [Selecting or creating a wallet](#42-selecting-or-creating-a-wallet)
   3. [Setting your password](#43-setting-your-password)
   4. [The main window opens](#44-the-main-window-opens)
5. [The main window at a glance](#5-the-main-window-at-a-glance)
6. [The Wallet tab](#6-the-wallet-tab)
   1. [Funding your wallet](#61-funding-your-wallet)
   2. [Receiving (single-use addresses)](#62-receiving-single-use-addresses)
   3. [The Transactions tab (your live activity log)](#63-the-transactions-tab-your-live-activity-log)
   4. [Sending BSV](#64-sending-bsv)
   5. [Backing up your seed](#65-backing-up-your-seed)
   6. [Your identity](#66-your-identity)
   7. [Other wallet tabs](#67-other-wallet-tabs)
7. [The Lobby tab](#7-the-lobby-tab)
8. [Playing poker (the Game tab)](#8-playing-poker-the-game-tab)
9. [Playing group blackjack](#9-playing-group-blackjack)
   1. [Starting or joining a blackjack table](#91-starting-or-joining-a-blackjack-table)
   2. [What you see on screen](#92-what-you-see-on-screen)
   3. [The deal is instant — the pot funds in the background](#93-the-deal-is-instant--the-pot-funds-in-the-background)
   4. [Playing a hand](#94-playing-a-hand)
   5. [Busting](#95-busting)
   6. [Continuous play and the pause between hands](#96-continuous-play-and-the-pause-between-hands)
   7. [Leaving the table and cashing out](#97-leaving-the-table-and-cashing-out)
   8. [The leaver split](#98-the-leaver-split)
   9. [Bankrolls: yours and the house's](#99-bankrolls-yours-and-the-houses)
10. [The Chat tab](#10-the-chat-tab)
11. [Publishing your node on-chain](#11-publishing-your-node-on-chain)
12. [The Replay tab](#12-the-replay-tab)
13. [The Help tab](#13-the-help-tab)
14. [Playing against a bot](#14-playing-against-a-bot)
15. [Settings you can change](#15-settings-you-can-change)
16. [Closing the app safely](#16-closing-the-app-safely)
17. [Where your data lives](#17-where-your-data-lives)
18. [Safety and privacy in plain terms](#18-safety-and-privacy-in-plain-terms)

---

## 1. What BSV Poker is (and what it is not)

BSV Poker is a single Windows program (`poker.exe`) that lets you:

- Keep a real **BSV wallet** with real satoshis (the smallest unit of Bitcoin).
- Play **poker** against other people (2–6 players) or a practice bot, with **true card
  privacy** — nobody, not even the other players, can see your hole cards until showdown.
- Play **group blackjack** (2–6 seats) where the "dealer" is computed jointly by all the
  players (there is no house croupier) and the money is held in a shared on-chain pot.
- **Chat** with other players — to one person, to everyone, or to a named group — encrypted.

What it is **not**:

- It is **not** a casino website. There is no company holding your funds.
- It is **not** "play money." On mainnet, the satoshis are real money. (You can also use
  testnet "test coins," which are free and worthless, to practice — see
  [Settings](#15-settings-you-can-change).)
- It does **not** require you to install .NET, Java, or any runtime. The app is fully
  self-contained.

---

## 2. Before you start: a few key ideas

A handful of ideas will make everything below click. Each is defined more fully in the
[GLOSSARY.md](GLOSSARY.md).

- **A wallet is a keychain, not an account.** There is no username/password held on a server.
  Your wallet is backed by a single secret called a **seed**. Whoever has the seed has the
  money. Back it up (see [section 6.5](#65-backing-up-your-seed)).
- **Your password protects the keys on your disk** (encryption at rest). It is *not* a login to
  any service. If you forget it, only your seed backup can recover the wallet.
- **An address is single-use.** When someone pays you, you give them a fresh receive address.
  This is normal and good for privacy.
- **SPV** is how the app checks the blockchain without trusting any server: it downloads and
  verifies block headers itself and accepts a payment only with a cryptographic proof that the
  payment was mined. You don't have to do anything — it just works in the background.
- **Every action is a Bitcoin transaction.** Sending money, sending a chat message, making a
  bet, dealing a hand, registering your identity — each becomes a tiny on-chain transaction
  (often a single satoshi plus a miner fee). They all appear in your **Transactions** list.
- **A second copy of the app is a different player.** Each running instance grabs its own
  profile (its own wallet and identity). This is how you can test two players on one machine.

---

## 3. Installing and launching

Full install instructions are in [INSTALL.md](INSTALL.md). In short, you have three options:

1. **Setup.exe** — double-click it, click through, done. It creates Start-Menu and Desktop
   shortcuts and an entry in **Settings → Apps**.
2. **The PowerShell installer** (`Install-BsvPoker.ps1`) — for the portable ZIP, this copies the
   app into place and makes the same shortcuts.
3. **The portable ZIP** — unzip anywhere and run `poker.exe` directly. Nothing to install.

All methods are **per-user** (no administrator rights needed) and install into
`%LOCALAPPDATA%\Programs\BSV Poker`. Your wallet and profile data are stored separately and are
**preserved across uninstall** (see [section 17](#17-where-your-data-lives)).

To launch, use the Start-Menu or Desktop shortcut, or double-click `poker.exe`.

---

## 4. First run: the startup sequence

The very first time you start the app, it walks you through a short, **sequential** sign-in.
These dialogs appear one after another — never on top of each other, never with the main window
hidden behind them.

### 4.1 Choosing a profile

Each running copy of the app uses its own **profile**, and each profile is a completely separate
player with its own wallet and identity. The first copy you run takes the first profile slot
("Player 1"); a second copy taken at the same time takes the next slot ("Player 2"), and so on.

You normally do not have to think about this — just run the app once and it picks the first free
slot. The reason it works this way is so that you *can* run two copies side by side (for example
to play yourself for testing, or to run a friend's seat on the same machine), and each will be a
genuinely separate player.

### 4.2 Selecting or creating a wallet

Next you see the **"Select your wallet"** dialog. This is always the very first thing on screen.
It lists every wallet the app has seen before (across all profiles), so you can pick the one you
want to open. A wallet is **never** opened automatically, even if you only have one — you always
choose, and only then are you asked for the password.

- If this is a brand-new install, there is nothing to select yet, so you create a wallet. The
  app then runs a short setup wizard (modelled on the popular ElectrumSV wallet): it sets a
  **password** first, then shows you your **seed** to write down, then asks you to confirm it.
- If you already have a wallet, select it from the list and continue.

> The seed is the master backup of your money. Write it down on paper and keep it somewhere
> safe. See [section 6.5](#65-backing-up-your-seed).

### 4.3 Setting your password

For a new wallet you choose a password. This encrypts your keys on disk (AES-256-GCM). For an
existing wallet you enter the password you set before.

Remember: the password is **local encryption only**. There is no "forgot password" email — your
seed backup is the only recovery path.

### 4.4 The main window opens

Once the wallet is open and unlocked, the main window — titled **"BSV Poker"** — appears, with
your wallet ready. In the background the app quietly:

- connects to the Bitcoin network and starts downloading block headers (for SPV),
- starts listening for transactions other players push directly to you,
- brings up your peer-to-peer node so you can host or join tables,
- begins discovering other players automatically (same machine instantly, same network within a
  few seconds, and across the internet via the on-chain directory).

You don't have to wait for any of this — you can start using the Wallet tab right away.

---

## 5. The main window at a glance

The main window has a **network selector** and a **status line** at the top, and six tabs:

| Tab | What it is for |
|-----|----------------|
| **Wallet** | Your BSV wallet: balance, receive address, transactions, sending, identity, backup, and more. |
| **Lobby** | Find and join tables; host poker or group blackjack; play a bot; publish your node. |
| **Game** | The poker table where hands are played. |
| **Replay** | Step through a finished, fully on-chain hand move by move. |
| **Chat** | Encrypted messaging — one person, everyone, or a named group. |
| **Help** | Plain-language how-to-play for someone new to poker. |

The status line near the top shows your name/handle, the current network (Mainnet by default),
that SPV is online, and how many other players have been discovered. The network selector lets
you switch between **Mainnet** (real BSV), **Testnet** (free test coins), and **Regtest** (a
local test chain). See [Settings](#15-settings-you-can-change).

---

## 6. The Wallet tab

The Wallet tab is a full BSV wallet, organised into its own row of sub-tabs (modelled on
ElectrumSV but built well beyond it):

| Sub-tab | What it shows |
|---------|---------------|
| **History** | Your confirmed on-chain history with running balance. |
| **Transactions** | A **live log of every action** the wallet has taken (chat, bets, deals, identity, sends, receives), shown immediately — before they confirm. |
| **Send** | Build and broadcast a payment. |
| **Receive** | Your current single-use receive address (with QR) and payment requests. |
| **Notifications** | Wallet notifications. |
| **Destinations** | The addresses your wallet has used. |
| **Coins (UTXOs)** | The individual coins your wallet holds, with their confirmation state. |
| **Contacts** | Your address book (handles → identity keys). |
| **Vaults** | 2-of-2 multisig vaults with time-locked recovery. |
| **Identity** | Your on-chain identity (pseudonym/handle and details). |
| **NFTs** | Cards and other items you hold as NFTs. |
| **Console** / **Agent** | Advanced tools. |

The big balance number at the top is in satoshis and comes entirely from SPV — the app derives
your balance from coins it has verified against the blockchain itself, not from any server's
say-so.

### 6.1 Funding your wallet

A new wallet starts **empty** — there is no play money. To play for real on mainnet you need to
put real satoshis in.

1. Go to **Wallet → Receive**.
2. Copy your receive address (or show the QR code).
3. Send BSV to that address from wherever you hold it (an exchange, another wallet, a friend).
4. Wait. The app discovers the incoming payment automatically: it watches the network's mempool
   for the instant the payment is sent, and rescans recent blocks for a payment that confirmed
   before you connected. When the payment is detected it appears as **pending**, and once it is
   mined and SPV-proven it becomes **confirmed** and adds to your spendable balance.

> You do not need to press any "refresh" button — discovery runs automatically. There is also a
> **Rescan / Find my coins** action if you ever want to force a full re-scan.

Practising for free: switch the network selector to **Testnet** and fund the testnet address
from a public testnet faucet, or to **Regtest** which can self-fund on your own local chain.

### 6.2 Receiving (single-use addresses)

Each receive address is meant to be used **once**. After you receive to an address, generate a
fresh one (the wallet does this for you, and there's a menu action **New receiving address**).
Using a fresh address per payment is normal and protects your privacy — it makes it harder for
an outside observer to link your payments together.

### 6.3 The Transactions tab (your live activity log)

This is one of the most important and reassuring parts of the app. **Everything the app does
on-chain shows up here, live.** When you send a chat message, make a bet, take part in a deal,
register your identity, fund a pot, send a payment, or receive one — a row appears immediately,
before it has even confirmed. Each row shows the time, the type of action, the amount, and a
memo. Confirmed history (with running balance) is on the **History** sub-tab.

The point: there are no hidden moves. If money or data went on-chain on your behalf, you can see
it.

### 6.4 Sending BSV

1. Go to **Wallet → Send**.
2. In the "pay to" box, enter a destination. This can be a normal BSV address, an identity
   **@handle**, an identity public key, or a `bitcoin:`/`pay:` URI.
3. Enter the amount and (optionally) a label and a fee rate.
4. Send. The app builds a real signed transaction (secp256k1, low-S, with the BSV FORKID
   sighash) and broadcasts it. It appears in your **Transactions** immediately.

### 6.5 Backing up your seed

Your **seed** is the single master backup of your entire wallet. With it you can restore your
money on any machine; without it, a lost device or a forgotten password can mean lost funds.

- To view it: the wallet menu has **Show seed backup…** and **Save a copy of the seed backup…**.
- Write the seed down on paper. Store it somewhere safe and private. Do **not** photograph it,
  email it, or store it in the cloud.
- To restore a wallet elsewhere: use **Open (restore from seed)…** and type the seed in.

There are also menu actions to save a copy of the whole wallet file, export a private key (WIF),
and sign/verify messages.

### 6.6 Your identity

To **play** (poker or blackjack) and to **chat**, your wallet needs an **identity** — a fixed
pseudonym (a @handle) that other players see instead of a raw key. The identity is self-signed
and can also be written **on-chain** as an NFT-like record so it is permanent and verifiable.

You do not set this up from a checkbox. The app asks you to register your identity **once**, the
first time you try to do something that needs it (for example, joining a table). After that it
never asks again — you just play. Registering writes a tiny (about 1 satoshi) on-chain identity
record, so your wallet needs to be funded first.

Until you register, the only thing the wallet can do is **receive** funds.

### 6.7 Other wallet tabs

- **Coins (UTXOs):** the individual coins you hold. Each shows whether it is confirmed,
  unconfirmed, or has been double-spent. Coins are never silently deleted; their state is always
  derived from a saved, re-verifiable proof.
- **Destinations:** the addresses your wallet has used.
- **Contacts:** your address book of handles and identity keys. You can save a player you meet
  in chat into your contacts.
- **Vaults:** advanced 2-of-2 multisig storage with a time-locked recovery path.
- **NFTs:** cards and items you hold.

---

## 7. The Lobby tab

The Lobby is where you find people to play with. It is fully peer-to-peer — there is no server
listing tables; your app sees other players' tables directly over the network.

What you see, top to bottom:

- **A live status line** ("Your node is LIVE…") that tells you players are found automatically:
  same machine instantly, same network within a few seconds, and across the internet via the
  on-chain directory. You **never** type an IP address or port for normal play.
- **Play now** buttons:
  - **Play a bot** — start a practice hand against a simple bot, using the game variant selected
    below.
  - **Play MY bot (on-chain)** — open your own bot (a separate player derived from your
    identity) and play a real on-chain hand against it. See [section 14](#14-playing-against-a-bot).
  - **Host Blackjack (group)** — host a multiplayer blackjack table with the chosen number of
    seats. See [section 9](#9-playing-group-blackjack).
- **Host table** — create a poker table: pick a game name, a variant, the number of players
  (2–6), the buy-in (starting chips), and the blind. Click **Create**. You are seated
  automatically and jumped to the Game tab — a host is also a player, so the hand starts as soon
  as the other seats fill.
- **Publish my node on-chain** — an explicit button that advertises your node's address to the
  on-chain directory so people across the internet can find you. See
  [section 11](#11-publishing-your-node-on-chain).
- **Open tables** — a list of every table currently visible on the network. **Double-click** a
  table to join it (or select it and click **Join selected**). The list shows the table name,
  the game type, and how many players are seated.
- **Advanced: manually add an internet peer (optional)** — a tucked-away box where you can type
  a `host:port` to dial a specific peer directly. You only need this for an unusual case such as
  a peer behind a restrictive firewall. Normal play needs nothing here.

The status line below the table list also shows how many nodes you are connected to right now and
how many open tables are visible, so "I can't see anyone" always has a concrete number behind it.

---

## 8. Playing poker (the Game tab)

The Game tab is the poker table — a green felt with cards, a pot, action buttons, and a
plain-language result banner.

How a networked hand works:

1. **Join or host a table** from the Lobby. When enough players are seated, the hand begins.
2. **Seat order is decided fairly** by a commit-reveal process between the players, so nobody can
   manipulate where they sit.
3. **The deck is shuffled by everyone, encrypted.** No single player deals. Through the
   mental-poker shuffle, the deck is jointly shuffled and encrypted such that **you see only your
   own hole cards** — the other players' hidden cards are mathematically out of everyone's reach
   until showdown.
4. **You act on your turn.** Your buttons — **Fold**, **Check**, **Call**, **Bet / Raise** (with
   an amount box) — are enabled only when it is your turn. There is also a per-hand countdown
   clock shown on the table.
5. **Every move you make becomes a real on-chain transaction** (a tiny typed "bet" output funded
   from your wallet), broadcast both directly to the other players and to the miners. You will
   see each move appear in your **Transactions** list.
6. **Showdown and result.** When the hand finishes, the result banner shows clearly whether you
   won or lost and by how much. The engine handles blinds, all-in side pots, and conserves chips
   exactly.

The app supports six poker variants (selectable when you host a table). For the detailed rules
and on-chain flow, see [GAMES.md](GAMES.md). For a from-scratch explanation of poker itself, open
the **Help** tab.

There is also a **Practice deal** button for a local hot-seat practice hand (both hands visible
on one screen) and a **Leave table** button. An **On-chain settle** action appears when the
wallet/node are available.

---

## 9. Playing group blackjack

Group blackjack is one of the most distinctive features of BSV Poker, so this section is
detailed. The deeper on-chain mechanics are in [GAMES.md](GAMES.md); here is the player's-eye
walkthrough.

**Important up front:** group blackjack is **never one-on-one against a house**. You play at a
table with **2 to 6 players**, and the "dealer" is **computed jointly between all the players**
using the same mental-poker technique as the poker game. There is no croupier and no operator —
the dealer's cards come out of the shared, jointly-shuffled deck, and its hole card stays sealed
(unreadable by anyone) until every player has finished, then it plays to 17 like a normal
blackjack dealer.

### 9.1 Starting or joining a blackjack table

- **To host:** in the Lobby, set the **players** count (2–6) and click **Host Blackjack
  (group)**. Your table is announced so others can join, and the live table window opens for you.
- **To join:** find a blackjack table in the Lobby's open-tables list (it is labelled "Blackjack
  (group)") and double-click it.

A blackjack table needs at least the seat count it was created with to fill before the first hand
deals. While it waits you will see "Waiting for players… (n/N)" and then a brief "Agreeing a fair
seat order…" step.

### 9.2 What you see on screen

The **Group Blackjack** window shows:

- **Players at the table** — the @handles of everyone seated, with "(you)" next to yours.
- **Dealer (shared)** — the dealer's cards. While players are still acting, the dealer's hole
  card is shown face-down.
- **Your hand** — your cards and your running total.
- A line showing the **hand number**, **your bankroll**, and the **house** bankroll.
- **Hit**, **Stand**, **Double** buttons (enabled only on your turn) and a **Leave table**
  button.
- A status line describing what's happening ("Hand #3 — you are seat 1…", "Dealer plays…",
  "Settling the pot on-chain…", and so on).

### 9.3 The deal is instant — the pot funds in the background

This is the key thing to understand, and it is intentional:

> **The table deals instantly (sub-second). The on-chain money pot is secured in the background
> and never blocks the cards.**

When the seats fill and the seat order is agreed, the cards are dealt right away — you start
playing immediately. Meanwhile, **in parallel**, the app:

1. **Escrows your stake** into a single shared **n-of-n pot** (one on-chain coin from each
   player). Your stake is your buy-in plus an equal share of the house bankroll.
2. **Asks the miners to confirm** every player's stake. A stake counts only if a miner accepts
   its funding transaction (BSV "first-seen"). If someone tried to double-spend their stake, the
   miner rejects it and the pot simply never reaches "ready" — but **the game keeps playing**.
3. **Co-signs a refund** — a pre-signed, time-locked safety transaction (see below).

You may see the status mention "confirming stakes" or similar while this happens. That is normal:
the pot is real and is being secured behind the scenes while you play.

For the full lifecycle (fund → play → settle/split → refund) see [GAMES.md](GAMES.md).

### 9.4 Playing a hand

On your turn, the **Hit**, **Stand**, and **Double** buttons light up:

- **Hit** — take another card.
- **Stand** — keep your hand and end your turn.
- **Double** — double your bet, take exactly one more card, and end your turn (only on your first
  two cards).

When you Hit or Double, the new card has to be revealed by **all** the players (that is how the
mental-poker deck works), so it appears a moment after you click. While that card is arriving you
cannot act again — this is deliberate, so you can never "blow past" a bust by clicking fast.

After every player has acted, the dealer's hole card is revealed and the dealer plays to 17.
Results are then computed identically on every player's machine. A natural blackjack pays 3:2.

### 9.5 Busting

If your hand total goes over 21, you **bust**: your turn ends immediately and you cannot act any
further. The app enforces this — once you are over 21, the buttons are disabled. A bust loses
your bet for that hand.

### 9.6 Continuous play and the pause between hands

A real table does not stop after one hand. Group blackjack plays **continuously, hand after
hand**, with a short pause (about **10 seconds**) after each hand so you can read who won and
lost. Then the next hand deals automatically. This continues until you (or enough other players)
leave, or until the house bankroll can no longer cover the table.

### 9.7 Leaving the table and cashing out

Click **Leave table** to cash out.

- You **finish the hand currently in progress** — your bet stands; you cannot pull a bet out of a
  hand that is already dealt (that is the real-table rule and it protects everyone).
- Then you are dealt out and your **bankroll is cashed out**.
- The remaining players keep playing.

Once the session is over for you, the **Leave table** button becomes **Close**.

### 9.8 The leaver split

If you leave a table that still has **3 or more players**, and the on-chain pot has finished
securing, the pot does not just close — it **splits on-chain**:

- You (the leaver) are **cashed out** on-chain to your final standing.
- The remaining money is **re-escrowed** into a new pot for the players who stay.
- The remaining players keep playing on the new pot.

All of this is co-signed by the current players and computed identically on every machine, so
nobody can cheat the split. If only one player would remain, the table instead ends and everyone
settles.

### 9.9 Bankrolls: yours and the house's

Every hand the window shows **your bankroll** and the **house** (dealer) bankroll, both in real
satoshis:

- **Your bankroll** is the cash you currently hold at the table — your buy-in plus or minus every
  hand's result. It is what you walk away with when you leave.
- **The house bankroll** is the dealer's stake. It wins what players lose and pays what players
  win. If it can no longer cover the table, the table closes (a real-money game cannot run a
  house that cannot pay).

When the session ends, all the pot coins are spent in **one** on-chain settlement transaction
that pays everyone their final standing, co-signed by every player, conserved to the satoshi.

---

## 10. The Chat tab

The Chat tab is fully encrypted, peer-to-peer messaging. Every message is itself a Bitcoin
transaction — the wire only ever carries ciphertext.

You can send to:

- **One person** — pick a recipient (from your contacts or the live "who's online" directory).
- **Everyone** — a broadcast that is *encrypted* to every known recipient (it is never sent as
  plaintext to all).
- **A named group** — create and manage groups; only the selected members can read the message.

Key points:

- Each direct message uses a fresh ephemeral key per recipient, so keys are never reused.
- Messaging works even when the recipient is **offline**: the message lands on the recipient's
  identity address on-chain (store-and-forward), and they receive it the next time they sync. You
  do nothing extra — if they're online it's instant, if they're offline it waits for them.
- Chat history persists across restarts (and because it lives on-chain encrypted to you, you can
  even recover your own group history after signing in again on another device).
- There is a **who's online** directory of people reachable right now, an address book, and a
  one-click "add a bot" identity you can put in chats or groups.

You must register your identity (see [section 6.6](#66-your-identity)) before you can send chat.

---

## 11. Publishing your node on-chain

For local and same-network players, discovery is fully automatic — you never do anything. For
players **across the internet** to find you automatically, you can publish your node's address to
the on-chain directory.

- In the **Lobby**, click **Publish my node on-chain**.
- This is an **explicit, opt-in** action that spends about **3 satoshis** (a record plus dust
  plus a fee). The app **never** auto-spends your coins for discovery — reading the directory is
  free, but publishing is something you choose to do.
- After you click, the app tells you the **txid** of the publishing transaction, the address you
  published, and the registry it went to. The transaction appears in your **Transactions** list
  immediately and confirms shortly. From then on, players anywhere can discover and connect to
  you.
- A published node stays listed for a time-to-live window; press the button again to refresh it
  before it expires.

If you press it and see no transaction, see [TROUBLESHOOTING.md](TROUBLESHOOTING.md).

---

## 12. The Replay tab

Because every move of a hand is on-chain, a finished hand can be loaded back and **stepped
through move by move**. The Replay tab fetches a real game off the blockchain by its start
address or a transaction id and lets you walk through the actual recorded hand. For example,
after you play a fully on-chain hand against your own bot, the resulting hand "tape" is loaded
here so you can review it.

---

## 13. The Help tab

The Help tab is a plain-language how-to-play for someone who has never played poker: the rules,
what each button does, and how you win or lose. If poker is new to you, start there.

---

## 14. Playing against a bot

Two bot options live in the Lobby:

- **Play a bot** — a quick practice hand against a simple automated opponent in the chosen
  variant. Good for learning the controls.
- **Play MY bot (on-chain)** — opens **your own** bot in its own small window. The bot is a
  genuinely separate player **derived from your identity**, and it only ever plays you. You fund
  it from your own wallet with one click (it refunds back to you when its window closes), and you
  then play a real, secure, dealerless mental-poker hand against it — the identical protocol as
  any networked table. A fully on-chain version of the hand is also recorded in the background and
  loaded into the **Replay** tab so you can step through it. This is the best way to see the real
  on-chain machinery end to end, by yourself.

You can also add a bot as a **chat identity** (no game) with one click from the Chat tab.

---

## 15. Settings you can change

BSV Poker keeps the interface deliberately simple. The main things you can change:

- **Network** — the selector at the top of the main window switches between **Mainnet** (real
  BSV), **Testnet** (free test coins for practice), and **Regtest** (your own local chain that
  can self-fund). Your choice is remembered across restarts, and the same seed works on every
  network. Use Testnet or Regtest to practice without risking real money.
- **Wallet password** — set or change it from the wallet menu (**Password (encrypt keys)…**).
- **New receiving address** — from the account menu, whenever you want a fresh address.
- **Accounts** — you can add or switch accounts (each with its own seed) from the wallet menus.
- **Per-hand timing** — the poker table shows a per-hand countdown; group blackjack pauses about
  10 seconds between hands. These are built-in behaviours of the game.
- **Contacts and groups** — manage your address book and chat groups from the Wallet and Chat
  tabs.
- **Firewall** — on first run the app tries to open its port for same-network play; if Windows
  prompts you, allow it on **private** networks.

---

## 16. Closing the app safely

Just close the main window. On close, the app:

- backs up your card vault,
- ends any table you were hosting (so it does not linger as a ghost table),
- stops any bots you had running and returns their funds to you,
- and shuts down its network connections cleanly, leaving no orphan processes.

There is no "log out" — closing the window is the safe, complete way to stop.

---

## 17. Where your data lives

- **The program** is installed in `%LOCALAPPDATA%\Programs\BSV Poker` (or wherever you unzipped
  the portable build).
- **Your wallet and profile data** are stored separately under
  `%LOCALAPPDATA%\BsvPoker\profiles\` — one folder per profile (`p1`, `p2`, …). This holds your
  wallet file, your identity, your block headers, your chat history, and your card vault.

Because the data is separate from the program, **uninstalling the app does not delete your wallet
or your money.** It also means you should keep your **seed** backed up independently — the seed is
the ultimate backup, not the folder.

(You can paste `%LOCALAPPDATA%\BsvPoker\profiles` into the Windows Explorer address bar to see it.)

---

## 18. Safety and privacy in plain terms

- **Your cards are private.** Through mental-poker commutative encryption, the deck is shuffled
  jointly and encrypted so no single player controls it and no one can see anyone else's hidden
  cards until showdown.
- **Your money needs your signature to move.** The shared pots are **n-of-n** — every player must
  sign for the money to move — and you hold a **pre-signed time-locked refund** before you ever
  risk a satoshi. If someone ever refuses to co-sign a payout, the refund returns every stake
  after the timeout. Nobody can run off with the pot.
- **Stakes are checked with the miners.** A player's stake only counts if a miner accepts it, so
  a faked or double-spent stake is caught.
- **You verify the chain yourself.** SPV means the app checks payments against block headers it
  validated itself — it does not trust any server.
- **Your keys never leave your device.** Your seed, your private keys, and your identity key stay
  on your machine, encrypted at rest with your password.
- **Your identity is a pseudonym.** Other players see your @handle, not a raw key, and it is an
  on-chain, verifiable record.

For the formal threat model and security properties, see [SECURITY.md](SECURITY.md). For deeper
mechanics, see [ONCHAIN_MODEL.md](ONCHAIN_MODEL.md), [MENTAL_POKER.md](MENTAL_POKER.md), and
[GAMES.md](GAMES.md).
