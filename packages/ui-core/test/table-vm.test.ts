import { test } from 'node:test';
import assert from 'node:assert/strict';
import { parseCard, type Card, type Ruleset } from '@bsv-poker/protocol-types';
import { createHoldem, type HoldemState } from '@bsv-poker/game-holdem';
import {
  tableViewModel,
  consequenceText,
  cardVM,
} from '../src/view-models/table.ts';

const NL: Ruleset = {
  variant: 'holdem',
  bettingStructure: 'NL',
  forcedBetModel: 'blinds',
  seats: 2,
  blinds: { smallBlind: 1, bigBlind: 2, ante: 0, bringIn: 0 },
  minBuyIn: 100,
  maxBuyIn: 200,
  timeouts: { decisionMs: 30000, recoveryMs: 120000 },
  signingMode: 'A',
  currency: 'play-regtest',
  suitTiebreakHouseRule: false,
  hiLo: false,
};

function fixedDeck(): Card[] {
  const head = ['As', 'Ks', 'Ah', 'Kh', 'Qd', 'Jc', '9h', '4s', '3h'].map(parseCard);
  const used = new Set(head);
  const rest: Card[] = [];
  for (let c = 0; c < 52; c++) if (!used.has(c)) rest.push(c);
  return [...head, ...rest];
}

const seats = [
  { seat: 0, stack: 100 },
  { seat: 1, stack: 100 },
];

test('cardVM splits rank/suit and carries the suit letter (no colour-only info)', () => {
  const vm = cardVM(parseCard('As'));
  assert.equal(vm.code, 'As');
  assert.equal(vm.rank, 'A');
  assert.equal(vm.suit, 's');
});

test('tableViewModel projects seats, board, hero hole cards, and the action bar', () => {
  const m = createHoldem({ deck: fixedDeck() });
  const s: HoldemState = m.init(NL, seats);
  const legal = m.getLegalActions(s, 0);
  const resolution = m.isTimeoutEligible(s, 0);
  const vm = tableViewModel({
    state: s,
    heroSeat: 0,
    heroHole: s.hole[0]!,
    legal,
    resolution,
    decisionMs: NL.timeouts.decisionMs,
  });

  assert.equal(vm.seats.length, 2);
  // Hero (seat 0) sees its own hole cards; the bot's are hidden.
  const hero = vm.seats.find((x) => x.seat === 0)!;
  const bot = vm.seats.find((x) => x.seat === 1)!;
  assert.equal(hero.holeCards.length, 2);
  assert.equal(hero.holeCards[0]!.code, 'As');
  assert.equal(bot.holeCards.length, 0);
  // Button/SB acts first preflop heads-up → it is the hero's turn.
  assert.equal(vm.toAct, 0);
  assert.equal(vm.actionBar.isHeroTurn, true);
  // Pot reflects posted blinds (1 + 2).
  assert.equal(vm.totalPot, 3);
  assert.equal(vm.board.length, 0);
});

test('consequence text: facing a bet → fold default, never a forced wager (core §6.4/§11.4)', () => {
  const m = createHoldem({ deck: fixedDeck() });
  const s = m.init(NL, seats);
  const resolution = m.isTimeoutEligible(s, 0); // SB facing BB → default fold
  const c = consequenceText(resolution, 0, 30000);
  assert.equal(c.defaultKind, 'fold');
  assert.match(c.text, /never forced to wager/);
  assert.match(c.text, /30s/);
});

test('consequence text: no bet to face → check default', () => {
  const m = createHoldem({ deck: fixedDeck() });
  let s = m.init(NL, seats);
  s = m.apply(s, { kind: 'call', seat: 0, amount: 1 }); // SB completes; BB to act, may check
  const resolution = m.isTimeoutEligible(s, 0);
  const c = consequenceText(resolution, 1, 30000);
  assert.equal(c.defaultKind, 'check');
  assert.match(c.text, /check/);
});
