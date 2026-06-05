# `@bsv-poker/game-omaha` — Invariants

Run: `node --test "packages/game-omaha/test/**/*.test.ts"`

| ID | Claim | Proof |
|---|---|---|
| INV-game-omaha-1 | Correct deal + legal-action progression for this variant. | `omaha.test.ts`: `4 hole cards are dealt per seat (REQ-FSM-006 override i)` |
| INV-game-omaha-2 | A full line settles and **conserves chips** across all seat counts. | `tests/exhaustive-play.test.ts` (this variant) |
| INV-game-omaha-3 | Two clients converge to byte-identical state (determinism, P2). | `tools/multiplayer-e2e.ts` / variant replay |
| INV-game-omaha-4 (negative) | An illegal / out-of-turn action is rejected (fail-closed). | `sdk/test/adversarial.test.ts` + variant tests |

## To extend

A new rule adds a positive test (the legal line settles + conserves chips) and negative tests (illegal
moves rejected). Settlement must pass the exhaustive-play conservation check.
