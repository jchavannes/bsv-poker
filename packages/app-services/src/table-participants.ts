/**
 * Participant-set policy (REQ-CRYPTO-011): the set of players in a hand is computed BETWEEN hands
 * (sit-out / join / bust take effect only here) and then FROZEN for the whole hand — the N-party
 * shuffle and settlement are over exactly that set. A change arriving mid-hand never alters the
 * in-progress hand; it applies to the next one.
 */

export interface SeatRef {
  readonly seat: number;
}

/** The seated set for the NEXT hand: seats with a positive stack (busted seats drop out). */
export function seatedForNextHand<T extends SeatRef>(seats: readonly T[], stackOf: (seat: number) => number): T[] {
  return seats.filter((s) => stackOf(s.seat) > 0);
}

/** Freeze the participant set at hand start — returned as an immutable snapshot for the hand. */
export function freezeParticipants<T extends SeatRef>(seated: readonly T[]): readonly T[] {
  return Object.freeze([...seated]);
}

export interface SeatChange {
  readonly kind: 'join' | 'sit-out';
  readonly seat: number;
}

/**
 * Apply sit-out/join changes — ONLY valid between hands. Returns the new seated set; the frozen set
 * of an in-progress hand is unaffected because callers freeze before the hand and re-derive after.
 */
export function applyBetweenHands<T extends SeatRef>(current: readonly T[], changes: readonly SeatChange[], make: (seat: number) => T): T[] {
  const set = new Map(current.map((s) => [s.seat, s]));
  for (const ch of changes) {
    if (ch.kind === 'join') set.set(ch.seat, make(ch.seat));
    else set.delete(ch.seat);
  }
  return [...set.values()].sort((a, b) => a.seat - b.seat);
}
