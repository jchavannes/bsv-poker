# Script validation

How locking/unlocking scripts are executed and bounded. Source:
`packages/script-templates-ts/src/interpreter.ts` (+ `SECURITY.md` / `INVARIANTS.md` in that package).

## What runs

`evaluate(unlocking, locking, ctx)` runs the unlocking then the locking script on a shared stack
(legacy/Genesis evaluation) and returns `{ ok, reason? }`. The result is truthy iff the final stack
top is truthy. Signature opcodes use **real** secp256k1 ECDSA. Negative tests fail INSIDE the
interpreter (the P9 obligation), never in a wrapper guard.

## Genesis semantics

- `OP_CHECKLOCKTIMEVERIFY` / `OP_CHECKSEQUENCEVERIFY` → NO-OPs (timing is transaction-level).
- `OP_RETURN` → the script fails wherever it appears (commitments are `<data> OP_DROP`, not OP_RETURN).
- Script numbers are arbitrary precision (post-Genesis), decoded as BigInt — required for the 256-bit
  in-script EC fair-play proof.

## Resource bounds (the security of an attacker-authored script)

A spend's scripts are hostile. Every attacker-controlled loop and value is bounded BEFORE the work:

| Bound | Value | Closes |
|---|---|---|
| `MAX_STACK` | 1000 | OP_DUP / pushdata flood (memory) |
| `MAX_MULTISIG_KEYS` | 20 | the `OP_CHECKMULTISIG` unbounded-pop DoS — n and m are range-checked first |
| `MAX_SCRIPT_NUM_BYTES` | 4096 | bignum growth via repeated `OP_MUL` (CPU/memory) |

No script throws out of `evaluate`; underflow / unbalanced IF / unsupported opcode / out-of-range
counts / oversize numbers all become `{ ok:false }`. Proven by `INV-INT-1..6` (incl. a 100k fuzz).

## Templates validated this way

`funding` (N-of-N CHECKMULTISIG), `revealOrTimeout`, `fold`, `settlement`, and `fairPlay`
(`IF claim / ELSE refund`). Each binds the anti-replay `BranchBinding` prefix. Positive and negative
spends are in `templates.test.ts`; the in-script EC fair-play (256-bit) is in `shuffle-key.test.ts`.

## Boundary

trusted: the sighash preimage (our own). untrusted: every opcode/pushdata. recoverable: every script
failure. fatal: none. side effects: none beyond the local stack. See the package `SECURITY.md`.

## TRACKED ASSUMPTION

This is the template-subset interpreter, not the full node consensus interpreter. A production swap
to the embedded node's interpreter re-runs these template tests unchanged.
