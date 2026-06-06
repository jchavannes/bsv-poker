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
| Funds are always recoverable | Each player pre-signs a unilateral nLockTime refund of 100% of their funds before risking any (`Chain.BuildRecovery`). |
| Signatures are consensus-valid | Low-S ECDSA, FORKID sighash, DER + hashtype; verification recomputes the digest from scriptCode. |
| No `OP_RETURN` | Card NFTs bind data with `OP_DROP`; no code path emits `OP_RETURN`. |
| A second instance is a different player | Per-instance profile claimed via an exclusive file lock, with its own seed and identity. |
| Clean termination, funds returned | `ShutdownMode.OnMainWindowClose` + a hard `OnExit` exit; no orphan processes. |

## Network and transport

- The transport is a peer-to-peer gossip mesh with **no server**. It has a connection cap, per-peer
  inbound rate limiting, and anti-eviction to resist resource-exhaustion from hostile peers.
- All application messages that carry secrets (chat, and the planned private deal) are end-to-end
  encrypted; the mesh sees only ciphertext.

## Known limitations (tracked, not hidden)

- **Hole-card privacy during play** is not yet cryptographic: after the reveal step both peers can
  reconstruct the deck. The commutative-encryption deal on the roadmap closes this. Until then, treat
  networked play as honest-but-verifiable rather than privacy-preserving against the opponent.
- **On-chain broadcast** is not yet wired to a live node (the node is a separate project), so end-to-end
  settlement on a real network is pending even though the transactions are built, signed, and verified.

## Red-team / audit

Red-team hardening is performed **after** the game is functionally complete, not before. Findings and
their fixes (each with a positive and a hostile-negative test) will be tracked here as that phase runs.
