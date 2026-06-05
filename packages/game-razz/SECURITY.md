# `@bsv-poker/game-razz` — Security model

Razz (lowball Stud; highest up-card brings in — reversed from Stud). It is a **pure deterministic** game module: no network, no I/O, no time, no randomness.

## Why a pure module is security-relevant

The relay/indexer are never the source of truth, so this module's output IS the agreed outcome. Its
security obligations are therefore **determinism** (two clients converge byte-for-byte — P2) and
**correct settlement/legality** (a rules bug misallocates funds or accepts an illegal move).

## Trust boundary

- **Trusted:** the ruleset and the ordered, already-validated action list from `app-services`.
- **Untrusted within the boundary:** every action is still checked against `getLegalActions`; an
  illegal or out-of-turn move is rejected and cannot mutate state.
- **Recoverable errors:** illegal action → rejection (no mutation).
- **Fatal errors:** an impossible state (e.g. chip non-conservation in the shared pot engine) throws
  fail-loud rather than silently mis-awarding.
- **Side effects:** none — pure transitions; `serialize` yields the canonical state bytes.

## What breaks if violated

Introducing I/O/time/randomness breaks determinism (state fork). A dealing/legality/showdown bug
misdirects the pot. Settlement is cross-checked by the shared pot engine and the exhaustive-play suite.
