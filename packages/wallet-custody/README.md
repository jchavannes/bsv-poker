# `@bsv-poker/wallet-custody`

One long-term secp256k1 key per player; deterministic per-game/per-card spend scalars derived from it
via HKDF-SHA256. The device stores one secret, derivation is deterministic and auditable, and old-game
keys reveal nothing (REQ-WALLET-001/002).

## WHAT / HOW / WHY

- `createSoftwareCustody(masterKey)` → a `Custody` that exposes `derive(gid, j, role)` (returns the
  **public** key only) and `reconstructAndSign(scalars, intent)` (Mode A: sum disclosed scalars,
  reject zero, sign the combined-key spend with LOW-S DER).
- `scalarToPrivateKey(d)` builds a secp256k1 key from a 32-byte scalar (length-checked).
- WHY one stored key + domain-separated derivation, and why scalars never reach the UI:
  [`KEY_LIFECYCLE.md`](../../KEY_LIFECYCLE.md) and [`SPEND_KEY_DERIVATION.md`](../../SPEND_KEY_DERIVATION.md).

## Security & invariants

- [`SECURITY.md`](./SECURITY.md) — attacker model, defences, trust boundary.
- [`INVARIANTS.md`](./INVARIANTS.md) — claims → tests.

## Tests

```
node --test "packages/wallet-custody/test/**/*.test.ts"
```
