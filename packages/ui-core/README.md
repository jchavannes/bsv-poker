# `@bsv-poker/ui-core`

View-models and components for the desktop/web shells. Menu-driven: **a human selects every action**
(no AI/bot/default gameplay or money decisions — bots are test-only). The UI is treated as a security
boundary, not "just UI".

## WHAT / WHY

Renders table/lobby/signing state and dispatches the human's explicit selections to `app-services`.
It must never (a) decide an action for the human, (b) keep load-bearing state in web storage, or
(c) auto-submit a money action. WHY each is enforced: [`SECURITY.md`](./SECURITY.md).

## Invariants

[`INVARIANTS.md`](./INVARIANTS.md) — human-control, accessibility, and no-load-bearing-storage claims
mapped to tests.

## Tests

```
node --test "packages/ui-core/test/**/*.test.ts"
```
