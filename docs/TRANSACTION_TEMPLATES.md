# Transaction Templates (every type, documented and published)

A transaction is like a frame in IP: **every kind of action has its own transaction TYPE** — its own
tag ("frame type"), version, documented field structure, and contract. This document publishes **all** of
them. Nothing is generic or assumed; if a field exists it is named here.

## On-chain encoding

A typed output is built by `TxTemplates.BuildOutput(kind, fields, ownerPub)` as:

```
<marker> OP_DROP   (<field> OP_DROP)*   <ownerPub(33)> OP_CHECKSIG
```

- `<marker>` is the type tag (ASCII, e.g. `BSVP:DM:1`) — the "frame type."
- each documented field is a data push immediately followed by `OP_DROP` (so the data is carried on-chain
  but does not affect spendability).
- the output is spendable by the owner (`<ownerPub> OP_CHECKSIG`).
- **never `OP_RETURN`.** `TxTemplates.Parse` recovers `(kind, fields[], ownerPub)`; the field count must
  match the schema exactly or the build is rejected.

Value/conditional types (escrow, settlement, recovery, auction, bid) additionally use the conditional
Script contracts in `BsvPoker.Core.Script.Contracts` (P2PKH, hash-lock, time-lock, IF/ELSE auction-escrow,
n-of-n multisig) bound to the type marker.

## The type registry

| Kind | Tag | Ver | Fields | Purpose |
|------|-----|-----|--------|---------|
| Payment | `BSVP:PAY:1` | 1 | memo | A plain value transfer of satoshis between wallets. |
| KeepAlive | `BSVP:KA:1` | 1 | seat, nonce | Liveness heartbeat that a peer/seat is still present. |
| ChatDirect | `BSVP:DM:1` | 1 | senderPub, recipientPub, ciphertext | One-to-one encrypted chat (ECDH+AES). |
| ChatGroup | `BSVP:GC:1` | 1 | groupId, senderPub, ciphertext | Group encrypted chat (per-recipient ECDH+AES). |
| CardNft | `BSVP:NFT:1` | 1 | sealCommitment | A card as a 1-sat NFT, sealed (ECDH+AES) to its owner. |
| Commitment | `BSVP:CMT:1` | 1 | commitHash | A hash commitment published before a reveal. |
| Reveal | `BSVP:RVL:1` | 1 | commitHash, preimage | The reveal of a prior commitment, verifiable against it. |
| ShuffleStage | `BSVP:SHF:1` | 1 | handId, step, deck | One seat's masking+shuffle stage of the deal. |
| Deal | `BSVP:DEAL:1` | 1 | handId, position, mask | Dealing a card to a seat (per-position mask reveal). |
| BoardReveal | `BSVP:BRD:1` | 1 | handId, street, mask | A community-board card revealed by all seats. |
| Showdown | `BSVP:SHO:1` | 1 | handId, seat, holeMasks | Showdown reveal of a seat's hole cards. |
| Bet | `BSVP:BET:1` | 1 | handId, seat, action, amount | A betting action committed on-chain. |
| PotEscrow | `BSVP:POT:1` | 1 | handId, members, amount | The pot in a threshold (n-of-n) escrow contract. |
| Settlement | `BSVP:STL:1` | 1 | handId, winnerPub | Cooperative settlement paying the pot to the winner(s). |
| Recovery | `BSVP:REC:1` | 1 | handId, lockHeight | Pre-signed nLockTime refund if a peer stalls. |
| Bid | `BSVP:BID:1` | 1 | auctionId, bidderPub, amount, commit | A conditional bid in an on-chain auction. |
| Auction | `BSVP:AUC:1` | 1 | auctionId, item, reserve, deadline | An auction genesis defining the item/role and rules. |
| RoleClaim | `BSVP:ROLE:1` | 1 | auctionId, role, winnerPub | Claiming an auctioned role (banker, dealer, draw). |
| TableGenesis | `BSVP:TBL:1` | 1 | tableId, variant, seats, stakes | Creating a table (its genesis). |
| GameStart | `BSVP:GAME:1` | 1 | tableId, gameId | Starting a game at a table. |
| HandStart | `BSVP:HAND:1` | 1 | gameId, handId, button | Starting a hand within a game. |

This list is exhaustive for the current stage and grows as new actions are added — **every** new action
gets its own type, schema, contract, and a row here. Versions bump when a template's structure changes.
