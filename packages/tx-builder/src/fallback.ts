/**
 * Pre-signed fallback graph (core §6.4 / §15.5, REQ-TX-008, P4 — no table is ever frozen by an
 * absent player). BEFORE play, the N contributors to a funded pot co-sign a **timeout-default**
 * recovery spend that returns each stake. It carries a LOW (non-final) nSequence so a later
 * **cooperative** settlement (higher sequence, up to 0xffffffff final) supersedes it under the
 * original-replacement rule (REQ-TX-002) — demonstrated on-chain in tools/onchain-recovery-e2e.ts.
 * The refund branch uses NO in-script CLTV/CSV (REQ-TX-001); maturity is transaction-level.
 */

import type { BranchBinding } from '@bsv-poker/protocol-types';
import { type Script, fundingUnlocking } from '@bsv-poker/script-templates-ts';
import { type Tx, type TxOutput, buildSettlement } from './txbuilder.ts';
import { sighashMessage } from './wire.ts';

/** The funded outpoint being recovered (its value + funding locking script for the sighash). */
export interface FundingRef {
  readonly txid: string;
  readonly vout: number;
  readonly value: number;
  readonly scriptCode: Script;
}

/** A contributor to the pot and the stake to return to them on timeout. */
export interface Contributor {
  readonly pub: Uint8Array;
  readonly amount: number;
}

/** Produces a script-ready signature for contributor `index` over the sighash message. */
export type Signer = (index: number, sighashMessage: Uint8Array) => Uint8Array;

export interface PresignedSpend {
  readonly kind: 'timeout-refund';
  readonly tx: Tx;
  readonly scriptSig: Script;
  readonly sighash: Uint8Array;
}

/** Refund outputs returning each contributor's stake, after subtracting a flat `fee` from the pot. */
export function refundOutputs(b: BranchBinding, contributors: readonly Contributor[], fee: number): TxOutput[] {
  const total = contributors.reduce((s, c) => s + c.amount, 0);
  if (total <= fee) throw new Error('fee exceeds pot');
  const payable = total - fee;
  // Proportional shares for contributors 1..n; the first contributor absorbs the rounding
  // remainder so the outputs sum to exactly `payable` (value-conserving).
  const tail = contributors.slice(1).map((c) => Math.floor((c.amount / total) * payable));
  const first = payable - tail.reduce((s, v) => s + v, 0);
  return contributors.map((c, i) => buildSettlement(b, c.pub, i === 0 ? first : tail[i - 1]!));
}

/** Build the timeout-default refund transaction (low, non-final sequence). */
export function buildTimeoutRefund(
  b: BranchBinding,
  funding: FundingRef,
  contributors: readonly Contributor[],
  opts: { fee?: number; sequence?: number; nLockTime?: number } = {},
): Tx {
  const fee = opts.fee ?? 0;
  const sequence = opts.sequence ?? 1; // replaceable: a cooperative spend with higher seq supersedes
  return {
    version: 1,
    inputs: [{ prevTxid: funding.txid, vout: funding.vout, sequence }],
    outputs: refundOutputs(b, contributors, fee),
    nLockTime: opts.nLockTime ?? 0,
  };
}

/**
 * Pre-sign the fallback graph: every contributor signs the timeout-default refund over the funding
 * (N-of-N CHECKMULTISIG) sighash, yielding a fully-assembled spend ready to broadcast if the table
 * stalls. The signatures are in contributor order (the order CHECKMULTISIG verifies).
 */
export function presignFallbackGraph(
  b: BranchBinding,
  funding: FundingRef,
  contributors: readonly Contributor[],
  signers: readonly Signer[],
  opts: { fee?: number; sequence?: number; nLockTime?: number } = {},
): PresignedSpend {
  if (signers.length !== contributors.length) throw new Error('one signer per contributor required');
  const tx = buildTimeoutRefund(b, funding, contributors, opts);
  const sighash = sighashMessage(tx, 0, funding.scriptCode, funding.value);
  const sigs = signers.map((sign, i) => sign(i, sighash));
  return { kind: 'timeout-refund', tx, scriptSig: fundingUnlocking(sigs), sighash };
}

/** A fully-signed, time-locked recovery every contributor holds BEFORE the pot is funded. */
export interface NlocktimeRecovery {
  readonly kind: 'nlocktime-recovery';
  /** The recovery transaction: spends the funded pot back to every contributor, time-locked. */
  readonly tx: Tx;
  /** The N-of-N unlock (every contributor's signature) — so ANY single holder can broadcast it alone. */
  readonly scriptSig: Script;
  readonly sighash: Uint8Array;
  /** The block height at/after which the node will admit this recovery (the unilateral exit opens). */
  readonly recoverableAtHeight: number;
}

/**
 * ABSOLUTE / life-critical (see memory `always-nlocktime-recovery-all-funds`): produce the
 * **guaranteed unilateral nLockTime recovery** that EVERY player must hold BEFORE risking a single
 * sat. It spends the N-of-N funded pot back to each contributor (100% of stake, minus only an
 * explicit miner `fee` that defaults to 0), with:
 *   - a FUTURE `nLockTime` (height) — the node rejects it as non-final until that height, so it
 *     cannot race a cooperative settlement that happens in time; and
 *   - a NON-FINAL input `nSequence` — required for the locktime to actually bind (a final input
 *     0xffffffff would make the tx final regardless of locktime and defeat the gate).
 * It is pre-signed N-of-N, so after the locktime ANY one player can broadcast it ALONE and every
 * contributor recovers their funds with no counterparty cooperation and no server. A funded pot
 * without this in every player's hands is a hard FAIL — there is no path in which a player is stranded.
 *
 * Throws (fail-closed) rather than emit a recovery that does not actually protect funds: a
 * non-future locktime, a final sequence, missing/short signer set, or a fee that consumes the pot.
 */
export function presignNlocktimeRecovery(
  b: BranchBinding,
  funding: FundingRef,
  contributors: readonly Contributor[],
  signers: readonly Signer[],
  recoverableAtHeight: number,
  opts: { fee?: number; sequence?: number } = {},
): NlocktimeRecovery {
  if (signers.length !== contributors.length) throw new Error('one signer per contributor required (no partial recovery)');
  if (contributors.length === 0) throw new Error('a recovery needs at least one contributor');
  if (!Number.isInteger(recoverableAtHeight) || recoverableAtHeight <= 0) {
    throw new Error('recoverableAtHeight must be a positive future block height (a real time-lock, not 0)');
  }
  const sequence = opts.sequence ?? 0xfffffffe; // non-final → the nLockTime gate actually binds
  if (sequence >= 0xffffffff) throw new Error('recovery input sequence must be non-final (< 0xffffffff) or the locktime does not bind');
  const fee = opts.fee ?? 0; // default returns 100% of the pot to the contributors
  const tx = buildTimeoutRefund(b, funding, contributors, { fee, sequence, nLockTime: recoverableAtHeight });
  const sighash = sighashMessage(tx, 0, funding.scriptCode, funding.value);
  const sigs = signers.map((sign, i) => sign(i, sighash));
  return { kind: 'nlocktime-recovery', tx, scriptSig: fundingUnlocking(sigs), sighash, recoverableAtHeight };
}
