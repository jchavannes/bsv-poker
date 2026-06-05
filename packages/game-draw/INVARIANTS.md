# `@bsv-poker/game-draw` — Invariants

Run: `node --test "packages/game-draw/test/**/*.test.ts"`

| ID | Claim | Proof |
|---|---|---|
| INV-game-draw-1 | Correct deal + legal-action progression for this variant. | `draw.test.ts`: `deals 5 concealed cards per seat; opens BET1` |
| INV-game-draw-2 | A full line settles and **conserves chips** across all seat counts. | `tests/exhaustive-play.test.ts` (this variant) |
| INV-game-draw-3 | Two clients converge to byte-identical state (determinism, P2). | `tools/multiplayer-e2e.ts` / variant replay |
| INV-game-draw-4 (negative) | An illegal / out-of-turn action is rejected (fail-closed). | `sdk/test/adversarial.test.ts` + variant tests |

## To extend

A new rule adds a positive test (the legal line settles + conserves chips) and negative tests (illegal
moves rejected). Settlement must pass the exhaustive-play conservation check.
