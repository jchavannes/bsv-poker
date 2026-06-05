# `@bsv-poker/engine` — Security model

The pure game-FSM framework: betting rules, pot construction/award, legal-action enumeration, replay,
and the timeout-default. Its security role is **determinism and correct settlement** — the relay/
indexer are never the source of truth, so the engine's output IS the truth. Source: `src/`.
Background: [`STATE_MACHINE.md`](../../STATE_MACHINE.md).

## Why this is security-critical (it has no network, but it owns the money)

- **Determinism (P2):** the engine is a pure function — no I/O, no time, no randomness. Any of those
  would let two honest clients diverge (fork the agreed state). This purity is the security property.
- **Settlement correctness:** `pots` must conserve chips exactly and award main/side pots correctly,
  including all-ins, folded contributors, and odd-chip splits. A bug here misdirects funds.
- **Legality:** `legalActions`/`applyAction` define exactly which moves are legal; an out-of-turn or
  illegal action is rejected, so it cannot corrupt state even if it passes the network layer.

## Trust boundary

- **Trusted:** the ruleset and the ordered action list it is asked to apply (already signature- and
  structure-validated upstream in `app-services`).
- **Untrusted within the boundary:** an action is still checked for legality before it mutates state.
- **Recoverable errors:** an illegal/out-of-turn action is rejected (no mutation).
- **Fatal errors:** a chip-conservation violation is an internal invariant breach — it throws rather
  than silently award wrong amounts (fail-loud on an impossible state).
- **Side effects:** none. Pure transitions; `serialize` produces the canonical state bytes.

## Timeout default (basis for the OPEN audit-3 work)

`isTimeoutEligible(state, now)` returns the safe default (check-or-fold). Applying it deterministically
when a peer is absent needs a shared anchored deadline (see `STATE_MACHINE.md` / `FAILURE_MODES.md`);
that drop-and-continue is OPEN and must not use a local clock (it would fork state).

## What breaks if violated

Introducing time/IO/randomness into the engine breaks determinism (state fork). A pot-math error
misallocates funds. An accepted illegal action corrupts the agreed state.
