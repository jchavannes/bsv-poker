# `@bsv-poker/sdk`

The assembled SDK surface: it wires the deterministic core (engine + game modules + hand-eval), the
crypto/chain layer (crypto-mentalpoker, script-templates, tx-builder, wallet-custody), and the
adapters into the API the desktop/web shells consume — composing them **without weakening any
boundary** and failing closed on adversarial use.

## WHAT / WHY

Exposes ruleset validation and the table/hand entry points. Security-critical operations route to the
REAL implementations (never fakes, REQ-DEP-004), and adversarial requests (out-of-turn, under-min,
stale, replayed, forged reveal) are rejected — many failing INSIDE the interpreter (P9). WHY/boundary:
[`SECURITY.md`](./SECURITY.md).

## Invariants

[`INVARIANTS.md`](./INVARIANTS.md) — the adversarial fail-closed suite mapped to tests; this is the
top-level "try to break the whole stack" entry point referenced by [`AUDIT_GUIDE.md`](../../AUDIT_GUIDE.md).

## Tests

```
node --test "packages/sdk/test/**/*.test.ts"
```
