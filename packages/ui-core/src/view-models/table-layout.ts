/**
 * Table-layout view-model (REQ-APP-051) — PURE geometry for seating N players around an oval
 * felt table. No React, no DOM: it returns percentage coordinates the presentational layer maps
 * onto an absolutely-positioned container, so the math is unit-testable under `node --test`
 * type-stripping (no enum/namespace/param-properties).
 *
 * Seats are placed on an ellipse. The hero (the local player's seat) is anchored at the BOTTOM
 * centre — the way a real client shows "you" — and the remaining seats fan out clockwise around
 * the rail. Coordinates are in percent of the table box (0–100) so the layout is responsive.
 */

export interface SeatPosition {
  /** Seat index this slot is for. */
  readonly seat: number;
  /** Centre X of the seat, percent of table width (0–100). */
  readonly xPct: number;
  /** Centre Y of the seat, percent of table height (0–100). */
  readonly yPct: number;
  /** True for the bottom-centre hero anchor. */
  readonly isHero: boolean;
}

/**
 * Compute seat positions around the oval for `count` seats (2–9 supported; clamped otherwise),
 * rotating so `heroSeat` sits at the bottom centre. `seatOrder` lets a caller pass the concrete
 * seat numbers (defaults to 0..count-1) so the projection matches the engine's seat indices.
 *
 * The ellipse is inset from the table box so seat cards sit ON the rail, not off the edge.
 */
export function seatPositions(args: {
  readonly count: number;
  readonly heroSeat: number;
  readonly seatOrder?: readonly number[];
  /** Horizontal radius as a fraction of half-width (default 0.92 — near the rail). */
  readonly radiusX?: number;
  /** Vertical radius as a fraction of half-height (default 0.92). */
  readonly radiusY?: number;
}): readonly SeatPosition[] {
  const count = Math.max(2, Math.min(9, Math.floor(args.count)));
  const order =
    args.seatOrder && args.seatOrder.length >= count
      ? args.seatOrder.slice(0, count)
      : Array.from({ length: count }, (_, i) => i);
  const rx = args.radiusX ?? 0.92;
  const ry = args.radiusY ?? 0.92;

  // Index of the hero within the order (the slot we anchor at the bottom).
  const heroIdx = Math.max(0, order.indexOf(args.heroSeat));

  // Angle convention: 90° (downward, screen-space) is the bottom-centre hero anchor. We walk
  // clockwise so the player to the hero's left is the next seat round.
  const bottom = Math.PI / 2;
  const out: SeatPosition[] = [];
  for (let i = 0; i < count; i++) {
    const slot = (i - heroIdx + count) % count; // 0 = hero, then clockwise
    const angle = bottom + (slot * 2 * Math.PI) / count;
    // Screen-space: +x right, +y DOWN. Centre is (50,50); radius is 50% scaled by rx/ry.
    const x = 50 + Math.cos(angle) * 50 * rx;
    const y = 50 + Math.sin(angle) * 50 * ry;
    const seat = order[i]!;
    out.push({
      seat,
      xPct: round2(x),
      yPct: round2(y),
      isHero: seat === args.heroSeat,
    });
  }
  // Return in seat-index order for stable rendering.
  return out.sort((a, b) => a.seat - b.seat);
}

function round2(n: number): number {
  return Math.round(n * 100) / 100;
}
