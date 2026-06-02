/**
 * Anchored timing for consensus decisions (REQ-TX-007). "Now" for timeouts is derived from a
 * chain/relay-anchored height (and median-time), NEVER local wall-clock — so every participant
 * agrees on whether a deadline has passed. Deadlines are expressed at the transaction level
 * (nLockTime / nSequence; REQ-TX-002), not via in-script CLTV/CSV (REQ-TX-001).
 */

/** A height/time reading taken from the chain (or a relay-anchored source), not the local clock. */
export interface AnchoredClock {
  readonly height: number;
  readonly medianTimeSeconds: number;
}

/** A deadline `blocks` ahead of the anchored height — the nLockTime a recovery spend matures at. */
export function deadlineFromAnchor(clock: AnchoredClock, blocks: number): number {
  return clock.height + Math.max(0, Math.floor(blocks));
}

/** True iff the anchored height has reached/passed the deadline height (consensus-safe). */
export function isDeadlinePassed(deadlineHeight: number, clock: AnchoredClock): boolean {
  return clock.height >= deadlineHeight;
}

/**
 * Decision deadline from the anchored clock for a per-action timeout window (seconds → an
 * approximate block budget at ~600s/block on mainnet; regtest mines on demand, so callers may pass
 * blocks directly). Local wall-clock is intentionally NOT consulted.
 */
export function decisionDeadlineHeight(clock: AnchoredClock, windowSeconds: number, secondsPerBlock = 600): number {
  return deadlineFromAnchor(clock, Math.ceil(windowSeconds / secondsPerBlock));
}
