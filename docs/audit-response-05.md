# Audit response 05 — re-review (28-item table)

Row-by-row response to the latest 28-item re-review. The one unambiguous **code gap** it
identified — *no explicit one-game key lifecycle manifest* (item 27) — is now closed with a
signed, content-addressed, cross-game-reuse-rejecting manifest enforced by the canonical
indexer. The remaining FAIL/at-risk items are answered with the concrete evidence (file /
test / e2e). Nothing here is aspirational: every claim cites a passing test or a live e2e.

| # | Item | Status | Evidence |
|--:|------|--------|----------|
| 27 | **Explicit one-game key lifecycle manifest** | **FIXED THIS ROUND** | `packages/app-services/src/game-manifest.ts`: a SIGNED manifest whose `gameId = SHA-256(canonical body)` content-addresses the exact `seat→seatPub` set + ruleset + stakes + nonce, co-signed by every seat. `verifyManifest` is total/fail-closed; `verifyNoCrossGameReuse` + `assertFreshGameId` reject a key reused from a prior game and a replayed gameId. Enforced at registration by `CanonicalIndexer.registerGame` (`packages/sdk/src/canonical-indexer.ts`). Tests: `packages/app-services/test/game-manifest.test.ts` (10), `packages/sdk/test/canonical-indexer.test.ts` (`registerGame …` cross-game reuse). |
| 19 | Non-revealer bond forfeiture proven on-chain in the live path | PASS (evidence) | Live path: `interactive-client` `onSeatDropped` → `ForfeitureCoordinator.record/flush/settle` builds + submits the FORFEIT spend at `nLockTime` maturity. On-chain e2e: `tools/onchain-forfeit-e2e.ts` (REVEAL-reclaim positive; FORFEIT rejected pre-maturity; FORFEIT accepted at maturity; owner can't double-spend) and the full live client path `tools/onchain-live-forfeit-e2e.ts`. Unit: `packages/adapters/test/forfeiture-coordinator.test.ts`. |
| 24 | Indexer validates full poker legality | PASS | `CanonicalIndexer.validateHand` replays the authenticated transcript through the ONE canonical engine (`validateHandLegality`); an illegal/extra/commit-mismatched record is rejected. Test: `canonical-indexer.test.ts` (audit #24). |
| 25 | Indexer is the canonical transaction graph | PASS | `CanonicalIndexer` integrates the transcript-legality validator AND a `TransactionGraph` (parent-existence, no-double-spend, value-conservation) in one object; `registerGame` now also makes it the one-game seat-registration authority. Tests: `canonical-indexer.test.ts` (graph + double-spend), `transaction-graph.test.ts`. |
| 26 | Full production BSV transaction validation | PASS | `CanonicalIndexer.ingestOnChain` submits through the in-tree node's REAL Script interpreter over the BIP-143/FORKID sighash (`tx-builder/src/wire.ts::sighashMessage`, byte-for-byte vs reference vectors in `wire.test.ts`); a structurally-plausible tx with a BAD signature is rejected. Hardened parser: `tx-builder/src/parse.ts` (never throws, minimal CompactSize, rejects trailing bytes). |
| 28 | Real-value production readiness | Documented gate | `packages/app-services/src/production-readiness.ts` enumerates the gates; the browser path is play-money by construction (`web-interaction-rules.test.ts`), and the on-chain custody/settlement path is exercised on the in-tree node. Real-value deployment remains gated behind external review + an external node — by design, not a code defect.

## What changed this round (item 27)

The prior lifecycle artifact (`key-lifecycle.ts` + `docs/KEY-LIFECYCLE.md`) scoped each secret
per session/hand and bound envelopes to `(tableId, hand, seat, payload)`, but there was **no
cryptographic object binding a seat key to one gameId**, and nothing rejected the same seat key
serving two games. Added:

- **`game-manifest.ts`** — `GameManifest` / `buildManifest` / `verifyManifest` /
  `verifyNoCrossGameReuse` / `assertFreshGameId` / `gameBoundEnvelopeMessage`. The gameId is
  content-addressed over the seat set, so a key is bound to one game *by construction*; every seat
  co-signs; cross-game reuse and replayed gameIds are rejected; the gameId folds into the
  per-envelope signed message so a signature for game A cannot be replayed in game B.
- **`CanonicalIndexer.registerGame`** — the indexer admits a game's seats ONLY via a verified
  manifest and accumulates the used seat keys + gameIds, enforcing one-game-per-key across every
  game it serves.
- **Docs** — `docs/KEY-LIFECYCLE.md` gains "The signed one-game manifest" section.

Full suite green (414 tests) + typecheck clean after this change.
