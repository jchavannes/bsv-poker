import { test } from 'node:test';
import assert from 'node:assert/strict';
import { parseCard, type Card, type Ruleset } from '@bsv-poker/protocol-types';
import { LocalTableClient } from '../src/local-table-client.ts';
import { shuffleWith } from '../src/shuffle.ts';

const NL: Ruleset = {
  variant: 'holdem',
  bettingStructure: 'NL',
  forcedBetModel: 'blinds',
  seats: 2,
  blinds: { smallBlind: 1, bigBlind: 2, ante: 0, bringIn: 0 },
  minBuyIn: 100,
  maxBuyIn: 100,
  timeouts: { decisionMs: 30000, recoveryMs: 120000 },
  signingMode: 'A',
  currency: 'play-regtest',
  suitTiebreakHouseRule: false,
  hiLo: false,
};

function fixedDeck(): Card[] {
  // hero(seat0)=AA, bot(seat1)=KK, board Qd Jc 9h 4s 3h → hero wins at showdown.
  const head = ['As', 'Ks', 'Ah', 'Kh', 'Qd', 'Jc', '9h', '4s', '3h'].map(parseCard);
  const used = new Set(head);
  const rest: Card[] = [];
  for (let c = 0; c < 52; c++) if (!used.has(c)) rest.push(c);
  return [...head, ...rest];
}

test('shuffleWith with a fixed RNG is a permutation of 0..51', () => {
  const deck = shuffleWith(() => 0.5);
  assert.equal(deck.length, 52);
  assert.equal(new Set(deck).size, 52);
});

test('a single human can drive a full hand vs the bot to settlement', () => {
  const client = new LocalTableClient({ ruleset: NL, heroSeat: 0, makeDeck: fixedDeck });
  // Hero is button/SB, acts first preflop.
  assert.equal(client.isHeroTurn(), true);
  assert.equal(client.getState().betting.toAct, 0);

  // Hero calls; bot (auto) checks → flop. Bot checks-to-act paths are driven internally.
  client.apply({ kind: 'call', seat: 0, amount: 1 });
  // Now postflop: bot (non-button) acts first and auto-checks back to hero.
  assert.equal(client.isHeroTurn(), true);
  client.apply({ kind: 'check', seat: 0, amount: 0 }); // flop
  client.apply({ kind: 'check', seat: 0, amount: 0 }); // turn
  client.apply({ kind: 'check', seat: 0, amount: 0 }); // river → showdown + settle

  const s = client.getState();
  assert.equal(s.handComplete, true);
  assert.equal(s.board.length, 5);
  // Hero (AA) wins the 4-chip pot.
  assert.equal(s.seats.find((x) => x.seat === 0)!.stack, 102);
  assert.equal(s.seats.find((x) => x.seat === 1)!.stack, 98);
});

test('startHand rotates the button, reshuffles, and carries stacks forward', () => {
  const client = new LocalTableClient({ ruleset: NL, heroSeat: 0, makeDeck: fixedDeck });
  client.apply({ kind: 'fold', seat: 0, amount: 0 }); // hero folds preflop
  let s = client.getState();
  assert.equal(s.handComplete, true);
  const heroAfter = s.seats.find((x) => x.seat === 0)!.stack; // 99
  assert.equal(heroAfter, 99);

  s = client.startHand();
  assert.equal(s.handComplete, false);
  // stacks carried forward into the next hand's buy-ins (before new blinds posted).
  const total = s.seats.reduce((p, x) => p + x.stack + x.committedThisHand, 0);
  assert.equal(total, 200); // chips conserved
});
