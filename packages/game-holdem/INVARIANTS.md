# `@bsv-poker/game-holdem` — Invariants

Run: `node --test "packages/game-holdem/test/**/*.test.ts"`

| ID | Claim | Proof |
|---|---|---|
| INV-game-holdem-1 | Correct deal + legal-action progression for this variant. | `holdem.test.ts`: `full heads-up hand to showdown: AA beats KK; pot settled, stacks updated` |
| INV-game-holdem-2 | A full line settles and **conserves chips** across all seat counts. | `tests/exhaustive-play.test.ts` (this variant) |
| INV-game-holdem-3 | Two clients converge to byte-identical state (determinism, P2). | `tools/multiplayer-e2e.ts` / variant replay |
| INV-game-holdem-4 (negative) | An illegal / out-of-turn action is rejected (fail-closed). | `sdk/test/adversarial.test.ts` + variant tests |

## To extend

A new rule adds a positive test (the legal line settles + conserves chips) and negative tests (illegal
moves rejected). Settlement must pass the exhaustive-play conservation check.
