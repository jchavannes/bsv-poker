/**
 * Bet-sizing view-model (REQ-APP-051) — PURE helpers for the bet/raise slider + quick buttons.
 *
 * CRITICAL CONTRACT: this NEVER computes legality. The min/max bounds come straight from the
 * engine-supplied `LegalActions` descriptor (legal.bet / legal.raise). All this does is:
 *   - clamp a requested amount into [min, max] (slider/keyboard input safety), and
 *   - turn "pot-relative" quick buttons (min, ½ pot, pot, all-in) into a concrete amount that is
 *     then ALSO clamped into the engine's legal range.
 *
 * If the engine offers no sizer (no bet and no raise legal) the helpers report `available:false`
 * and the component hides the slider. React-free / strip-friendly for `node --test`.
 */

import type { LegalActions } from '@bsv-poker/protocol-types';

export interface SizerRange {
  readonly available: boolean;
  /** 'bet' when opening, 'raise' when re-raising; null when unavailable. */
  readonly kind: 'bet' | 'raise' | null;
  readonly min: number;
  readonly max: number;
}

/** Read the active sizing range out of the engine legal descriptor (bet takes precedence). */
export function sizerRange(legal: LegalActions): SizerRange {
  const s = legal.bet ?? legal.raise;
  if (!s) return { available: false, kind: null, min: 0, max: 0 };
  return {
    available: true,
    kind: legal.bet ? 'bet' : 'raise',
    min: s.min,
    max: s.max,
  };
}

/** Clamp `amount` into the legal [min,max], snapping to an integer (satoshis, INV-BS-1). */
export function clampToRange(amount: number, range: SizerRange): number {
  if (!range.available) return 0;
  const n = Number.isFinite(amount) ? Math.round(amount) : range.min;
  if (n < range.min) return range.min;
  if (n > range.max) return range.max;
  return n;
}

export type QuickSize = 'min' | 'half-pot' | 'pot' | 'all-in';

export interface QuickButtonVM {
  readonly key: QuickSize;
  readonly label: string;
  /** Concrete amount this button would set, already clamped into the engine's legal range. */
  readonly amount: number;
}

/**
 * Build the quick-size buttons for the current pot + legal range. The raw pot-relative targets
 * are computed for display convenience ONLY and are clamped into the engine range — so a "pot"
 * button can never request an illegal size; if the pot bet is below the legal min it lands on min,
 * and a pot bet over the player's stack lands on all-in (max). For a RAISE the target is the
 * round-bet "raise to" total (current call + pot-fraction), still clamped to the legal raise band.
 */
export function quickButtons(args: {
  readonly range: SizerRange;
  readonly pot: number;
  /** Amount the hero must call to continue (0 when opening) — used to size raises off the pot. */
  readonly toCall: number;
}): readonly QuickButtonVM[] {
  const { range, pot, toCall } = args;
  if (!range.available) return [];
  // Pot-after-call is the standard base for a pot-sized raise; for an opening bet toCall is 0.
  const potAfterCall = pot + toCall;
  const halfPotRaiseTo = toCall + Math.round(potAfterCall * 0.5);
  const potRaiseTo = toCall + potAfterCall;
  const halfTarget = range.kind === 'bet' ? Math.round(pot * 0.5) : halfPotRaiseTo;
  const potTarget = range.kind === 'bet' ? pot : potRaiseTo;
  return [
    { key: 'min', label: range.kind === 'raise' ? 'Min-raise' : 'Min', amount: clampToRange(range.min, range) },
    { key: 'half-pot', label: '½ Pot', amount: clampToRange(halfTarget, range) },
    { key: 'pot', label: 'Pot', amount: clampToRange(potTarget, range) },
    { key: 'all-in', label: 'All-in', amount: clampToRange(range.max, range) },
  ];
}
