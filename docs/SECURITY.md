# Security model

This client is intended to be read as open reference crypto infrastructure: the goal is to make any
weakness easy to find. This document states the properties the code aims to provide and how they are
enforced, so each claim can be checked against the code and the tests.

## Cryptographic primitives

- **secp256k1 only** (the BSV curve). ECDSA uses RFC-6979 deterministic nonces and enforces **low-S**, as
  BSV consensus requires. Ed25519 is not used anywhere.
- **Hashing:** SHA-256, SHA-256d, RIPEMD-160, and HASH160. RIPEMD-160 is re-implemented because .NET
  removed it; it is covered by known-answer tests.
- **Authenticated encryption:** AES-256-GCM (`nonce‖ciphertext‖tag`, fresh random nonce per message).
  Wrong key or tampered ciphertext fails the tag and throws — it never returns unauthenticated data.
- **Key derivation:** HKDF-SHA256 for message keys; PBKDF2-HMAC-SHA256 (250k iterations) for the
  password that protects the wallet seed at rest.

## Properties and how they are enforced

| Property | Mechanism |
|----------|-----------|
| No party controls the deck | Commit-before-reveal + composition of all players' unbiased permutations (`MentalPoker`). |
| Shuffle is unbiased | Rejection-sampled Fisher–Yates over an HMAC counter stream; the rejection bound is computed in 64-bit to avoid overflow. |
| Hole cards belong to one player | Cards are sealed (ECIES over secp256k1) to the owner; only their key opens them (`CardNft`). |
| Chat confidentiality, no key reuse | Fresh ephemeral ECDH key **per message per recipient** → HKDF → AES-256-GCM; the wire is ciphertext. |
| Hole cards are private during play | Networked deal uses commutative-encryption masking (`MentalPokerEC`): each peer can unmask only its own holes; the board is revealed per street, opponents' holes only at showdown. Proven 2-player and 3-player. |
| Funds are always recoverable | Each player pre-signs a unilateral nLockTime refund of 100% of their funds before risking any (`Chain.BuildRecovery`); shared pots use a co-signed 2-of-2 escrow recovery (`Chain.BuildEscrowRecovery`). |
| Signatures are consensus-valid | Low-S ECDSA, FORKID sighash, DER + hashtype; verification recomputes the digest from scriptCode. |
| No `OP_RETURN` | Card NFTs bind data with `OP_DROP`; no code path emits `OP_RETURN`. |
| A second instance is a different player | Per-instance profile claimed via an exclusive file lock, with its own seed and identity. |
| Clean termination, funds returned | `ShutdownMode.OnMainWindowClose` + a hard `OnExit` exit; no orphan processes. |

## Network and transport

- The transport is a peer-to-peer gossip mesh with **no server**. It has a connection cap, per-peer
  inbound rate limiting, and anti-eviction to resist resource-exhaustion from hostile peers.
- Chat messages are end-to-end encrypted (fresh ephemeral key per recipient per message); the mesh sees
  only ciphertext. The card deal is privacy-preserving by construction (`MentalPokerEC`).

## Known limitations (tracked, not hidden)

- **The network transport is not yet authenticated.** Game messages (`hello`, the deal stages, board and
  showdown reveals, and betting actions) are **unsigned**, and presence/table announcements are unsigned.
  This means card *privacy* holds against an honest-but-curious opponent, but the protocol is **not**
  secure against an *active* hostile peer who forges, replays, or spoofs messages (e.g. faking a seat or
  an action). Authenticating every message (signing bound to seat/hand/phase/sequence) and signing
  directory/presence is part of the deferred red-team hardening. Until then, treat networked play as safe
  only among cooperating peers, not in a fully adversarial setting.
- **On-chain broadcast** is not yet wired to a live node (the node is a separate project), so end-to-end
  settlement on a real network is pending even though the transactions (P2PKH and the 2-of-2 escrow,
  settlement, and recovery) are built, signed, and strictly verified in-process.
- Other hardening deferred to the red-team phase: loopback-by-default binding, byte-accurate frame/rate
  caps, strict DER on the legacy P2PKH path (the escrow path is already strict), and accountable
  commit/reveal abort timeouts.

## Red-team / audit

Red-team hardening is performed **after** the game is functionally complete, not before — always last.
An audit is not a trigger to start remediation. Findings and their fixes (each with a positive and a
hostile-negative test) will be tracked here when that phase is explicitly run.
