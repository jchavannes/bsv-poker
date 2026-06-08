# SPV — how the wallet sees the chain with no server

> Status: living document, under active construction.

The wallet is SPV in the Craig-Wright / whitepaper §8 sense: it keeps and validates **block headers**, and it
accepts a coin only when it has a **merkle proof** that the coin's transaction is in a block whose header it
validated itself. It connects to **no** server, indexer, or API — only to BSV peers, and only to (a) sync
headers, (b) load a bloom filter and receive matching transactions + proofs, and (c) broadcast. It is always
online; "offline" is not a state this software has.

Code: `BsvPoker.Net` — `BsvNode`, `HeaderStore` / `HeadersChain`, `BloomFilter`, `PartialMerkleTree`,
`SpvFunding`, `BlockHeader`, `BsvBlock`, `TxLink`; wallet side in `WalletView` (`ConfirmIncoming`,
`FilterElements`, `FindByTxid`, `ImportFunding`).

## 1. Headers — the only thing we trust

`BsvNode` resolves the network's DNS seeds, dials peers, completes the version/verack handshake, and syncs
headers from genesis with `getheaders`, validating **proof-of-work and parent linkage** across batch
boundaries before appending to a persistent `HeaderStore`. The active chain is the most-work branch
(`HeadersChain`), so the node follows the real chain and handles reorgs without trusting any peer. A coin is
only ever confirmed against a header that passed this validation.

**WHAT BREAKS IT.** On mainnet the header chain starts at genesis (900k+ headers), so until sync reaches a
payment's block, that payment shows as *pending* and cannot be confirmed yet. This is honest SPV behaviour, not
a bug, and is never papered over with fake confirmation.

## 2. Bloom-filter discovery — payments find you, no indexer

`BloomFilter` is a Bitcoin connection bloom filter (MurmurHash3 x86-32), **byte-verified against the canonical
reference filter wire vectors** so real BSV peers accept it. `WalletView.FilterElements()` loads every
receive/change/seat key's hash160 + pubkey (and any swept keys); `BsvNode.SetSpvFilter` sends `filterload` to
all peers and on connect, then `mempool` (instant detect of a just-sent payment). For history,
`RequestFilteredBlocks` sends `getdata MSG_FILTERED_BLOCK` over recent headers.

A matching transaction arrives with a `merkleblock`; `BsvNode` pairs proof↔tx by txid and raises
`OnConfirmedTransaction`. `WalletView.ConfirmIncoming` re-verifies via `SpvFunding.VerifyFromMerkleBlock`: the
partial tree must recompute to a root equal to a header **we** validated, the tx must be among the proven
leaves, and an output must pay one of our keys — only then is a confirmed UTXO added. A false positive in the
filter only wastes a little bandwidth; it can never credit money that is not ours.

## 3. PartialMerkleTree — the proof format

`PartialMerkleTree` builds and parses Bitcoin `merkleblock` payloads (80-byte header, tx count, hashes, flag
bits). `Extract` recomputes the root and collects the matched `(txid, index)` pairs, and **rejects malformed
trees** (too many/few hashes or bits, an illegitimately duplicated right branch) so a hostile peer cannot
smuggle a tree that "verifies" against an unrelated root. Hostile-input tests cover these.

## 4. The three funding paths (all server-less)

1. **Automatic** — bloom filter + mempool + filtered blocks (above).
2. **Find by txid** — supply a txid + confirming block hash; the wallet fetches that block (`GetBlockAsync`),
   builds the merkle proof, and credits it.
3. **Envelope import** — a payer hands raw tx + merkleblock; the wallet verifies it against its own headers.

Identity payments are claimed with the receipt; the derived one-time key's coin is then found by these paths.

## 5. Transport — IP-to-IP, and every inter-machine message is a transaction

`TxLink` carries Bitcoin-wire `tx` messages directly between peers (the payer sends the funding tx IP-to-IP to
the recipient *and* to miners). Chat and game messages are themselves real, funded, encrypted transactions —
there is no off-chain side channel.

## 6. Same model on every network

Regtest, testnet, and mainnet run one code path; the network is only a parameter set (magic, port, address
version, seeds, genesis). The wallet always opens on mainnet by default.
