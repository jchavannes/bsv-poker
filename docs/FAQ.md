# BSV Poker — Frequently Asked Questions

Real questions a new user asks, with plain, honest answers. For step-by-step instructions see
[USER_GUIDE.md](USER_GUIDE.md); for the game rules and on-chain mechanics see [GAMES.md](GAMES.md);
for fixes when something is wrong see [TROUBLESHOOTING.md](TROUBLESHOOTING.md); for definitions see
[GLOSSARY.md](GLOSSARY.md).

---

## Table of contents

- [Getting started](#getting-started)
- [Wallet and money](#wallet-and-money)
- [Identity and privacy](#identity-and-privacy)
- [Finding and playing with people](#finding-and-playing-with-people)
- [Group blackjack and the pot](#group-blackjack-and-the-pot)
- [Poker](#poker)
- [Safety and trust](#safety-and-trust)
- [Technical](#technical)

---

## Getting started

### Do I need to install .NET or any other software first?
No. BSV Poker is a single, self-contained Windows program (`poker.exe`). Everything it needs is
already inside it. Just install or unzip it and run.

### Is there a website or account I sign up for?
No. There is no server, no website, and no account. The app runs entirely on your machine and
talks directly to other players and to the Bitcoin network. Your "account" is your **wallet**,
which lives on your device.

### How do I start the app the first time?
Launch it, then: **Select your wallet** (or create one), enter or set a **password**, and the
main window opens. See [USER_GUIDE.md](USER_GUIDE.md), section 4.

### Is it free to use?
The software is free to run. Playing for real on mainnet uses **real BSV**: each action is a tiny
on-chain transaction (typically about a satoshi plus a small fee). To practice for free, switch
the network selector to **Testnet** (free test coins) or **Regtest** (your own local chain).

### Can I try it without risking real money?
Yes — use **Testnet** or **Regtest** from the network selector at the top of the window. You can
also play the practice bot.

---

## Wallet and money

### My wallet shows a balance of zero — is that a bug?
No. A new wallet starts **empty**. Fund it by sending BSV to your receive address
(Wallet → Receive), or switch to Testnet/Regtest to practice for free.

### How do I add money?
Go to **Wallet → Receive**, copy your address (or scan the QR), and send BSV to it. The app
detects the incoming payment automatically — pending first, then confirmed once it is mined and
SPV-proven.

### Why does my receive address keep changing?
Addresses are **single-use** for privacy. After you receive to one, the wallet gives you a fresh
one. This is normal and good practice.

### Where is my money actually stored?
On the blockchain, controlled by keys derived from your **seed**, which lives on your device. The
app holds the keys (encrypted with your password) and tracks your coins via SPV. There is no
server holding your funds.

### What is the seed and why does it matter so much?
The seed is the single master backup of your whole wallet. Anyone with it controls your money;
anyone without it (including you, if you lose it) cannot recover the wallet. Write it down on
paper and keep it safe. See [USER_GUIDE.md](USER_GUIDE.md), section 6.5.

### I forgot my password. Can I get back in?
The password only encrypts your keys on disk — there is no reset email. If you have your **seed**,
you can restore the wallet (Open → restore from seed) and set a new password. Without the seed,
the funds are unrecoverable. This is the trade-off of holding your own money.

### Will uninstalling the app delete my wallet or money?
No. Your wallet and profile data live in a **separate** folder from the program and are preserved
across uninstall. Your real backup is still the **seed**, so keep it written down.

### What does the Transactions tab show?
A **live log of every on-chain action** the wallet takes — chat, bets, deals, identity
registration, pot funding, sends, and receives — shown immediately, before they confirm. It is
there so nothing happens behind your back.

### How do I send money to someone?
Wallet → Send. You can pay a normal address, an identity **@handle**, an identity public key, or a
`bitcoin:`/`pay:` URI.

---

## Identity and privacy

### Why am I being asked to "register an identity"?
To play or chat, your wallet needs an identity — a fixed pseudonym (a @handle) other players see
instead of a raw key. The app asks **once**, the first time you do something that needs it, then
never again. Registering writes a tiny (~1 sat) on-chain record, so fund the wallet first.

### Do other players see my real name or my keys?
No. They see your **@handle** (pseudonym). Your private keys never leave your device.

### Are my chat messages private?
Yes. Every message is encrypted; the wire only ever carries ciphertext. Direct messages use a
fresh ephemeral key per recipient. "Broadcast to everyone" is encrypted to every known recipient
— it is never sent as plaintext.

### Can I message someone who is offline?
Yes. The message lands on the recipient's identity address on-chain and they receive it the next
time they sync. If they are online it arrives instantly; if not, it waits for them. You do nothing
extra either way.

---

## Finding and playing with people

### How do I find other players? Do I need their IP address?
No IP needed for normal play. Discovery is automatic: players on the **same machine** connect
instantly, players on your **same network** within a few seconds, and players **across the
internet** via the on-chain directory. Open tables just appear in the Lobby — double-click to
join.

### What is "Publish my node on-chain" for?
It advertises your node's address on the on-chain directory so people **across the internet** can
find you automatically. It is an explicit, opt-in action that spends about 3 satoshis. Local and
same-network discovery does not need it.

### I clicked publish but see no transaction — what happened?
See [TROUBLESHOOTING.md](TROUBLESHOOTING.md). Common causes: the wallet isn't funded/unlocked, or
the broadcast didn't reach a server. The app reports the txid when it works; it also appears in
Transactions.

### Can I run two players on one computer?
Yes. Each running copy of the app is a **different player** with its own wallet and identity. Just
launch the app twice.

### What's the difference between "Play a bot" and "Play MY bot"?
"Play a bot" is a quick practice hand against a simple opponent. "Play MY bot (on-chain)" opens
your own bot — a separate player derived from your identity that only ever plays you — for a real,
secure, dealerless hand, with a fully on-chain copy recorded into the Replay tab.

---

## Group blackjack and the pot

### Why did my table start instantly but say it was "confirming stakes" — is the pot real?
The pot is **real**. The deal is intentionally instant; the on-chain pot is secured in the
**background**, in parallel, while you play. Confirming stakes means the app is asking the miners
to accept each player's funding transaction. The cards never wait for the money.

### Is blackjack one-on-one against the house?
No, never. Group blackjack is **2–6 players**, and the "dealer" is computed jointly between all
the players from a shared, encrypted deck. There is no house croupier and no operator.

### Who controls the dealer's cards?
Nobody single. The dealer's cards come from the shared, jointly-shuffled deck. Its hole card is
sealed (unreadable by anyone) until every player has finished, then it plays to 17.

### What if someone double-spends their stake?
The app **asks the miner** to confirm each stake (BSV first-seen). A double-spent stake is
rejected and flagged; it simply keeps the pot from becoming "ready." The game keeps playing
throughout; it just means the end-of-session payout may settle on tracked bankroll instead of
fully on-chain.

### Can I leave in the middle of a hand?
You can press **Leave** any time, but you **finish the current hand first** — your bet stands. You
cannot pull a bet out of a hand that's already dealt. After the hand, you're cashed out and the
others play on.

### What happens to the pot when I leave?
If 3+ players remain and the pot is secured, the pot **splits on-chain**: you're cashed out, the
remainder is re-escrowed for the others, and they keep playing. If too few remain, the table
settles and ends.

### What is my "bankroll" versus the "house" figure?
Your bankroll is the cash you currently hold at the table (buy-in ± each hand's result) — what you
walk away with. The house bankroll is the dealer's stake; it wins what players lose and pays what
they win. Both are shown every hand.

### Why couldn't I act after going over 21?
Because you **busted**. Over 21 ends your turn immediately; the buttons disable. This is enforced
so you can't accidentally act past a bust.

### Why did my new card take a moment to appear after I hit?
Because the card has to be revealed by **all** players (that's how the encrypted deck works). You
can't act again until you actually see it — that's deliberate.

---

## Poker

### Can other players see my cards?
No. Through the mental-poker deal you see **only your own hole cards** until showdown. The others'
hidden cards are locked by secrets no one fully holds, so they cannot be read early — by anyone.

### What poker games can I play?
Six variants, chosen when you host a table (default Texas Hold'em). Up to 6 players, with blinds,
all-in side pots, and exact chip conservation.

### Are my poker bets really on-chain?
Yes. Every move becomes a real on-chain transaction (about a satoshi), broadcast both to the other
players and to the miners. You can see them in Transactions, and replay a finished hand in the
Replay tab.

### What is "Practice deal"?
A local hot-seat hand with both sides visible, for learning the controls. No network, no real
money.

---

## Safety and trust

### Is my money safe if someone refuses to pay?
Yes. The pot is **n-of-n** — every player must sign for it to move — and before any card is dealt
everyone co-signs a **pre-signed, time-locked refund**. If anyone refuses to co-sign the payout
(or vanishes), the refund is broadcast and every stake is returned after the time-lock. Nobody can
run off with the pot.

### Can the app or its author take my funds?
No. There is no server and no operator in the money path. Your keys are on your device; the pots
need your signature; the refund protects you. The app cannot move your money without you.

### Is this gambling with real money?
On **mainnet**, yes — the satoshis are real. Play responsibly, and use Testnet/Regtest if you only
want to practice.

### What stops someone from cheating the deck?
The deck is shuffled and encrypted jointly, and every card reveal is checked against a
cryptographic commitment, so a player cannot lie about their part to change a card. Attempts are
detected.

---

## Technical

### What network is it on by default?
**Mainnet** (real BSV). You can switch to **Testnet** or **Regtest** with the selector at the top;
your choice is remembered, and the same seed works on every network.

### How does the app know my balance without a server?
**SPV.** It downloads and verifies block headers itself, then accepts a payment only with a proof
(merkle proof) that it was mined into a block in that verified chain. No server is trusted.

### Where is my data stored on disk?
The program is in `%LOCALAPPDATA%\Programs\BSV Poker`; your wallet/profile data is separately in
`%LOCALAPPDATA%\BsvPoker\profiles\` (one folder per profile).

### Does it use OP_RETURN?
No. Card data is bound with `OP_DROP`, not `OP_RETURN`.

### What cryptography does it use?
Bitcoin's own curve, secp256k1, throughout (no Ed25519). Encryption at rest is AES-256-GCM; chat
uses ephemeral ECDH → HKDF → AES-256-GCM.

### How do I update?
Install the newer version over the old one (Setup.exe, the PowerShell installer, or replacing the
portable `poker.exe`). Your wallet/profile data is untouched. See [INSTALL.md](INSTALL.md).

### Something isn't working — where do I look?
[TROUBLESHOOTING.md](TROUBLESHOOTING.md) covers wallet unlock problems, not finding players,
tables not starting, stuck-unconfirmed payments, funding issues, broadcast failures, the pot never
becoming "ready," and node-publish showing no transaction.
