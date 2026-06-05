# `@bsv-poker/engine`

The pure game-FSM framework: betting legality, pot construction/award, legal-action enumeration,
deterministic `replay`, and the timeout-default descriptor. No I/O, no time, no randomness — purity is
the security property that makes cross-client agreement (P2) possible.

## WHAT

- `legalActions` / `applyAction` / `isRoundClosed` / `openRound` — the betting state machine.
- `GameModule` contract + `enumerateActions` + `replay` — the per-variant interface and the
  deterministic-core driver.
- pot construction/award with exact chip conservation (main/side pots, all-ins, odd-chip splits).
- `isTimeoutEligible` — the safe default move for the seat on the clock.

## WHY / boundary

See [`SECURITY.md`](./SECURITY.md) (why a network-less package owns the money) and the system-level
[`STATE_MACHINE.md`](../../STATE_MACHINE.md). Invariants → tests: [`INVARIANTS.md`](./INVARIANTS.md).

## Tests

```
node --test "packages/engine/test/**/*.test.ts"
```
