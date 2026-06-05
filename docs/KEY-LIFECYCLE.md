# Key & entropy lifecycle manifest (audit #40, #33)

> This prose manifest is mirrored as a discoverable **code** artifact:
> `packages/app-services/src/key-lifecycle.ts` exports the real `perHandEntropy(...)` derivation the
> live client uses and a machine-readable `KEY_LIFECYCLE` array (one entry per secret: origin,
> derivation, scope, no-reuse guarantee). Tests in `key-lifecycle.test.ts` exercise both.


Explicit, repository-wide statement of every key and entropy value a player uses, where it comes
from, how long it lives, and the no-reuse boundaries. This is the "one-game key lifecycle" manifest:
keys and randomness are scoped to a session/game and a hand — never reused across games.

## The values

| Value | Type | Derivation | Scope / lifetime | Where |
|---|---|---|---|---|
| **session root** | 32 random bytes | `crypto.getRandomValues` (CSPRNG) — fresh each launch/session | one session (process/tab) | `apps/client-web/src/app.ts` (`seed`), bot/SDK callers |
| **seat session key** | Ed25519 keypair | `sessionAuthFromSeed(deriveSeatSeed(root, "bsv-poker/seat-ed25519"))` where `deriveSeatSeed = SHA-256(label ‖ root)` | one session — signs every relay envelope; the public key IS the seat identity used for seating + signature checks | `packages/app-services/src/session-auth.ts` |
| **table entropy** | 32 random bytes | `crypto.getRandomValues` — fresh each table join | one table session | `apps/client-web/src/app.ts` (`entropy`), `NetworkTable` |
| **per-hand entropy** | 32 bytes | `SHA-256(table_entropy ‖ uint32_le(handIndex))` (the code's `concat(entropy, u32(hand))`; `ByteWriter.u32` is little-endian) | one hand — the player's secret contribution to that hand's N-party shuffle | `interactive-client.ts` `playSession` (REQ-CRYPTO-010) |
| **per-hand reveal/commit** | derived | `commit = SHA-256(per-hand entropy)`, revealed after all commit | one hand (commit→reveal handshake) | `interactive-client.ts` |

## The guarantees (no reuse across games)

1. **Fresh root per session.** The session root is drawn from the CSPRNG at launch and never persisted
   as load-bearing state (REQ-UI-002 / REQ-APP-042 — only a play-money balance may sit in
   localStorage; keys/seeds/transcripts must not). A new session ⇒ a new seat key.
2. **Fresh entropy per table, fresh contribution per hand.** Table entropy is CSPRNG per join; each
   hand's contribution is `H(table_entropy ‖ handIndex)`, so every hand has a distinct, unpredictable
   secret permutation input even within one table session (REQ-CRYPTO-010 — no entropy is reused
   across hands).
3. **Domain separation.** Key derivation is labelled (`"bsv-poker/seat-ed25519"`) and hand entropy is
   bound to the hand index, so the seat key and the shuffle entropy are in disjoint domains and a
   value from one can never collide with the other.
4. **Signature binding.** Every envelope a seat signs binds `(tableId, type, seat, hand, payload,
   prev, deadline/height)` (`envelopeMessage`), so a signature is valid only for the exact
   table/hand/seat/state it was produced for — a key cannot be replayed onto another game, hand, or
   state (and a peer action is additionally bound to the agreed prior state, audit #12).

## What is NOT here (by design)

- **No long-lived identity key in the browser path.** The browser play-money client uses only the
  per-session seat key above; durable on-chain custody keys live behind the `Custody` boundary on the
  Node/SDK path (§A8, §A2.3) and never enter the browser bundle.
- **No key persists to disk in the browser.** Load-bearing key material is never written to
  localStorage/sessionStorage (enforced by `tests/web-interaction-rules.test.ts`).

The executable claims of this manifest are tested in
`packages/app-services/test/key-lifecycle.test.ts` (distinct keys per session root; distinct,
deterministic per-hand entropy bound to the hand index; domain separation).
