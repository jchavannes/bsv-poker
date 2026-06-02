import { test } from 'node:test';
import assert from 'node:assert/strict';
import { parseCard } from '@bsv-poker/protocol-types';
import {
  cardValue,
  handTotal,
  isBlackjack,
  isBust,
  dealRound,
  hit,
  stand,
  double,
  legalMoves,
  type BlackjackState,
} from '../src/blackjack.ts';

const C = (s: string) => parseCard(s);

test('card values: pips, tens, and the ace as 11', () => {
  assert.equal(cardValue(C('2c')), 2);
  assert.equal(cardValue(C('9d')), 9);
  for (const t of ['Th', 'Js', 'Qc', 'Kd']) assert.equal(cardValue(C(t)), 10);
  assert.equal(cardValue(C('Ah')), 11);
});

test('hand totals handle soft aces (and reduce to avoid busting)', () => {
  assert.deepEqual(handTotal([C('Ah'), C('Kd')]), { total: 21, soft: true }); // blackjack, soft
  assert.deepEqual(handTotal([C('Ah'), C('6d')]), { total: 17, soft: true }); // soft 17
  assert.deepEqual(handTotal([C('Ah'), C('6d'), C('Tc')]), { total: 17, soft: false }); // ace → 1
  assert.deepEqual(handTotal([C('Ah'), C('Ad')]), { total: 12, soft: true }); // one ace reduced
  assert.deepEqual(handTotal([C('Kh'), C('Qd'), C('2c')]), { total: 22, soft: false }); // bust
});

test('blackjack and bust detection', () => {
  assert.equal(isBlackjack([C('Ah'), C('Kd')]), true);
  assert.equal(isBlackjack([C('Ah'), C('5d'), C('5c')]), false); // 21 but 3 cards ≠ natural
  assert.equal(isBust([C('Kh'), C('Qd'), C('5c')]), true);
});

// Build a deck whose first cards force a chosen player/dealer deal: player gets idx0,2; dealer idx1,3.
function deckFrom(order: string[]): number[] {
  const used = new Set(order.map(C));
  const rest: number[] = [];
  for (let c = 0; c < 52; c++) if (!used.has(c)) rest.push(c);
  return [...order.map(C), ...rest];
}

test('player natural blackjack pays 3:2', () => {
  // player A,K (idx0,2); dealer 9,7 (idx1,3)
  const s = dealRound(deckFrom(['Ah', '9d', 'Kc', '7s']), 100);
  assert.equal(s.finished, true);
  assert.equal(s.outcome, 'player-blackjack');
  assert.equal(s.payout, 150);
});

test('both blackjack pushes', () => {
  const s = dealRound(deckFrom(['Ah', 'Ad', 'Kc', 'Ks']), 100);
  assert.equal(s.outcome, 'push');
  assert.equal(s.payout, 0);
});

test('player bust loses immediately, dealer never draws', () => {
  // player K,Q (20) then hits a 5 → 25 bust. dealer 9,7.
  let s = dealRound(deckFrom(['Kh', '9d', 'Qc', '7s', '5h']), 100);
  s = hit(s);
  assert.equal(s.outcome, 'player-bust');
  assert.equal(s.payout, -100);
});

test('stand → dealer hits to 17 and the higher total wins', () => {
  // player T,9 = 19. dealer 7,6 = 13 → must draw. next card 4 → 17 (stands). player 19 > 17 → win.
  const s = stand(dealRound(deckFrom(['Th', '7d', '9c', '6s', '4h']), 100));
  assert.equal(handTotal(s.dealer).total, 17);
  assert.equal(s.outcome, 'player-win');
  assert.equal(s.payout, 100);
});

test('double doubles the bet, takes exactly one card, and stands', () => {
  // player 5,6 = 11; double → draw T = 21. dealer 9,7=16 → draws; next 8 → 24 bust → player win 2×.
  const s = double(dealRound(deckFrom(['5h', '9d', '6c', '7s', 'Th', '8d']), 100));
  assert.equal(s.player.length, 3);
  assert.equal(s.doubled, true);
  assert.equal(s.outcome, 'player-win');
  assert.equal(s.payout, 200);
});

test('legal moves: double only on the opening two cards', () => {
  const s = dealRound(deckFrom(['5h', '9d', '6c', '7s', '2h']), 100);
  assert.deepEqual([...legalMoves(s)].sort(), ['double', 'hit', 'stand']);
  const after = hit(s);
  if (!after.finished) assert.deepEqual([...legalMoves(after)].sort(), ['hit', 'stand']);
});

test('EXHAUSTIVE: every deck deal resolves to a valid outcome + consistent payout (no crash, no limbo)', () => {
  function seededDeck(seed: number): number[] {
    // simple deterministic shuffle (LCG) → permutation of 0..51
    const a = Array.from({ length: 52 }, (_, i) => i);
    let x = (seed * 2654435761) >>> 0;
    for (let i = 51; i > 0; i--) {
      x = (x * 1103515245 + 12345) >>> 0;
      const j = x % (i + 1);
      [a[i], a[j]] = [a[j]!, a[i]!];
    }
    return a;
  }
  const strategies: ((s: BlackjackState) => BlackjackState)[] = [
    (s) => stand(s),
    (s) => { let t = s; while (!t.finished && handTotal(t.player).total < 17) t = hit(t); return t.finished ? t : stand(t); },
    (s) => (legalMoves(s).includes('double') ? double(s) : stand(s)),
    (s) => { let t = s; while (!t.finished) t = hit(t); return t; }, // hit until bust/stop
  ];
  let n = 0;
  for (let seed = 0; seed < 400; seed++) {
    for (const play of strategies) {
      let s = dealRound(seededDeck(seed), 100);
      if (!s.finished) s = play(s);
      assert.equal(s.finished, true, `seed ${seed}: round never finished`);
      assert.ok(['player-blackjack', 'player-win', 'push', 'dealer-win', 'player-bust'].includes(s.outcome!), `seed ${seed}: bad outcome ${s.outcome}`);
      // payout matches outcome sign + magnitude exactly
      const expect = s.outcome === 'player-blackjack' ? Math.floor(s.bet * 1.5)
        : s.outcome === 'player-win' ? s.bet
        : s.outcome === 'push' ? 0
        : -s.bet;
      assert.equal(s.payout, expect, `seed ${seed}: payout ${s.payout} ≠ ${expect} for ${s.outcome}`);
      n++;
    }
  }
  assert.equal(n, 400 * 4);
});
