/**
 * Table view-model (REQ-APP-051) — a PURE projection of engine state into render props.
 *
 * No React, no I/O, no business logic: it reads a HoldemState (+ the bot/own seat point of
 * view) and the module's legal-action / timeout-eligibility outputs, and emits the props the
 * presentational components render. Legality is NEVER computed here — it is read from the
 * engine (game-holdem getLegalActions / isTimeoutEligible). The UI hides complexity but never
 * consequences (core §11.4 / §6.4): the consequence text is derived from isTimeoutEligible.
 *
 * Kept strip-friendly (no enum/namespace/param-properties) so the unit tests run under
 * `node --test` type-stripping.
 */

import { cardToString } from '@bsv-poker/protocol-types';
import type { Card, LegalActions, Pot, SeatState } from '@bsv-poker/protocol-types';
import type { HoldemState } from '@bsv-poker/game-holdem';
import type { TimeoutResolution } from '@bsv-poker/engine';

export interface CardVM {
  /** Full two-char code e.g. "As". */
  readonly code: string;
  /** Rank glyph, e.g. "A", "T". */
  readonly rank: string;
  /** Suit letter, e.g. "s" — carried as a glyph so info is never colour-only (§A3.5 a11y). */
  readonly suit: string;
}

export interface SeatVM {
  readonly seat: number;
  readonly stack: number;
  readonly committedThisRound: number;
  readonly folded: boolean;
  readonly allIn: boolean;
  readonly isButton: boolean;
  readonly isToAct: boolean;
  /** True for the seat this view is rendered for (the human's perspective). */
  readonly isHero: boolean;
  /** Hero's own hole cards (custody-bound — only ever populated for the hero seat). */
  readonly holeCards: readonly CardVM[];
}

export interface PotVM {
  readonly amount: number;
  readonly eligible: readonly number[];
}

export interface ActionBarVM {
  /** Whether it is the hero's turn (controls live) — read straight from the engine. */
  readonly isHeroTurn: boolean;
  readonly legal: LegalActions;
}

export interface TimerVM {
  /** Seat the clock is on. */
  readonly seat: number | null;
  /** decisionMs from the ruleset timeout profile (operational, not consensus — core §6.2). */
  readonly decisionMs: number;
  /** Exact consequence text (core §11.4). */
  readonly consequenceText: string;
  /** The kind the safe default resolves to ("check" | "fold"); null if no seat on the clock. */
  readonly defaultKind: string | null;
}

export interface TableViewModel {
  readonly phase: string;
  readonly handComplete: boolean;
  readonly board: readonly CardVM[];
  readonly seats: readonly SeatVM[];
  readonly pots: readonly PotVM[];
  /** Sum of every pot plus uncommitted chips already in front of seats this round. */
  readonly totalPot: number;
  readonly toAct: number | null;
  readonly heroSeat: number;
  readonly actionBar: ActionBarVM;
  readonly timer: TimerVM;
}

export function cardVM(c: Card): CardVM {
  const code = cardToString(c);
  return { code, rank: code[0] ?? '?', suit: code[1] ?? '?' };
}

function seatVM(
  s: SeatState,
  heroSeat: number,
  buttonSeat: number,
  toAct: number | null,
  hole: readonly Card[],
): SeatVM {
  const isHero = s.seat === heroSeat;
  return {
    seat: s.seat,
    stack: s.stack,
    committedThisRound: s.committedThisRound,
    folded: s.folded,
    allIn: s.allIn,
    isButton: s.seat === buttonSeat,
    isToAct: toAct === s.seat,
    isHero,
    holeCards: isHero ? hole.map(cardVM) : [],
  };
}

/**
 * Consequence text derived from the module's timeout resolution (core §11.4 / §6.4).
 * The player is NEVER forced to wager: facing a bet the default is fold, otherwise check.
 */
export function consequenceText(
  resolution: TimeoutResolution | null,
  heroSeat: number,
  decisionMs: number,
): { text: string; defaultKind: string | null } {
  if (resolution === null) {
    return { text: 'Waiting for the hand to advance.', defaultKind: null };
  }
  const seconds = Math.round(decisionMs / 1000);
  const onClock = resolution.seat === heroSeat ? 'you' : `seat ${resolution.seat}`;
  if (resolution.defaultAction.kind === 'fold') {
    return {
      text: `If ${onClock === 'you' ? 'you do' : onClock + ' does'} nothing while facing a bet, ${onClock} fold${onClock === 'you' ? '' : 's'} in ${seconds}s — you are never forced to wager.`,
      defaultKind: 'fold',
    };
  }
  return {
    text: `If ${onClock === 'you' ? 'you do' : onClock + ' does'} nothing, ${onClock} check${onClock === 'you' ? '' : 's'} in ${seconds}s.`,
    defaultKind: 'check',
  };
}

/**
 * Project a HoldemState into the table render props for the given hero seat.
 * `legal` and `resolution` are the engine's outputs (getLegalActions / isTimeoutEligible) —
 * passed in so the projection stays pure and the component never recomputes legality.
 */
export function tableViewModel(args: {
  readonly state: HoldemState;
  readonly heroSeat: number;
  readonly heroHole: readonly Card[];
  readonly legal: LegalActions;
  readonly resolution: TimeoutResolution | null;
  readonly decisionMs: number;
}): TableViewModel {
  const { state, heroSeat, heroHole, legal, resolution, decisionMs } = args;
  const toAct = state.betting.toAct;
  const seats = state.seats.map((s) =>
    seatVM(s, heroSeat, state.buttonSeat, toAct, heroHole),
  );
  const pots: PotVM[] = (state.pots as readonly Pot[]).map((p) => ({
    amount: p.amount,
    eligible: [...p.eligible],
  }));
  const committed = state.seats.reduce((sum, s) => sum + s.committedThisHand, 0);
  const cons = consequenceText(resolution, heroSeat, decisionMs);
  return {
    phase: state.phase,
    handComplete: state.handComplete,
    board: state.board.map(cardVM),
    seats,
    pots,
    totalPot: committed,
    toAct,
    heroSeat,
    actionBar: { isHeroTurn: toAct === heroSeat && !state.handComplete, legal },
    timer: {
      seat: resolution ? resolution.seat : null,
      decisionMs,
      consequenceText: cons.text,
      defaultKind: cons.defaultKind,
    },
  };
}
