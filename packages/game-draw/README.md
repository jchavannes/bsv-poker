# `@bsv-poker/game-draw`

Five-card Draw (concealed cards + a discard/draw round). A **pure** `GameModule` (no I/O, no time, no randomness) over the `@bsv-poker/engine`
framework — determinism is the security property (P2).

## WHAT / WHY

Implements `init` / `getLegalActions` / `apply` / `isTimeoutEligible` / `settle` / `serialize` for
this variant. It receives already signature- and structure-validated actions from `app-services` and
produces canonical state bytes; the engine owns pot conservation and legality (see
[`STATE_MACHINE.md`](../../STATE_MACHINE.md)).

## Security & invariants

- [`SECURITY.md`](./SECURITY.md) — why a pure module is security-relevant; trust boundary.
- [`INVARIANTS.md`](./INVARIANTS.md) — claims → tests.

## Tests

```
node --test "packages/game-draw/test/**/*.test.ts"
```
