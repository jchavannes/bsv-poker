# BSV Poker — Glossary

Plain-English definitions of every term you'll meet in BSV Poker and its documentation. Where a
term has a deeper write-up, a link points to it. For how things fit together, see
[USER_GUIDE.md](USER_GUIDE.md) and [GAMES.md](GAMES.md).

Terms are grouped for browsing, then there's an A–Z index at the end.

---

## Table of contents

1. [Money and the wallet](#1-money-and-the-wallet)
2. [Identity and the network](#2-identity-and-the-network)
3. [Verifying the blockchain (SPV)](#3-verifying-the-blockchain-spv)
4. [The games and the cards](#4-the-games-and-the-cards)
5. [The on-chain pot (blackjack)](#5-the-on-chain-pot-blackjack)
6. [Security and cryptography](#6-security-and-cryptography)
7. [A–Z quick index](#7-az-quick-index)

---

## 1. Money and the wallet

**BSV (Bitcoin SV)** — the blockchain this app runs on. Its coins are what you play with on
mainnet.

**Satoshi (sat)** — the smallest unit of Bitcoin. Amounts in the app are shown in satoshis. Many
actions cost only about a single satoshi plus a tiny fee.

**Wallet** — your keychain. It holds your coins (as keys to spend them), your identity, and your
history. It is **not** an account on a server — it lives on your device.

**Seed** — the single 32-byte master secret that backs up your entire wallet. Every spending key
derives from it. Whoever has the seed has the money; whoever loses it (and the password) loses the
wallet. Back it up on paper. (BSV-native, backed up as a single Base58Check string — not a BIP39
word list.)

**Password (password-at-rest)** — encrypts your keys on disk (AES-256-GCM). It is local
encryption, **not** a login to any service. There is no reset — only the seed can recover a
wallet whose password is forgotten.

**Address** — where someone sends you coins. BSV Poker uses **single-use** addresses: a fresh one
per payment, for privacy.

**Single-use address** — a receive address intended to be used once and then replaced by a fresh
one, so an outside observer can't easily link your payments.

**UTXO (Unspent Transaction Output)** — an individual coin your wallet holds. Your balance is the
sum of your spendable UTXOs. The **Coins (UTXOs)** wallet sub-tab lists them with their state
(confirmed, unconfirmed, double-spent).

**Confirmed / Unconfirmed** — a coin is *unconfirmed* when received but not yet mined into a
block; *confirmed* once it has an SPV proof that it was mined. 0-confirmation coins are still
spendable in play.

**Balance** — your total spendable satoshis, derived entirely from SPV-verified coins (not from a
server's word).

**Transaction (tx)** — a signed record on the blockchain that moves coins and/or carries data. In
BSV Poker nearly every action — a bet, a chat message, a deal, identity registration, a pot
funding, a send — is its own tiny transaction.

**Txid (transaction id)** — the unique fingerprint of a transaction. The app shows you txids for
things like publishing your node.

**Fee** — the small amount paid to miners to include your transaction. BSV fees are tiny (often
sub-satoshi to a few satoshis).

**WIF (Wallet Import Format)** — a standard text encoding of a single private key, which you can
export from the wallet.

**Send / Receive** — the wallet sub-tabs for paying out and getting paid.

**Transactions tab** — the wallet's **live activity log**: every on-chain action appears here
immediately, before it confirms. The **History** tab shows confirmed history with a running
balance.

**Vault** — advanced storage: a 2-of-2 multisig coin with a time-locked recovery path.

---

## 2. Identity and the network

**Identity** — your fixed pseudonym in the app (a @handle plus optional details), self-signed and
optionally written on-chain. You need one to play or chat. Other players see your @handle, never a
raw key.

**Identity key (Base ID)** — the key that *is* your identity: the wallet pays/encrypts/claims with
it, chat and game messages are signed with it, and your card NFTs are sealed to it. One identity
across everything.

**@handle / pseudonym** — the friendly name others see for you (e.g. `@alice`).

**Identity registration** — the one-time step that sets your identity (and writes a ~1-sat on-chain
record). Offered the first time you try to play or chat; never asked again afterwards.

**Peer-to-peer (P2P)** — players connect directly to each other; there is no central server, relay,
or operator anywhere in the app.

**Node** — your app *is* a node that peers directly with the Bitcoin network and with other
players. "Connecting" means joining that peer-to-peer network.

**Peer discovery** — how the app finds other players automatically: same-machine via a shared
rendezvous file, same-network via a LAN sweep on a well-known port, and across the internet via
the on-chain node-seed registry. No IP entry needed for normal play.

**Node-seed registry / "Publish my node on-chain"** — an on-chain directory of node addresses.
Reading it is free; **publishing** your own address (so internet players can find you) is an
explicit, opt-in action costing ~3 satoshis that returns a txid.

**Rendezvous file** — a small shared file used so two copies on the **same machine** find each
other instantly.

**LAN / subnet sweep** — the automatic check across your local network (on a well-known port) that
finds same-network players within a few seconds.

**Dual-path broadcast (redundant)** — every move/transaction is sent two ways at once: directly to
the other players (IP-to-IP) **and** to the miners. This redundancy means a move can't be quietly
raced by a double-spend and still lands if one path struggles.

**Lobby** — the tab where tables are discovered, hosted, and joined.

**Table** — a game instance (poker or blackjack) you host or join. It advertises itself across the
network so others can see and join it.

**Who's-online directory** — a live list of players reachable right now (handle + endpoint),
gossiped between apps; used so you can message someone instantly without exchanging keys or IPs.

---

## 3. Verifying the blockchain (SPV)

**SPV (Simplified Payment Verification)** — the trust-minimised way the app checks the blockchain
without trusting any server. It downloads and validates **block headers** itself, then accepts a
payment only when it has a **merkle proof** that the payment was mined into a block in that
validated chain. See [SPV.md](SPV.md).

**Block / block header** — blocks are the chunks the blockchain is built from; a header is the
compact summary of a block that SPV validates.

**Mempool** — the pool of transactions miners have seen but not yet put in a block. The app watches
it to detect a just-sent payment instantly.

**Merkle proof / merkleblock** — the cryptographic proof that a specific transaction is included in
a specific block. SPV uses it to confirm a payment against headers it trusts.

**Rescan ("Find my coins")** — a full re-scan of recent blocks to discover coins, useful if a
payment confirmed before your app was connected.

**Network (Mainnet / Testnet / Regtest)** — Mainnet is real BSV; Testnet uses free, worthless test
coins for practice; Regtest is your own local chain (can self-fund). The same seed works on all
three; your choice is remembered per profile.

---

## 4. The games and the cards

**Mental poker** — the cryptographic technique that lets players shuffle and deal a deck **with no
dealer** and keep cards hidden. Each player applies their own secret shuffle-and-lock; a card opens
only when the needed players contribute their parts. See [MENTAL_POKER.md](MENTAL_POKER.md).

**Dealerless** — no operator, house, or single player acts as the dealer. The deck is jointly
shuffled and encrypted by all players.

**Commutative encryption** — the kind of locking used in the deal: locks can be applied and removed
in any order, which is what makes the joint shuffle work while keeping cards hidden.

**Hole cards** — your private cards. In poker, only you can read them until showdown.

**Showdown** — the end of a poker hand when remaining players' cards are revealed and the winner is
decided.

**Commit-reveal (seat order)** — the fair way seats are assigned: everyone commits to a secret
value before anyone reveals, so no one can rig the seating.

**Reveal commitment** — a cryptographic commitment attached when a player reveals their part of a
card, so they can't lie about it to change which card appears. A mismatch is detected as cheating.

**Variant** — one of the six poker games selectable when hosting a table (default Texas Hold'em).

**Side pot (all-in)** — when a player is all-in for less than others' bets, extra chips go into a
separate pot they can't win; the engine handles these and conserves chips exactly.

**Card NFT** — a card held in your wallet as a non-fungible token, sealed to your identity. Card
data is bound with `OP_DROP` (never `OP_RETURN`).

**Replay** — stepping through a finished, fully on-chain hand move by move, fetched from the
blockchain by its start address or a txid.

**Bot** — an automated player. "Play a bot" is a simple practice opponent; "Play MY bot" is your
own bot, derived from your identity, that only ever plays you.

**Bust** — in blackjack, going over 21. Your turn ends immediately and you lose that hand's bet.

**Hit / Stand / Double** — blackjack actions: take a card / keep your hand and end your turn /
double your bet for exactly one more card then end your turn.

**Natural blackjack** — a two-card 21 (Ace + ten-value card); pays 3:2.

**Bankroll** — the cash a player currently holds at a blackjack table (buy-in ± each hand's
result); what they cash out on leaving.

**House / dealer bankroll** — the dealer's real-token stake; it wins what players lose and pays what
they win. If it can't cover the table, the table closes.

**Buy-in** — the chips/stake you bring to a table.

**Continuous play** — a blackjack table deals hand after hand automatically, with a ~10-second
pause between hands, until players leave or the house can't pay.

---

## 5. The on-chain pot (blackjack)

**Pot** — the shared money for a blackjack session, held in a single on-chain coin arrangement.

**Escrow** — locking your stake into the shared pot before play, as a real on-chain coin.

**Stake** — what each player escrows: their buy-in plus an equal share of the house bankroll.
Everyone escrows the same full stake.

**n-of-n** — the locking rule on the pot: there are *n* players and **all n** must sign for the
money to move. No subset can spend it.

**First-seen** — BSV's rule that the first valid version of a transaction a miner sees is the one it
keeps. The app uses it to verify stakes: a stake counts only if a miner accepts its funding tx.

**Double-spend** — trying to spend the same coins twice (e.g. faking a stake while spending those
coins elsewhere). Caught by the miner (first-seen) check; a double-spent stake keeps the pot from
becoming "ready" but never freezes the game.

**"Confirming stakes" / asking the miner** — the background step where the app checks each player's
escrow with a miner. Runs while you play; doesn't block the deal.

**Pot "ready"** — the state where all stakes are escrowed, miner-verified, and the refund is
co-signed; only then can the end-of-session payout be fully on-chain.

**Settlement** — the single on-chain transaction at the end of a session that pays every player
their final standing, co-signed by all players, conserved to the satoshi.

**Leaver split** — when a player leaves a table with 3+ players: the leaver is cashed out on-chain
and the remainder is re-escrowed into a new pot for the players who continue, all co-signed by the
current players.

**Pot generation** — a counter bumped on each leaver split so a stale signature from before a split
can never be mixed into the new pot's signing.

**nLockTime refund (pre-signed)** — the safety net co-signed up front, before any card: a
time-locked transaction (about 30 days out) that returns every player's stake. It is broadcast only
if a cooperative payout can't be assembled (a player refuses to sign or vanishes), so no stake can
be stranded by a griefer.

**Griefing** — a player misbehaving not to profit but to disrupt (e.g. refusing to co-sign). The
pre-signed refund neutralises it: stakes always come back.

**Conservation (conserved to the satoshi)** — payouts always sum exactly to the pot; no satoshi is
created or lost. The tiny on-chain fee is taken off the largest payout so every payout stays
positive.

---

## 6. Security and cryptography

**secp256k1** — the elliptic-curve used by Bitcoin and by this app for all keys and signatures. (No
Ed25519 anywhere.)

**FORKID sighash** — the specific way BSV transactions are signed; the app uses it so its
transactions are valid on BSV.

**AES-256-GCM** — the symmetric encryption used to protect keys at rest and to encrypt chat
messages.

**ECDH (ephemeral)** — the key-agreement used per chat message to derive a one-time encryption key,
so no key is ever reused across messages.

**HKDF** — the key-derivation step that turns agreed secrets into the actual encryption key for a
message.

**Ciphertext** — encrypted data. Chat messages travel as ciphertext; only the intended
recipient(s) can read them.

**Broadcast encryption** — sending one message that only a chosen set of recipients can decrypt;
used for "send to everyone" and named groups, so a broadcast is never plaintext-to-all.

**Store-and-forward (offline chat)** — a message to an offline recipient lands on their identity
address on-chain and is picked up when they next sync.

**OP_DROP / OP_RETURN** — script operations for carrying data on-chain. BSV Poker binds card data
with **OP_DROP** and deliberately does **not** use OP_RETURN.

**Self-custody** — you, and only you, control your keys and funds. The flip side is responsibility:
back up your seed, because there's no operator to recover it for you.

**Profile** — the per-instance data slot (its own wallet + identity). Each running copy of the app
holds an exclusive lock on one profile, so a second copy is a different player.

---

## 7. A–Z quick index

- **@handle / pseudonym** — your friendly name; see [Identity](#2-identity-and-the-network).
- **Address** — where coins are sent; single-use.
- **AES-256-GCM** — encryption at rest and for chat.
- **Bankroll** — your cash at a blackjack table.
- **Base ID / identity key** — the one key behind your identity.
- **Block / header** — the chain's building blocks; headers are validated by SPV.
- **Bot** — an automated player.
- **Broadcast encryption** — encrypted send to many/groups.
- **BSV** — the blockchain the app runs on.
- **Bust** — going over 21 in blackjack.
- **Buy-in** — your starting stake at a table.
- **Card NFT** — a card held in your wallet as a token.
- **Ciphertext** — encrypted data.
- **Commit-reveal** — fair seat-order assignment.
- **Commutative encryption** — the deal's order-independent locking.
- **Confirmed / Unconfirmed** — mined vs not-yet-mined coins.
- **Conservation** — payouts sum exactly to the pot.
- **Continuous play** — hand after hand in blackjack.
- **Dealerless** — no dealer; jointly shuffled deck.
- **Double** — blackjack action: double bet, one card.
- **Double-spend** — spending the same coins twice; caught by first-seen.
- **Dual-path broadcast** — sending to peers and miners at once.
- **ECDH (ephemeral)** — per-message key agreement.
- **Escrow** — locking your stake into the pot.
- **Fee** — paid to miners to include a tx.
- **First-seen** — the rule used to verify stakes.
- **FORKID sighash** — how BSV txs are signed.
- **Generation (pot)** — counter that isolates pre/post-split signatures.
- **Griefing** — disruptive misbehaviour; neutralised by the refund.
- **HKDF** — key derivation for chat.
- **Hit** — blackjack action: take a card.
- **Hole cards** — your private cards.
- **House bankroll** — the dealer's stake.
- **Identity** — your pseudonym; needed to play/chat.
- **LAN sweep** — same-network peer discovery.
- **Leaver split** — cashing out a leaver, re-escrowing the rest.
- **Lobby** — find/host/join tables.
- **Mainnet / Testnet / Regtest** — real / test / local networks.
- **Mempool** — unmined transactions miners have seen.
- **Mental poker** — cryptographic dealerless dealing.
- **Merkle proof / merkleblock** — proof a tx is in a block.
- **n-of-n** — all players must sign to move the pot.
- **Natural blackjack** — two-card 21; pays 3:2.
- **Network** — see Mainnet / Testnet / Regtest.
- **nLockTime refund** — pre-signed time-locked safety transaction.
- **Node** — your app as a peer on the network.
- **Node-seed registry** — the on-chain node directory.
- **OP_DROP / OP_RETURN** — data ops; the app uses OP_DROP, not OP_RETURN.
- **Password** — local encryption of your keys.
- **Peer-to-peer (P2P)** — direct, server-less connections.
- **Peer discovery** — automatic finding of players.
- **Pot** — the shared blackjack money.
- **Pot "ready"** — escrowed + verified + refund co-signed.
- **Profile** — per-instance wallet/identity slot.
- **Publish my node** — opt-in on-chain advertising of your node.
- **Replay** — step through a finished on-chain hand.
- **Rescan ("Find my coins")** — full re-scan for coins.
- **Reveal commitment** — anti-cheat check on card reveals.
- **Satoshi (sat)** — smallest Bitcoin unit.
- **secp256k1** — the elliptic curve used throughout.
- **Seed** — the master wallet backup.
- **Self-custody** — you control your keys and funds.
- **Send / Receive** — wallet pay-out / get-paid tabs.
- **Settlement** — the final co-signed payout transaction.
- **Showdown** — poker hand reveal.
- **Side pot** — separate pot for all-in situations.
- **Single-use address** — a fresh address per payment.
- **SPV** — trust-minimised chain verification.
- **Stake** — buy-in plus a share of the house bankroll.
- **Stand** — blackjack action: keep your hand.
- **Store-and-forward** — offline message delivery on-chain.
- **Table** — a game instance.
- **Transaction (tx) / txid** — an on-chain record and its id.
- **Transactions tab** — the wallet's live activity log.
- **UTXO** — an individual coin.
- **Variant** — one of the six poker games.
- **Vault** — 2-of-2 multisig storage with recovery.
- **Wallet** — your on-device keychain.
- **Who's-online directory** — live list of reachable players.
- **WIF** — exported single private key format.
