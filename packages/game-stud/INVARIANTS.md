# `@bsv-poker/game-stud` — Invariants

Run: `node --test "packages/game-stud/test/**/*.test.ts"`

| ID | Claim | Proof |
|---|---|---|
| INV-game-stud-1 | Correct deal + legal-action progression for this variant. | `stud.test.ts`: `ante + bring-in posted; lowest up-card (2c, seat 0) brings in` |
| INV-game-stud-2 | A full line settles and **conserves chips** across all seat counts. | `tests/exhaustive-play.test.ts` (this variant) |
| INV-game-stud-3 | Two clients converge to byte-identical state (determinism, P2). | `tools/multiplayer-e2e.ts` / variant replay |
| INV-game-stud-4 (negative) | An illegal / out-of-turn action is rejected (fail-closed). | `sdk/test/adversarial.test.ts` + variant tests |

## To extend

A new rule adds a positive test (the legal line settles + conserves chips) and negative tests (illegal
moves rejected). Settlement must pass the exhaustive-play conservation check.
