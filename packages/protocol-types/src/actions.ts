/**
 * Player actions and betting-action kinds — core §5.4, §7.1.
 * Actions are the in-window moves; each maps to a signed transaction (core §6.1 Action).
 */

export const ACTION_KINDS = [
  'check',
  'bet',
  'call',
  'raise',
  'fold',
  'draw', // five-card draw (core §7.3.3)
  'stand', // stand pat (draw 0)
] as const;
export type ActionKind = (typeof ACTION_KINDS)[number];

export interface Action {
  readonly kind: ActionKind;
  readonly seat: number;
  /** For bet/call/raise: the amount (raise = raise-TO total this round). 0 otherwise. */
  readonly amount: number;
  /** For draw: the set of concealed-card slot indices the player discards (0..n). */
  readonly discard?: readonly number[];
}

/** Legal-action descriptor returned by BettingStructure.legalBets (core §5.4, REQ-POKER-008). */
export interface LegalActions {
  readonly check: boolean;
  readonly call?: { readonly amount: number };
  readonly bet?: { readonly min: number; readonly max: number };
  readonly raise?: { readonly min: number; readonly max: number };
  readonly fold: boolean;
  /** Draw variants only. */
  readonly draw?: boolean;
}
