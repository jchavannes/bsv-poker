/**
 * Dealerless Blackjack (core D7; app §A21.7). There is NO trusted dealer: the shoe is the shared
 * mental-poker deck, and the "dealer" is a deterministic automaton driven entirely by that deck —
 * it reveals its hole card and HITS to a hard/soft 17 by fixed rule, so no party controls it. The
 * human chooses hit / stand / double; settlement compares the player's total to the dealer's. This
 * is its OWN model (player-vs-dealer, no inter-player pot) — it is NOT the poker pipeline.
 */

import { type Card, cardRank } from '@bsv-poker/protocol-types';

/** Blackjack value of a card: 2–9 pip, T/J/Q/K = 10, Ace = 11 (soft, reduced to 1 as needed). */
export function cardValue(card: Card): number {
  const r = cardRank(card); // 0=2 … 7=9, 8=T,9=J,10=Q,11=K, 12=A
  if (r === 12) return 11;
  if (r >= 8) return 10;
  return r + 2;
}

export interface HandTotal {
  readonly total: number;
  /** True if an ace is still counted as 11 (a "soft" total). */
  readonly soft: boolean;
}

export function handTotal(cards: readonly Card[]): HandTotal {
  let total = 0;
  let aces = 0;
  for (const c of cards) {
    const v = cardValue(c);
    total += v;
    if (v === 11) aces++;
  }
  // Reduce aces from 11 to 1 while busting.
  while (total > 21 && aces > 0) {
    total -= 10;
    aces--;
  }
  return { total, soft: aces > 0 };
}

export const isBust = (cards: readonly Card[]): boolean => handTotal(cards).total > 21;
export const isBlackjack = (cards: readonly Card[]): boolean => cards.length === 2 && handTotal(cards).total === 21;

export type Outcome = 'player-blackjack' | 'player-win' | 'push' | 'dealer-win' | 'player-bust';
export type PlayerMove = 'hit' | 'stand' | 'double';

export interface BlackjackState {
  readonly deck: readonly Card[];
  readonly cursor: number;
  readonly player: readonly Card[];
  readonly dealer: readonly Card[];
  readonly bet: number;
  readonly doubled: boolean;
  readonly playerDone: boolean;
  readonly finished: boolean;
  readonly outcome?: Outcome;
  /** Net chips to the player (negative = lost to the dealer). */
  readonly payout?: number;
}

/** Deal the opening round: two player cards, two dealer cards (one is the hole). */
export function dealRound(deck: readonly Card[], bet: number): BlackjackState {
  if (!Number.isInteger(bet) || bet <= 0) throw new Error('bet must be a positive integer');
  if (deck.length < 10) throw new Error('deck too small');
  const player = [deck[0]!, deck[2]!];
  const dealer = [deck[1]!, deck[3]!];
  const s: BlackjackState = { deck, cursor: 4, player, dealer, bet, doubled: false, playerDone: false, finished: false };
  // A natural blackjack resolves immediately.
  if (isBlackjack(player) || isBlackjack(dealer)) return settle({ ...s, playerDone: true });
  return s;
}

export function legalMoves(s: BlackjackState): readonly PlayerMove[] {
  if (s.finished || s.playerDone) return [];
  // Double only on the opening two cards (classic rule).
  return s.player.length === 2 ? ['hit', 'stand', 'double'] : ['hit', 'stand'];
}

export function hit(s: BlackjackState): BlackjackState {
  if (s.finished || s.playerDone) throw new Error('player cannot act');
  const player = [...s.player, s.deck[s.cursor]!];
  const next = { ...s, player, cursor: s.cursor + 1 };
  if (isBust(player)) return settle({ ...next, playerDone: true });
  return next;
}

export function stand(s: BlackjackState): BlackjackState {
  if (s.finished || s.playerDone) throw new Error('player cannot act');
  return settle({ ...s, playerDone: true });
}

export function double(s: BlackjackState): BlackjackState {
  if (s.finished || s.playerDone || s.player.length !== 2) throw new Error('cannot double now');
  const player = [...s.player, s.deck[s.cursor]!];
  return settle({ ...s, player, cursor: s.cursor + 1, bet: s.bet * 2, doubled: true, playerDone: true });
}

/** Deterministic dealer + settlement (the "dealerless" automaton: reveal + hit to 17, fixed). */
export function settle(s: BlackjackState): BlackjackState {
  const playerBJ = isBlackjack(s.player);
  const dealerBJ = isBlackjack(s.dealer);
  let dealer = [...s.dealer];
  let cursor = s.cursor;

  if (isBust(s.player)) {
    return { ...s, dealer, cursor, finished: true, playerDone: true, outcome: 'player-bust', payout: -s.bet };
  }
  // Naturals resolve before the dealer draws.
  if (playerBJ || dealerBJ) {
    const outcome: Outcome = playerBJ && dealerBJ ? 'push' : playerBJ ? 'player-blackjack' : 'dealer-win';
    const payout = outcome === 'player-blackjack' ? Math.floor(s.bet * 1.5) : outcome === 'push' ? 0 : -s.bet;
    return { ...s, dealer, cursor, finished: true, playerDone: true, outcome, payout };
  }
  // Dealer plays: hit while total < 17 (stands on all 17, incl. soft 17). Bounded by the deck.
  while (handTotal(dealer).total < 17 && cursor < s.deck.length) {
    dealer = [...dealer, s.deck[cursor]!];
    cursor++;
  }
  const p = handTotal(s.player).total;
  const d = handTotal(dealer).total;
  let outcome: Outcome;
  if (d > 21 || p > d) outcome = 'player-win';
  else if (p === d) outcome = 'push';
  else outcome = 'dealer-win';
  const payout = outcome === 'player-win' ? s.bet : outcome === 'push' ? 0 : -s.bet;
  return { ...s, dealer, cursor, finished: true, playerDone: true, outcome, payout };
}
