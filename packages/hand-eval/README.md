# `@bsv-poker/hand-eval`

Pure hand ranking and showdown evaluation (high, lowball, Hi-Lo). It decides the pot winner, so its
correctness is fund safety, and it reproduces a fixed reference oracle bit-for-bit so every client
ranks identically (P2).

## WHAT / WHY

Ranks the documented hand sizes per variant and produces a total ordering used at showdown. Pure
(no I/O/time/randomness). WHY it is security-critical despite no network: [`SECURITY.md`](./SECURITY.md).

## Invariants

[`INVARIANTS.md`](./INVARIANTS.md) — claims → tests (the §19.D oracle vectors and the exhaustive-play
settlement checks).

## Tests

```
node --test "packages/hand-eval/test/**/*.test.ts"
```
