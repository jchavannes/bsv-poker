# `@bsv-poker/ui-core` — Invariants

Run: `node --test "packages/ui-core/test/**/*.test.ts"`

| ID | Claim | Proof |
|---|---|---|
| INV-UI-1 | Card labels are colour-independent rank+suit words (REQ-APP-054). | `accessibility.test.ts`: `card labels are colour-independent rank+suit words (REQ-APP-054)` |
| INV-UI-2 | A human selects every action — no AI/default/bot selection in the UI path. | view-model tests (action dispatch is user-driven) |
| INV-UI-3 | No LOAD-BEARING state is persisted in localStorage/sessionStorage (REQ-UI-002). | web-interaction / storage tests (`tests/web-interaction-rules.test.ts`) |
| INV-UI-4 | No implicit form submit of a money action (explicit handlers only, REQ-UI-003). | `tests/web-interaction-rules.test.ts` |
| INV-UI-5 | The play-money/regtest/mainnet banner is surfaced, never hidden. | table view-model tests |

## To extend

A new control adds a test that the action is user-initiated, carries no load-bearing storage, and
surfaces any real-value warning. The UI never makes a gameplay/money decision for the human.
