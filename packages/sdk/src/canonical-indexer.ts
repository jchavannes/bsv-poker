/**
 * Canonical validating indexer (audit findings #24 + #25). The transport indexer (relay-go) is a mere
 * authenticated projection (P3, ADR 0005); THIS is the indexer-side component that makes the served
 * truth both LEGAL and CANONICAL by integrating the two validators into one object:
 *
 *   - POKER LEGALITY (#24): `validateHand` replays the authenticated transcript records through the ONE
 *     canonical engine and returns a verdict, rejecting any illegal action, forged/extra record, or
 *     commit-mismatch (no second poker engine).
 *   - CANONICAL TRANSACTION GRAPH (#25): `addTransaction` feeds the on-chain funding/settlement txs into
 *     a `TransactionGraph` that enforces parent-existence, no-double-spend and value-conservation, and
 *     exposes the authoritative UTXO set — the truth a settlement rests on, validated.
 *
 * A node/operator runs this over the authenticated record stream + the on-chain transactions to serve
 * a transcript that is provably legal AND a transaction graph that is provably consistent. It rebuilds
 * independently from the same inputs (P2) and matches what the node accepted.
 */
import { validateHandLegality, rebuildHand, type LegalityVerdict, type TxRecord } from '@bsv-poker/app-services';
import { verifyManifest, verifyNoCrossGameReuse, assertFreshGameId, manifestSeatPubs, type GameManifest } from '@bsv-poker/app-services';
import { TransactionGraph, type AddResult, type GraphOutput } from '@bsv-poker/adapters/transaction-graph';
import type { Ruleset, GameState } from '@bsv-poker/protocol-types';
import type { TablePlayer } from '@bsv-poker/app-services';

export interface GameRegistration { readonly ok: boolean; readonly reason: string; readonly gameId?: string }

export class CanonicalIndexer {
  private readonly graph = new TransactionGraph();
  // one-game key lifecycle (audit #27): the seat keys + gameIds this indexer has ever
  // admitted, so a key may serve at most ONE game and a gameId is never replayed.
  private readonly usedSeatPubs = new Set<string>();
  private readonly seenGameIds = new Set<string>();

  // ---- one-game key lifecycle manifest (#27) ----

  /**
   * Register a game from its SIGNED one-game manifest. The seats for a game are admitted
   * ONLY via a manifest that (a) verifies (content-addressed gameId + every seat's
   * signature), (b) reuses NO seat key from a prior game, and (c) has a fresh, non-replayed
   * gameId. On success the seat keys + gameId are recorded so the one-game lifecycle is
   * enforced across every game this indexer serves — a seat key is bound to exactly one game.
   */
  async registerGame(manifest: GameManifest): Promise<GameRegistration> {
    const v = await verifyManifest(manifest);
    if (!v.ok) return { ok: false, reason: `manifest invalid: ${v.reason}` };
    const reuse = verifyNoCrossGameReuse(manifest, this.usedSeatPubs);
    if (!reuse.ok) return { ok: false, reason: reuse.reason };
    const fresh = assertFreshGameId(manifest, this.seenGameIds);
    if (!fresh.ok) return { ok: false, reason: fresh.reason };
    for (const pub of manifestSeatPubs(manifest)) this.usedSeatPubs.add(pub);
    this.seenGameIds.add(manifest.gameId);
    return { ok: true, reason: v.reason, gameId: manifest.gameId };
  }

  /** True iff a seat key has already served a game (so it must never be reused). */
  isSeatKeyUsed(seatPub: string): boolean {
    return this.usedSeatPubs.has(seatPub);
  }

  // ---- canonical transaction graph (#25) ----

  /** Register a pre-existing outpoint (e.g. a mined coinbase) the on-chain graph may spend from. */
  addRoot(txid: string, vout: number, satoshis: bigint): void {
    this.graph.addRoot(txid, vout, satoshis);
  }

  /** Ingest an on-chain transaction into the canonical graph (structural validation before commit). */
  addTransaction(rawHex: string): AddResult {
    return this.graph.add(rawHex);
  }

  /**
   * FULL production BSV validation (audit #26): validate a transaction through the node's REAL
   * consensus engine — the Script interpreter (every input's unlocking script vs the spent output's
   * locking script over the BIP-143/FORKID sighash), nLockTime finality, value conservation and
   * double-spend — AND, on acceptance, fold it into the canonical structural graph. A
   * structurally-plausible tx with an INVALID signature is rejected here (the node runs the
   * interpreter), unlike the structure-only `addTransaction`. Returns the verdict.
   */
  async ingestOnChain(
    rawHex: string,
    node: { submitTx(raw: string): Promise<{ ok: boolean; reason?: string }> },
  ): Promise<{ validated: boolean; reason?: string }> {
    const r = await node.submitTx(rawHex); // the node runs the FULL interpreter + consensus checks
    if (!r.ok) return { validated: false, ...(r.reason !== undefined ? { reason: r.reason } : {}) };
    const g = this.graph.add(rawHex);
    return g.ok ? { validated: true } : { validated: false, reason: g.reason };
  }

  /** The authoritative UTXO set the validated transaction graph represents. */
  utxos(): GraphOutput[] {
    return this.graph.utxos();
  }

  /** Is this outpoint unspent in the canonical graph? */
  isUnspent(txid: string, vout: number): boolean {
    return this.graph.isUnspent(txid, vout);
  }

  // ---- poker legality over the authenticated transcript (#24) ----

  /** Validate that the authenticated transcript records form a LEGAL hand through the canonical engine. */
  validateHand(records: readonly TxRecord[], ruleset: Ruleset, seats: readonly TablePlayer[], handNo = 0, buttonIndex = 0): LegalityVerdict {
    return validateHandLegality(records, ruleset, seats, handNo, buttonIndex);
  }

  /** Rebuild the (legality-validated) state of a hand from the transcript (P2 deterministic replay). */
  rebuildHand(records: readonly TxRecord[], ruleset: Ruleset, seats: readonly TablePlayer[], handNo = 0, buttonIndex = 0): { state: GameState; stateHash: string } {
    return rebuildHand(records, ruleset, seats, handNo, buttonIndex);
  }

  /**
   * Serve the canonical view for a hand: the legality verdict AND the validated UTXO set. A served
   * transcript is admitted only when it is legality-valid (a node would refuse to serve an illegal one).
   */
  canonicalView(records: readonly TxRecord[], ruleset: Ruleset, seats: readonly TablePlayer[], handNo = 0, buttonIndex = 0): {
    legality: LegalityVerdict;
    utxos: GraphOutput[];
  } {
    return { legality: this.validateHand(records, ruleset, seats, handNo, buttonIndex), utxos: this.utxos() };
  }
}
