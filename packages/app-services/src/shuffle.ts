/**
 * Browser-safe deck shuffle for Phase-1 play-money/regtest hot-seat play.
 *
 * HONEST SCOPE (§A2.3, build brief): this is a single-party Fisher–Yates seeded by
 * crypto.getRandomValues. It is NOT the multi-party mental-poker shuffle (core §4) — that
 * commit-reveal protocol with no single party knowing the order is the Node SDK / crypto-layer
 * path and is out of scope for the browser bundle (crypto-mentalpoker uses node:crypto). Here a
 * single client deals to itself + a bot, so a trustless shuffle is neither possible nor claimed.
 */

import { NUM_CARDS, type Card } from '@bsv-poker/protocol-types';

/** Deterministic Fisher–Yates using an injected 0..1 RNG (so tests can seed it). */
export function shuffleWith(rng: () => number): Card[] {
  const deck: Card[] = [];
  for (let c = 0; c < NUM_CARDS; c++) deck.push(c);
  for (let i = deck.length - 1; i > 0; i--) {
    const j = Math.floor(rng() * (i + 1));
    const tmp = deck[i]!;
    deck[i] = deck[j]!;
    deck[j] = tmp;
  }
  return deck;
}

/** A uniform 0..1 RNG backed by crypto.getRandomValues when available (browser), else Math.random. */
export function cryptoRng(): () => number {
  const g = globalThis as { crypto?: { getRandomValues?: (a: Uint32Array) => Uint32Array } };
  const c = g.crypto;
  if (c && typeof c.getRandomValues === 'function') {
    return () => {
      const buf = new Uint32Array(1);
      c.getRandomValues!(buf);
      return buf[0]! / 0x100000000;
    };
  }
  return Math.random;
}

/** Shuffle a fresh 52-card deck using the platform CSPRNG (regtest/play-money). */
export function shuffleDeck(): Card[] {
  return shuffleWith(cryptoRng());
}
