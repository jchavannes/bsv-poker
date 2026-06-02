/**
 * Ruleset model — core §5.2. A Ruleset fixes variant, betting structure, seats, blind/ante
 * schedule, bring-in, buy-ins, sizing rules, raise cap, timeout profile, deck format, and
 * currency semantics (D8). rulesetHash = H(canonicalSerialize(Ruleset)) is bound into every
 * transaction (core §6.3) — see ./serialize.ts.
 */

/** Variants (core §0.3, §7). Blackjack is a later track (core D7) — not a poker variant. */
export const VARIANTS = ['holdem', 'omaha', 'stud', 'draw', 'razz'] as const;
export type Variant = (typeof VARIANTS)[number];

/** Betting structures (core §5.4, D3). */
export const BETTING_STRUCTURES = ['NL', 'PL', 'FL'] as const;
export type BettingStructure = (typeof BETTING_STRUCTURES)[number];

/** Forced-bet model (core §A21.2): blinds (holdem/omaha/draw) vs ante+bring-in (stud/razz). */
export const FORCED_BET_MODELS = ['blinds', 'ante-bringin'] as const;
export type ForcedBetModel = (typeof FORCED_BET_MODELS)[number];

/** Signing mode (core §4.3, D9). Mode A default for Phase 1. */
export const SIGNING_MODES = ['A', 'B'] as const;
export type SigningMode = (typeof SIGNING_MODES)[number];

/** Currency semantics (core D8). Play-money / regtest by default. */
export const CURRENCY_SEMANTICS = ['play-regtest', 'mainnet-research'] as const;
export type CurrencySemantics = (typeof CURRENCY_SEMANTICS)[number];

export interface TimeoutProfile {
  /** UI/operational decision countdown; NOT a consensus value (timing is tx-level, core §6.2). */
  readonly decisionMs: number;
  /** Recovery window; MUST be > decisionMs. */
  readonly recoveryMs: number;
}

export interface BlindSchedule {
  readonly smallBlind: number;
  readonly bigBlind: number;
  /** Ante per seat (0 if none). Stud/razz use ante+bringIn instead of blinds. */
  readonly ante: number;
  /** Bring-in amount (stud/razz only; 0 otherwise). */
  readonly bringIn: number;
}

export interface FixedLimitSizing {
  readonly smallBet: number;
  readonly bigBet: number;
  /** Cap on number of raises per street in FL (standard 1 bet + N raises). */
  readonly maxRaisesPerStreet: number;
}

export interface Ruleset {
  readonly variant: Variant;
  readonly bettingStructure: BettingStructure;
  readonly forcedBetModel: ForcedBetModel;
  readonly seats: number; // 2..9 (core D2)
  readonly blinds: BlindSchedule;
  readonly minBuyIn: number;
  readonly maxBuyIn: number;
  /** Fixed-Limit sizing — present iff bettingStructure === 'FL'. */
  readonly flSizing?: FixedLimitSizing;
  readonly timeouts: TimeoutProfile;
  readonly signingMode: SigningMode;
  readonly currency: CurrencySemantics;
  /**
   * House-rule: odd-chip suit tiebreak. DEFAULT false (core §5.5.1, RT-01 m3). MUST NOT be
   * implemented inside hand evaluation — it is a pot-award tiebreak only.
   */
  readonly suitTiebreakHouseRule: boolean;
  /** Omaha hi-lo split (core REQ-FSM-007). Off unless set. */
  readonly hiLo: boolean;
}
