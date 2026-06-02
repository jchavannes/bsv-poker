/**
 * Transaction classes — core §6.1. These are conceptual classes (wire names are
 * implementation detail). Every transaction binds gid + rulesetHash + round + state hash +
 * successor commitment (core §6.3, REQ-TX-005) for anti-replay.
 */

export const TX_CLASSES = [
  'Funding',
  'Commitment',
  'Deal',
  'Action',
  'Timeout',
  'Reveal',
  'Fold',
  'FairPlay',
  'Settlement',
  'Recovery',
  'TableMgmt',
] as const;
export type TxClass = (typeof TX_CLASSES)[number];

/**
 * The anti-replay binding carried by every protocol transaction (core §6.3).
 * Carried as pushdata in a live script — NEVER OP_RETURN (core P11/§6.5).
 */
export interface BranchBinding {
  readonly gid: string; // hex
  readonly rulesetHash: string; // hex
  readonly round: number;
  readonly stateHash: string; // hex — hash of the state being spent
  readonly actingSeat: number; // -1 where not applicable
  readonly successorCommitment: string; // hex — commitment to the successor state
}

/** A protocol transaction as the engine consumes it (the on-chain bytes live in tx-builder). */
export interface ProtocolTx {
  readonly txid: string; // hex
  readonly cls: TxClass;
  readonly binding: BranchBinding;
  /** Class-specific payload, already validated/normalised by the SDK. */
  readonly payload: Readonly<Record<string, unknown>>;
}

/**
 * A transcript is the ordered set of valid table transactions plus the commit/reveal material
 * needed to re-derive state (core §12.2, REQ-DATA-002).
 */
export interface Transcript {
  readonly rulesetHash: string;
  readonly txs: readonly ProtocolTx[];
}
