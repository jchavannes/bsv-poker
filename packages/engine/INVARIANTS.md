# `@bsv-poker/engine` — Invariants

Run: `node --test "packages/engine/test/**/*.test.ts"`

## Betting legality — `betting.test.ts`

| ID | Claim | Proof (test) |
|---|---|---|
| INV-ENG-1 | bet→call closes a heads-up round. | `NL: bet → call closes a heads-up round` |
| INV-ENG-2 | A re-raise reopens action to the original bettor. | `NL: re-raise reopens action to the original bettor` |
| INV-ENG-3 | Check-through closes a round with no bet. | `NL: check-through closes round (no bet)` |
| INV-ENG-4 | A short all-in does NOT reopen the raise to a player who already acted (REQ-POKER-010). | `REQ-POKER-010: short all-in does NOT reopen the raise to a player who already acted` |
| INV-ENG-5 | Min-raise legality uses the last full raise. | `NL: min-raise legality uses last full raise` |
| INV-ENG-6 | PL/FL sizing rules are enforced. | `PL: pot-limit max raise is pot + call`, `FL: fixed bet/raise sizes and raise cap` |

## Pot construction / award + conservation — `pots.test.ts`

| ID | Claim | Proof (test) |
|---|---|---|
| INV-ENG-7 | Layered all-ins build correct main/side pots (§19.B worked examples). | `§19.B worked example…`, `§19.B award with C>B>A…` |
| INV-ENG-8 | A folded-but-contributing player sits in a pot they cannot win. | `folded-but-contributing player sits in a pot they cannot win` |
| INV-ENG-9 | Odd-chip splits go left-of-button deterministically (REQ-POKER-013). | `odd-chip split goes left-of-button deterministically (REQ-POKER-013)` |
| INV-ENG-10 | Chip conservation holds on valid input; an impossible state throws (fail-loud). | `conservation assertion throws on impossible input is not triggered by valid input` |

## Determinism (system-level)

| ID | Claim | Proof |
|---|---|---|
| INV-ENG-11 | The engine is pure (no I/O/time/randomness) → two clients converge byte-for-byte. | `tools/multiplayer-e2e.ts`, exhaustive-play suites |

## To extend

A new rule/variant adds positive tests (the legal lines settle and conserve chips) and negative tests
(illegal/out-of-turn moves rejected; impossible pot states throw).
