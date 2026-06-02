/**
 * Card and deck encoding — core §5.1, REQ-POKER-001.
 *
 * Canonical: card = rank*4 + suit, rank ∈ 0..12 (2=0 … A=12), suit ∈ 0..3 (c=0,d=1,h=2,s=3).
 * card_serial ∈ 0..51. This is the SAME encoding bound into the shuffle (core §4) and the
 * transaction schemas (core §6). It is the one the oracle (handeval_oracle.py) uses.
 */

export const RANKS = '23456789TJQKA' as const; // index 0..12 -> char
export const SUITS = 'cdhs' as const; // index 0..3 -> char

/** A concealed/known card index in 0..51. */
export type Card = number;

export const NUM_CARDS = 52;

export function isCard(n: number): boolean {
  return Number.isInteger(n) && n >= 0 && n < NUM_CARDS;
}

/** Spec encoding rank: 0..12 with 2=0 … A=12. */
export function cardRank(c: Card): number {
  return Math.floor(c / 4);
}

/** Suit: 0..3 (c,d,h,s). Poker has NO suit precedence (core §5.5.1). */
export function cardSuit(c: Card): number {
  return c % 4;
}

/**
 * Internal comparison rank 2..14 (A=14). Used by hand evaluation; the wheel (A-2-3-4-5)
 * is scored as straight-high 5 by the evaluator, not here.
 */
export function compareRank(c: Card): number {
  return cardRank(c) + 2;
}

/** Ace-to-five LOW value: A=1, 2=2 … K=13 (core §5.3.3, REQ-POKER-006). */
export function lowRankValue(c: Card): number {
  const r = cardRank(c);
  return r === 12 ? 1 : r + 2;
}

export function cardToString(c: Card): string {
  if (!isCard(c)) throw new RangeError(`card out of range: ${c}`);
  return `${RANKS[cardRank(c)]}${SUITS[cardSuit(c)]}`;
}

/** Parse "As", "Td", "9h" … into a card index. */
export function parseCard(s: string): Card {
  if (s.length !== 2) throw new SyntaxError(`bad card: "${s}"`);
  const r = RANKS.indexOf(s[0]!.toUpperCase());
  const su = SUITS.indexOf(s[1]!.toLowerCase());
  if (r < 0 || su < 0) throw new SyntaxError(`bad card: "${s}"`);
  return r * 4 + su;
}

export function parseHand(s: string): Card[] {
  return s.trim().split(/\s+/).map(parseCard);
}
