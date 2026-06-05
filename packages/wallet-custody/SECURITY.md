# `@bsv-poker/wallet-custody` — Security model

Holds ONE long-term secp256k1 key per player and derives per-game/per-card spend scalars from it. The
device stores one secret; everything else is derived (REQ-WALLET-001/002). Source: `src/custody.ts`.
Background: [`KEY_LIFECYCLE.md`](../../KEY_LIFECYCLE.md), [`SPEND_KEY_DERIVATION.md`](../../SPEND_KEY_DERIVATION.md).

## Attacker model

A caller (incl. a compromised UI) trying to extract a private scalar, or to make two spends share a
key (linking them / stranding funds), or to sign something other than the intended spend.

## Defences

| Threat | Defence |
|---|---|
| Scalar exfiltration to the UI | the `Custody` contract returns **public** keys only (`derive` → compressed pubkey hex); scalars never cross to the viewer path (REQ-APP-025). |
| Linked spends / address reuse | per-(gid, j, role) HKDF-SHA256 derivation with full domain separation; each context yields a distinct key. |
| Invalid scalar | `deriveScalar` rejection-samples into `[1, n-1]`; throws after a bounded number of tries (never returns an out-of-range scalar). |
| Wrong combined scalar | Mode A `reconstructAndSign` sums the disclosed scalars and **rejects a zero** combined scalar. |
| Claiming Mode B under Mode A | the software backend refuses Mode B operations it cannot honestly perform. |
| High-S signature | signing produces LOW-S DER (node rejects high-S). |

## Trust boundary

- **Trusted:** the master key (held in-process), the caller's `SignIntent.sighashPreimage`.
- **Untrusted:** the derivation context fields are validated; a 32-byte scalar length is enforced
  (`scalarToPrivateKey` throws otherwise).
- **Recoverable errors:** invalid scalar length, zero combined scalar, exhausted derivation — all
  throw and are surfaced; no silent wrong key.
- **Side effects:** none beyond reading the in-process master and Node `crypto`.

## What must never be assumed / breaks if violated

- Never expose a scalar to the UI — it is a spend key.
- Never reuse a derivation `info` (gid/j/role) — reuse links spends and can strand funds.
- Never sign without a full `SignIntent` (the sighash preimage is what binds the signature to a tx).
