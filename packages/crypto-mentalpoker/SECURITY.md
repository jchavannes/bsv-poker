# `@bsv-poker/crypto-mentalpoker` — Security model

The SECURITY-CRITICAL mental-poker crypto: commit/reveal, the secret-permutation shuffle, and the
real secp256k1 combined per-card keys. This path is NEVER exercised against a fake (REQ-DEP-004).
Source: `src/realct.ts`. Background: [`CARD_AND_DECK_MODEL.md`](../../CARD_AND_DECK_MODEL.md).

## Attacker model

A player who wants to (a) fix or bias the deck order, (b) choose their entropy after seeing others'
(late-entropy), (c) learn a card before reveal, or (d) forge a reveal that doesn't match its commit.

## Defences

| Threat | Defence |
|---|---|
| One party fixes the order | order = composition of EVERY party's secret permutation; depends on all entropies (INV-CT-1). |
| Late-entropy selection | commit `SHA-256(entropy)` is published before reveal; a party is bound to its committed entropy. |
| Modulo-biased shuffle | unbiased Fisher–Yates with rejection sampling; the rejection loop is **bounded** (fail-closed at `MAX_REJECTION_DRAWS`, a `<2^-128` false-fail). |
| Timing oracle on reveal/commit match | constant-time hex comparison (`constantTimeEqualHex`, CWE-208/697). |
| Invalid combined-key scalar | `combinedKey` re-hashes with a salt until a valid secp256k1 scalar; bounded loop, throws if exhausted. |

## Trust boundary

- **Trusted:** the local party's own entropy (from the CSPRNG).
- **Untrusted:** every other party's commit/reveal and pubkey — verified before use.
- **Recoverable errors:** a reveal that doesn't match its commit (caught by the verify), an entropy/
  pubkey count mismatch (throws, surfaced), an exhausted derivation (throws).
- **Fatal errors:** none on hostile reveal input (the comparison simply returns false).
- **Side effects:** none — pure functions over byte inputs; uses Node `crypto` for SHA-256/ECDH/HMAC.

## What must never be assumed / breaks if violated

- Never accept a reveal without the constant-time commit check — an unchecked reveal lets a party
  substitute entropy and bias the order.
- Never replace the rejection sampler with a plain modulo — that reintroduces shuffle bias.
- Never run this path against a fake adapter — security-critical crypto must be the real thing.
