# `@bsv-poker/game-razz` — Invariants

Run: `node --test "packages/game-razz/test/**/*.test.ts"`

| ID | Claim | Proof |
|---|---|---|
| INV-game-razz-1 | Correct deal + legal-action progression for this variant. | `razz.test.ts`: `highest up-card (Ks, seat 1) brings in — reversed from stud (REQ-FSM-011 i)` |
| INV-game-razz-2 | A full line settles and **conserves chips** across all seat counts. | `tests/exhaustive-play.test.ts` (this variant) |
| INV-game-razz-3 | Two clients converge to byte-identical state (determinism, P2). | `tools/multiplayer-e2e.ts` / variant replay |
| INV-game-razz-4 (negative) | An illegal / out-of-turn action is rejected (fail-closed). | `sdk/test/adversarial.test.ts` + variant tests |

## To extend

A new rule adds a positive test (the legal line settles + conserves chips) and negative tests (illegal
moves rejected). Settlement must pass the exhaustive-play conservation check.
