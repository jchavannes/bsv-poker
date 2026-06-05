# State machine

The game engine is a **pure** finite-state machine: a function of its inputs with no I/O, no time, no
randomness (P2 / REQ-ARCH-002). This is what makes replay and cross-client agreement possible. Source:
`packages/engine/src/fsm.ts` and the per-variant `packages/game-*`.

## The `GameModule` contract (`fsm.ts`)

```
init(ruleset, seats)            -> state
getLegalActions(state, seat)    -> LegalActions     (the legal-move descriptor)
apply(state, action)            -> state'           (pure transition)
isTimeoutEligible(state, now)   -> { seat, defaultAction } | null   (the safe default move)
isHandComplete(state)           -> boolean
settle(state)                   -> Payouts
serialize(state)                -> bytes            (the canonical state bytes -> stateHash)
```

`enumerateActions` turns a `LegalActions` descriptor into the literal `Action[]`; `replay(module,
ruleset, seats, actions)` re-derives state from an ordered action list — the deterministic-core
driver used by reconnect/rebuild and dispute replay.

## Transition rules (per action)

For each betting action `fold` / `check` / `call` / `bet` / `raise` (and `discard` in Draw):

- **Precondition:** the seat is the one on the clock (`betting.toAct` or `drawToAct`); the action is
  in `getLegalActions(state, seat)`. An action that is not legal is rejected by `apply` — it cannot
  corrupt state.
- **Valid actor:** exactly the seat named by `toAct`. A signed envelope from another seat is rejected
  at the trust boundary before it reaches `apply` (`PROTOCOL.md`).
- **State mutation:** pure; produces a new state. The prior state hash is bound into the action's
  signature (`prev`) so the move is pinned to one transcript position.
- **On-chain:** a settlement/fallback transaction is produced at terminal states (`ONCHAIN_MODEL.md`).
- **Rejection:** an illegal or out-of-turn action is a no-op rejection; the transcript/indexer never
  stores an unauthenticated or illegal move (validating mode).

## Timeout default (the basis for accountable drop — partly OPEN)

`isTimeoutEligible(state, now)` already returns the **safe default** for the seat on the clock
(check-or-fold). The deterministic *application* of that default when a peer is absent — the
drop-and-continue — is the subject of audit finding 3. It is **OPEN** because doing it safely needs a
**shared anchored deadline** (both clients must drop at the same logical point or they fork the state).
The design (anchored block-height deadline + a signed timeout-claim applied as a replayable default
branch) is in `docs/audit-response-03.md`. Until then the live path fails closed: an absent player
aborts the hand and funds recover via the pre-signed refund graph.

## Convergence guarantee (P2)

Two honest clients applying the same valid action set reach **byte-identical** state — proven live by
`tools/multiplayer-e2e.ts` and `tools/validating-indexer-e2e.ts` (both players' `stateHash` match) and
by the reconnect/rebuild path (`tools/reconnect-e2e.ts`: rebuilt-from-transcript state == live state).
This is why the engine forbids I/O, time, and randomness: any of those would break determinism.
