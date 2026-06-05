# `@bsv-poker/hand-eval` — Invariants

Run: `node --test "packages/hand-eval/test/**/*.test.ts"`

| ID | Claim | Proof |
|---|---|---|
| INV-HE-1 | High-hand categories reproduce the reference oracle bit-for-bit (§19.D). | `vectors.test.ts`: `§19.D high-hand category vectors reproduce the oracle bit-for-bit` |
| INV-HE-2 | Ranking is deterministic and total (every comparison resolves consistently). | `vectors.test.ts` (property over generated hands; seeded LCG, no Math.random) |
| INV-HE-3 | Lowball / Hi-Lo ordering is correct where the variant uses it (Razz, Omaha-8). | `vectors.test.ts` low/hi-lo vectors |
| INV-HE-4 | The chosen winner conserves and correctly awards the pot end-to-end. | `tests/exhaustive-play.test.ts` (settlement + conservation) |

## To extend

A new category/tie-break adds vectors that reproduce the oracle and a settlement check that the
correct hand wins the pot. Ranking must stay pure and deterministic (no I/O/time/randomness).
