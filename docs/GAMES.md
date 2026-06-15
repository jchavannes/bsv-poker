# BSV Poker — Games: Rules and On-Chain Mechanics

This document explains the games in BSV Poker — the rules you play by, and what happens on the
blockchain underneath. It is written to be readable by someone who has never played before, while
also being precise about the on-chain machinery so you understand exactly where your money is at
every step.

There are two kinds of game:

- **Poker** — networked multiplayer (2–6 players) with true hidden-card privacy, plus a practice
  mode and a play-your-own-bot mode. Six variants are supported.
- **Group blackjack** — networked multiplayer (2–6 seats) with a jointly-computed dealer and a
  shared on-chain pot. Never one-on-one against a house.

For where the buttons are and how to start a table, see [USER_GUIDE.md](USER_GUIDE.md). For
definitions of any term, see [GLOSSARY.md](GLOSSARY.md).

---

## Table of contents

1. [Concepts shared by both games](#1-concepts-shared-by-both-games)
   1. [The dealerless, encrypted deck (mental poker)](#11-the-dealerless-encrypted-deck-mental-poker)
   2. [Fair seat order](#12-fair-seat-order)
   3. [Every move is a transaction](#13-every-move-is-a-transaction)
   4. [The n-of-n pot](#14-the-n-of-n-pot)
   5. [The pre-signed refund (your safety net)](#15-the-pre-signed-refund-your-safety-net)
   6. [Asking the miner (the double-spend check)](#16-asking-the-miner-the-double-spend-check)
2. [Poker](#2-poker)
   1. [The flow of a hand](#21-the-flow-of-a-hand)
   2. [The actions](#22-the-actions)
   3. [Privacy of your cards](#23-privacy-of-your-cards)
   4. [On-chain poker moves](#24-on-chain-poker-moves)
   5. [Variants](#25-variants)
   6. [Practice and bot modes](#26-practice-and-bot-modes)
3. [Group blackjack — the rules](#3-group-blackjack--the-rules)
   1. [The goal](#31-the-goal)
   2. [Seats and the shared dealer](#32-seats-and-the-shared-dealer)
   3. [Card values](#33-card-values)
   4. [Your turn: hit, stand, double](#34-your-turn-hit-stand-double)
   5. [Busting](#35-busting)
   6. [The dealer plays](#36-the-dealer-plays)
   7. [Payouts](#37-payouts)
   8. [Continuous play](#38-continuous-play)
4. [Group blackjack — the on-chain pot lifecycle](#4-group-blackjack--the-on-chain-pot-lifecycle)
   1. [Overview: instant deal, background money](#41-overview-instant-deal-background-money)
   2. [Stage 1 — Funding (escrowing the stakes)](#42-stage-1--funding-escrowing-the-stakes)
   3. [Stage 2 — Miner verification (first-seen)](#43-stage-2--miner-verification-first-seen)
   4. [Stage 3 — Co-signing the refund](#44-stage-3--co-signing-the-refund)
   5. [Stage 4 — Playing](#45-stage-4--playing)
   6. [Stage 5 — Settlement (end of session)](#46-stage-5--settlement-end-of-session)
   7. [Stage 6 — The leaver split](#47-stage-6--the-leaver-split)
   8. [The dealer/house bankroll](#48-the-dealerhouse-bankroll)
   9. [What happens if something goes wrong](#49-what-happens-if-something-goes-wrong)
5. [Frequently misunderstood points](#5-frequently-misunderstood-points)

---

## 1. Concepts shared by both games

Both games are built on the same handful of building blocks. Understanding these once makes both
games clear.

### 1.1 The dealerless, encrypted deck (mental poker)

There is **no dealer** in BSV Poker — no operator, no house croupier, not even one of the players
acting as the dealer. Instead the deck is shuffled and encrypted **jointly by all the players**
using a technique called *mental poker*.

In plain terms: each player applies their own secret shuffle-and-lock to the deck in turn. After
everyone has done so, the deck is thoroughly shuffled and every card is locked with a combination
of secrets that **no single player holds**. A specific card can be opened only when the players
who need to reveal it each contribute their part. That is how a card stays hidden from everyone
until the moment it should be shown:

- In **poker**, your hole cards are unlocked only for you; the others stay locked for everyone
  until showdown.
- In **blackjack**, the cards are face-up (it's a face-up game) — *except* the dealer's hole
  card, which stays locked and unreadable by anyone until every player has finished and the
  dealer plays.

Crucially, when a card is revealed, the contribution that opens it comes with a cryptographic
**commitment check**, so a player cannot lie about their part to change which card appears. If
someone tries, it is detected.

### 1.2 Fair seat order

Before a table starts, the players agree a seat order using a **commit-reveal** process: everyone
first commits to a secret random value, then everyone reveals it, and the combined randomness
fixes the seating. Because each player commits before anyone reveals, no one can choose a value
to engineer a favourable seat. This is the same anti-grinding seating used in both games.

### 1.3 Every move is a transaction

BSV Poker is "fully on-chain": essentially every action becomes a real Bitcoin transaction,
typically funded with about a satoshi plus a tiny miner fee. Your bets, your card actions, the
deal record, identity registration, chat messages, and pot funding/settlement are all
transactions. Each is broadcast on a **redundant dual path** — sent directly to the other players
(IP-to-IP) **and** to the miners — so a move cannot be quietly raced by a double-spend, and so
the action lands even if one path is having a bad moment.

You can watch all of this in the wallet's **Transactions** list.

### 1.4 The n-of-n pot

When real money is at stake (group blackjack), every player's stake goes into **one shared pot**
locked by an **n-of-n** rule: there are *n* players, and **all n** must sign for the money to
move. No subset — and certainly no single person — can spend the pot. The only ways the money
moves are:

- the **settlement** (everyone co-signs the final payout), or
- a **leaver split** (the current players co-sign cashing out a leaver and re-escrowing the
  rest), or
- the **refund** (a pre-signed safety transaction; see next).

### 1.5 The pre-signed refund (your safety net)

Before any card is dealt for real money, the players co-sign a **refund** transaction up front.
It is a time-locked (nLockTime) transaction — it cannot be used until roughly **30 days** in the
future — that simply returns every player's stake. It sits unused as long as everyone cooperates.

Its purpose is griefing protection: if, at settlement time, a player refuses to co-sign the
payout (or signs garbage, or disappears), the others wait a short grace period and then broadcast
the pre-signed refund. After the time-lock, **every stake comes back**. So the worst a malicious
or absent player can do is force everyone (including you) back to their original stake after a
delay — they can never steal the pot.

### 1.6 Asking the miner (the double-spend check)

A player could *claim* to have funded their stake while secretly trying to spend the same coins
elsewhere (a double-spend). To prevent a fake stake from counting, the app **asks the miner**:
each player's funding transaction counts only if a miner **accepts** it under BSV's *first-seen*
rule. If a conflicting transaction is already at the miner, the funding is rejected.

Importantly, this check runs in the **background** and never freezes the game. If a stake is
caught as a double-spend, the pot simply does not reach "ready" — which only means the
end-of-session payout cannot go fully on-chain — but the cards keep playing throughout.

---

## 2. Poker

### 2.1 The flow of a hand

1. **Seating.** Players join a table from the Lobby; seat order is fixed fairly (see
   [1.2](#12-fair-seat-order)).
2. **The deal.** The deck is jointly shuffled and encrypted; each player can open only their own
   hole cards.
3. **Betting rounds.** Players act in turn — folding, checking, calling, or raising — across the
   usual streets, with blinds posted as configured for the table.
4. **The board** (community cards, for Hold'em-style variants) is revealed as the hand
   progresses.
5. **Showdown.** Remaining players' cards are revealed and the best hand wins. The engine handles
   all-in side pots and conserves chips exactly.
6. **Result.** A plain-language banner shows whether you won or lost and by how much.

### 2.2 The actions

On your turn the table enables only the legal actions:

- **Fold** — give up the hand.
- **Check** — pass the action with no bet (only when nothing is owed).
- **Call** — match the current bet.
- **Bet / Raise** — put in chips; type the amount in the box next to the button.

A per-hand countdown clock is shown on the table.

### 2.3 Privacy of your cards

Through the mental-poker deal, you see **only your own hole cards** until showdown. The other
players' hidden cards are locked by secrets no one fully holds, so they cannot be read early — not
by another player, and not by any outside observer of the network. This is real cryptographic
privacy, not a UI trick.

### 2.4 On-chain poker moves

Every move you make is turned into a real on-chain transaction — a small typed "bet" output
funded from your wallet (about a satoshi) — and broadcast on the redundant dual path. Card
actions such as discard-and-draw charge a small on-chain fee too. Because every move is recorded
on-chain, a finished hand can later be loaded into the **Replay** tab and stepped through move by
move.

### 2.5 Variants

The app supports six poker variants, chosen when you host a table (the default is Texas
Hold'em). The variant you pick also determines the game your bot deals.

### 2.6 Practice and bot modes

- **Practice deal** — a local hot-seat hand with both sides visible, for learning the controls.
- **Play a bot** — a quick networked-style hand against a simple automated opponent.
- **Play MY bot (on-chain)** — your own bot (a separate player derived from your identity) that
  only ever plays you, for a real, secure, dealerless hand end to end. See
  [USER_GUIDE.md](USER_GUIDE.md), section 14.

---

## 3. Group blackjack — the rules

### 3.1 The goal

Blackjack's goal is to get a hand total closer to **21** than the dealer, without going **over**
21. Going over 21 is a **bust** and loses immediately.

### 3.2 Seats and the shared dealer

A group blackjack table has **2 to 6 seats**. There is **no house dealer person** — the "dealer"
is **computed jointly between all the players** from the same shared, jointly-shuffled,
encrypted deck used for the players' cards. The dealer's up-card is visible to everyone; the
dealer's **hole card is sealed** (unreadable by anyone) until every player has finished, exactly
as in a normal blackjack game. This is what makes it fair without a trusted operator.

You always play at a table **with** other players — never one-on-one against a house.

### 3.3 Card values

- Number cards (2–10) are worth their face value.
- Face cards (J, Q, K) are worth 10.
- An Ace is worth 11 or 1, whichever helps your hand more.
- A two-card 21 (an Ace plus a ten-value card) is a **natural blackjack**.

### 3.4 Your turn: hit, stand, double

When it is your turn, three buttons become available:

- **Hit** — take another card.
- **Stand** — keep your current hand and end your turn.
- **Double** — double your bet, take exactly **one** more card, then end your turn. Available only
  on your first two cards.

When you Hit or Double, the new card must be revealed by **all** players (because of the
mental-poker deck), so it appears a moment after you click. Until that card has actually appeared
on your screen you **cannot act again** — this is deliberate, so you can never click past a bust.

### 3.5 Busting

If your total goes over 21 you **bust**: your turn ends at once and you cannot act again that
hand. The app disables your buttons the instant you are over 21. A bust loses your bet for that
hand.

### 3.6 The dealer plays

After every player has finished (stood, doubled, or busted), the dealer's hole card is revealed
and the dealer draws until reaching at least **17**, then stops — the standard dealer rule.

### 3.7 Payouts

For each player, compared against the dealer:

- **Player busts** — lose your bet.
- **Player natural blackjack** vs no dealer blackjack — win **3:2** (1.5× your bet).
- **Both have blackjack** — push (tie, no money changes hands).
- **Dealer busts** (and you didn't) — win your bet.
- **Your total beats the dealer's** — win your bet.
- **Dealer beats you** — lose your bet.
- **Equal totals** — push.

All results are computed **identically on every player's machine** from the same revealed cards,
so there is one agreed outcome.

### 3.8 Continuous play

A real table doesn't stop after one hand. Group blackjack deals **hand after hand
continuously**, with a roughly **10-second pause** after each hand so everyone can read the
results, then the next hand deals automatically. This continues until you leave, until too few
players remain, or until the house bankroll can no longer pay.

---

## 4. Group blackjack — the on-chain pot lifecycle

This section is the heart of how real money is handled. The single most important idea:

> **The cards deal instantly. The money is secured in the background, in parallel, and never
> blocks play.**

### 4.1 Overview: instant deal, background money

The moment the seats fill and seat order is agreed, the first hand is **dealt immediately** (in
under a second). At the same time, separate background work secures the on-chain pot. If that
background work has finished by the time the session ends, the payout is fully on-chain; if it
has not (for example because someone tried to cheat their stake), the table still plays and
simply settles on its tracked bankroll as a best effort. Either way, **the game never stalls
waiting on the money.**

The phases the background pot work moves through are: **Funding → miner verification → refund
co-signing → (pot ready)**, and at the end **Settling** (or a **leaver split** mid-session).

### 4.2 Stage 1 — Funding (escrowing the stakes)

Each player escrows their stake into the **one shared n-of-n pot** (see [1.4](#14-the-n-of-n-pot))
as a single on-chain coin. Your stake is your **buy-in plus an equal share of the house
bankroll**. The app builds and broadcasts your funding transaction locally without blocking, then
announces the resulting pot coin to the other players. Every announced stake is structurally
verified by the other players: the raw transaction must hash to the claimed id, and the named
output must pay exactly the claimed amount to the agreed pot script. A bogus "I funded nothing"
claim is rejected.

### 4.3 Stage 2 — Miner verification (first-seen)

Once all stakes are announced, the app asks the miner about each one (see
[1.6](#16-asking-the-miner-the-double-spend-check)). A stake counts only if a miner accepts its
funding transaction under first-seen. A double-spent stake is flagged and blocks the pot from
becoming "ready" — but, again, **the game keeps playing** the whole time. This runs in the
background on its own threads.

### 4.4 Stage 3 — Co-signing the refund

With the stakes in hand, the players co-sign the pre-signed, time-locked **refund** (see
[1.5](#15-the-pre-signed-refund-your-safety-net)). Once everyone has co-signed and the refund is
assembled and verified, the pot is marked **ready**: from this point the end-of-session payout
can be done fully on-chain. All of this is background work.

### 4.5 Stage 4 — Playing

You play hand after hand (see [3.8](#38-continuous-play)). Each hand updates the **bankrolls**:
the house wins what players lose and pays what they win, conserved to the satoshi. The on-chain
pot coins do not move during play — only the tracked bankroll figures change. The pot moves on
chain only at settlement or a split.

### 4.6 Stage 5 — Settlement (end of session)

When the session ends (you leave and too few remain, or the house busts), all the pot coins are
spent in **one settlement transaction** that pays every player their **final standing** (their
bankroll plus their share of the house residual), conserved exactly, with the tiny on-chain fee
taken off the largest payout so every payout stays positive. **Every player co-signs** this one
transaction (it is n-of-n). The app assembles it only when every signature is present **and** the
result fully verifies, so a missing or garbage signature can never be mistaken for a completed
payout. The settled transaction is then broadcast, and any straggler adopts the finished
transaction so no one is left waiting.

### 4.7 Stage 6 — The leaver split

If a player leaves while **3 or more** players remain and the pot is ready, the pot is **split
on-chain** rather than closed:

- The leaver is **paid out** on-chain to their standing.
- The remainder is **re-escrowed** into a **new** n-of-n pot for the players who continue.
- The remaining players adopt the new pot, re-secure it with a fresh refund in the background,
  and **play on** — no re-funding, no re-confirmation needed.

This split is co-signed by the current players and built deterministically (identically on every
machine), and each split bumps a pot "generation" so a stale signature from before the split can
never be mixed into the new pot. If a leave would drop the table below two players, the table
settles and ends instead.

### 4.8 The dealer/house bankroll

The house has its own real-token bankroll, staked to cover the table. Each hand it wins what
players lose and pays what players win. Both your bankroll and the house bankroll are shown every
hand. If the house bankroll cannot cover the players, the table **closes** — a real-money game
cannot run a house that cannot pay. At settlement, any house residual is shared out among the
players as part of their final standings.

### 4.9 What happens if something goes wrong

- **A player tries to double-spend their stake:** caught by the miner check; the pot doesn't
  become "ready"; the game still plays; the cheat is flagged.
- **A player refuses to co-sign the payout, signs garbage, or vanishes:** after a short grace
  window the pre-signed **refund** is broadcast and every stake is returned after the time-lock.
  Nobody loses their stake to a griefer.
- **The settlement finishes on one machine first:** the others **adopt** the finished,
  fully-verified transaction, so no one stalls.
- **Your app or machine drops out mid-session:** the n-of-n pot cannot be moved without you, and
  the time-locked refund guarantees the stakes can always be recovered. Your money cannot be
  taken in your absence.

---

## 5. Frequently misunderstood points

- **"My table dealt instantly but the status said it was still confirming stakes — is the pot
  real?"** Yes. The pot is real and is being secured in the background while you play. The deal
  is intentionally not blocked by the money work. See [4.1](#41-overview-instant-deal-background-money).
- **"Can I pull my bet out of a hand I'm in?"** No. As at a real table, once a hand is dealt your
  bet stands. You can **Leave**, which cashes you out **after** the current hand finishes.
- **"Is the dealer the house's computer?"** No. The dealer is computed jointly by all the players
  from the shared encrypted deck; nobody controls it and its hole card is unreadable until it
  plays.
- **"Can another player see my blackjack hole card?"** In blackjack the player cards are face-up
  by the rules of the game; the **dealer's** hole card is the one kept sealed until the dealer
  plays. (In **poker**, your hole cards are private to you until showdown.)
- **"What if someone wins the pot and refuses to pay the others?"** They can't — the pot is
  n-of-n and moves only on a co-signed payout, with the pre-signed refund as the fallback.
