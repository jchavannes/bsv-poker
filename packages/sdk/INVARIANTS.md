# `@bsv-poker/sdk` — Invariants

Run: `node --test "packages/sdk/test/**/*.test.ts"`

## Adversarial / fail-closed — `adversarial.test.ts`

| ID | Claim | Proof (test) |
|---|---|---|
| INV-SDK-1 | An out-of-turn action is rejected (fail-closed). | `out-of-turn action is rejected (fail-closed)` |
| INV-SDK-2 | An under-min raise is rejected inside the engine (REQ-POKER-008). | `under-min raise is rejected inside the engine (REQ-POKER-008)` |
| INV-SDK-3 | A stale/duplicate action for a seat not on the clock is rejected. | `stale/duplicate action for a seat not on the clock is rejected` |
| INV-SDK-4 | The timeout-default keeps the hand progressing (no freeze, P4). | `timeout-default applied keeps the hand progressing (no freeze, P4)` |
| INV-SDK-5 | A withheld/incorrect entropy reveal is detected by the commitment (recovery trigger). | `withheld/incorrect entropy reveal is detected by the commitment (recovery trigger, §4.1)` |
| INV-SDK-6 | Card substitution at reveal fails INSIDE the interpreter (REQ-CRYPTO-005). | `card-substitution at reveal fails INSIDE the interpreter (REQ-CRYPTO-005)` |
| INV-SDK-7 | A fair-play violation (mismatched key) forfeits — fails INSIDE the interpreter (THR-FAIR-2). | `fair-play violation (mismatched key) forfeits — fails INSIDE the interpreter (THR-FAIR-2)` |
| INV-SDK-8 | A replayed branch against a different state fails the binding (anti-replay, THR-PROTO-1). | `replayed branch against a different state fails the binding (anti-replay, THR-PROTO-1)` |
| INV-SDK-9 | All-in side-pot conservation across a 3-handed showdown (REQ-POKER-011). | `all-in side-pot conservation across a 3-handed showdown (REQ-POKER-011)` |
| INV-SDK-10 | `validateRuleset` accepts the Phase-1 ruleset and rejects bad ones. | `validateRuleset accepts the Phase-1 ruleset and rejects bad ones` |

## To extend

A new SDK entry point adds an adversarial test proving it fails closed on illegal/forged/replayed
input, and never routes a security-critical operation to a fake.
