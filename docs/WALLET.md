# The BSV Wallet — architecture, every tab, and why

> Status: under active construction. This documents the wallet as it stands now. It is **not** a finished
> product description — it is a living engineering document that grows with every build.

The wallet is a full standalone BSV wallet built into `poker.exe`, modelled on **ElectrumSVP**
(github `TruthMachine/ElectrumSVP`) as a *baseline* and extended far beyond it: it is SPV (Craig-Wright /
whitepaper §8 sense), pure peer-to-peer (no server, no indexer), identity-first (a Base ID key with Type-42
hash-chained sub-keys), and integrated with chat, the game, and on-chain NFTs. It holds **real** satoshis
only — there is no play money and the wallet starts empty.

It lives in `dotnet/src/BsvPoker.App/Views/WalletView.cs`, on top of:
`BsvPoker.Crypto` (secp256k1, AEAD, hashes), `BsvPoker.Core` (Chain, OnChainWallet, WalletKeys, Identity =
Type42 + KeyChain + KeyRing + IdentityPayment, CardNft), and `BsvPoker.Net` (BsvNode, SpvFunding, BloomFilter,
PartialMerkleTree, HeaderStore/HeadersChain, TxLink, PokerGossip).

---

## 1. Money model — what a "balance" is and is not

**WHAT.** The balance is the sum of UTXOs that are (a) SPV-confirmed against headers this client validated
itself, and (b) unspent. Nothing else is ever counted. Unconfirmed-but-detected coins are shown as *pending*
and are never spendable. A brand-new or unfunded wallet shows exactly zero with no history.

**WHY.** Displaying any unit of money, or any history entry, that does not correspond to a real, SPV-verified,
on-chain coin is treated as criminal-grade fraud in this project. So the balance and the history are *derived*
from the verified UTXO set and the wallet's own real broadcasts — never from a free-form stored log that could
drift from reality.

**HOW (code).** `Balance => Utxos.Where(!Spent && Confirmed).Sum(Value)`;
`Pending => Utxos.Where(!Spent && !Confirmed).Sum(Value)`. On load, only `Confirmed` coins survive a restart
(pending coins are re-discovered by SPV); a valid seed is never wiped, so funds are never lost on rebuild.

**WHAT BREAKS IT.** If header sync has not yet reached the block that confirmed a payment, that payment stays
*pending* until the headers catch up — it cannot be confirmed without the header whose merkle root the proof
must match. This is inherent to from-genesis SPV and is surfaced honestly, never faked.

---

## 2. Keys & identity — the Base ID key and Type-42 hash chain

The wallet is one 32-byte master **seed** (Base58Check backup). Spending keys are derived from the seed by
domain-separated HMAC-SHA256 reduced to a secp256k1 scalar (`WalletKeys.Account(seed, chain, index)`). No
BIP32/39/44 — BSV-native only.

The **identity** is a separate concept and is shared across the whole app (wallet, chat, game, NFT sealing):

- **Base ID key** (`Identity.cs` → `KeyRing.IdentityPriv/Pub`, here the profile identity): a secp256k1 key
  that is **never used as an address** and never receives funds directly. Its only role is to derive ECDH
  sub-keys.
- **Type-42 derivation** (`Type42`): `shared = ECDH(rootPriv, counterpartyPub)`,
  `k = HMAC-SHA256(shared, invoice) mod n`, `childPriv = (rootPriv + k) mod n`, `childPub = rootPub + k·G`.
  The payer computes the payee's child *public* key (the address to pay) without the payee's private key; the
  payee computes the matching child *private* key to spend it. Every payment/message uses a fresh one-time key.
- **Hash-chained index** (`KeyChain`): `link[0] = SHA256("bsvpoker-keychain/v1" ‖ rootPub)`,
  `link[i] = SHA256(link[i-1] ‖ be32(i))`, and `k[i]` binds the ECDH secret, an HMAC, and the prior link, so
  the whole set is provably one ordered chain (`KeyChain.Verify`). Tamper with any link and every later key
  fails to reproduce.

**WHY this design.** Digital scarcity under hostile (nation-state) attack needs every key provably linked,
ordered, and one-use, with no path from a sub-key (or a leaked link) back to the root or a sibling. ECDH binds
each key to a specific counterparty; the hash chain binds order and prevents reuse.

**Security boundary.** The seed and identity private key never leave the process unencrypted; at rest they are
AES-GCM encrypted under a password (Tools → Password). Losing the seed loses the wallet; the seed backup is the
whole backup.

---

## 3. SPV payment discovery — no server, the whitepaper way

The wallet learns its coins three ways, all server-less:

1. **Automatic bloom-filter SPV** (`BloomFilter` + `BsvNode.SetSpvFilter`): the wallet loads its addresses
   (hash160 + pubkey of receive/change/seat keys, and any swept keys) into a Bitcoin connection bloom filter
   and sends `filterload` to its peers, then `mempool` (instant detect of a just-sent payment) and `getdata
   MSG_FILTERED_BLOCK` over recent headers (catch a payment confirmed before connecting). Peers return matching
   txs + a `merkleblock` proof, which the wallet re-verifies against headers it validated itself
   (`SpvFunding.VerifyFromMerkleBlock`) before crediting a confirmed UTXO. The bloom filter is byte-verified
   against the canonical reference filter wire vectors.
2. **Find by txid** (Receive tab): supply a txid + the confirming block hash; the wallet fetches that block
   from a peer, builds + verifies a merkle proof, and credits any output paying it.
3. **SPV envelope import** (Receive tab): a payer hands over the raw tx + a merkleblock; the wallet verifies it
   against its own headers and credits it.

A payment to an **identity** is claimed via the receipt (`identity-payment|payerIdPub|invoice|txid`): the payee
derives the one-time spend key (`IdentityPayment.SpendPriv`) and the coin is found by rescan.

---

## 4. The tabs (ElectrumSVP order, dark theme)

Menu bar: **File · Wallet · Account · View · Tools · Help** (Tools includes Sign/Verify, Encrypt/Decrypt
message, Pay-to-many, Sweep Private Key, Load/Broadcast transaction, Pay invoice BIP270). Bottom status bar:
balance (+pending), lock state, network + SPV peer count, and a live status message.

- **History** — every real movement (received + sent), running balance, labels (editable), Details viewer,
  CSV export. Right-click/buttons: copy txid, details, set label.
- **Transactions** — pending / unconfirmed coins.
- **Send** — aligned grid form: *Pay to* (address · @handle · identity pubkey · bitcoin:/pay: URI · or many
  `payee,amount` lines), *Amount* + Max, *Fee* (rate combo or custom), *Description*. Buttons: Send, Preview,
  Paste URI, Pay invoice (BIP270), Clear. Pay-to-identity produces a claim receipt for the payee.
- **Receive** — grid form (Receiving destination + copy/new, Requested amount, Description, Save request) with
  a 200×200 QR (rendered by an in-tree Reed–Solomon encoder) to the right and a Requests list below; plus the
  SPV funding tools (import envelope, create envelope, find-by-txid, rescan, claim-identity-payment).
- **Notifications** — wallet events.
- **Destinations** (Keys) — the hash-chained key ring: address, derivation path, balance, used flag, label;
  copy, show WIF, show more.
- **Coins (UTXOs)** — full coin control: every coin with outpoint, address, value, status, frozen flag;
  freeze/unfreeze, spend selected.
- **Contacts** — handle ↔ identity public key; add/update/delete, pay a contact.
- **Identity** — your Base ID key (the NFT-equivalent identity), set a handle, copy/share the identity key.
- **NFTs** — renders the real card NFTs (1-sat on-chain outputs sealed to your identity) as tiles with their
  on-chain provenance; click to copy the sealed blob.
- **Console / Tools** — security (password/encrypt, unlock, seed backup, restore), messages (sign/verify,
  encrypt/decrypt to an identity), import (sweep WIF), transactions (load/broadcast, master public key).

---

## 5. BIP270 invoice payment (Anypay / Centi)

`Bip270.cs`: extract the payment-request URL from a URL/QR/`pay:` URI, GET the JSON PaymentRequest, build and
broadcast a transaction paying exactly its outputs (`OnChainWallet.BuildActionMany`), then POST the BIP270
Payment to the merchant and read the PaymentACK. Only the merchant handshake is HTTP; the money is a real
on-chain transaction.

---

## 6. Integration with chat, game, and NFTs

One identity key flows through everything: the wallet pays/encrypts/claims with the same Base ID key that chat
uses to address messages and the game uses for the player identity, and NFTs are sealed to it (the shared
`CardVault`, surfaced in the NFTs tab). Paying a handle, messaging a handle, and playing a peer all reference
the same identity.

---

## 7. What is deliberately NOT here

No OP_RETURN (the on-chain design uses typed outputs); no BIP32/39/44 or mnemonic; no server, indexer, or API;
no play money; no central anything.
