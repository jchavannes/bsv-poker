import { test } from 'node:test';
import assert from 'node:assert/strict';
import { PLANNED_GAMES, BLACKJACK, SUPPORTED_VARIANTS, createGameModule } from '../src/game-registry.ts';

test('Blackjack is now an IMPLEMENTED dealerless game, no longer a planned reservation (REQ-APP-219)', () => {
  assert.equal(PLANNED_GAMES.find((g) => g.id === 'blackjack'), undefined, 'no longer in the planned list');
  assert.equal(BLACKJACK.status, 'playable');
  assert.equal(BLACKJACK.dealerArea, true);
  assert.equal(BLACKJACK.interPlayerPot, false, 'player-vs-dealer, no inter-player pot');
  for (const c of ['hit', 'stand', 'double']) assert.ok((BLACKJACK.controls as readonly string[]).includes(c));
});

test('Blackjack is NOT a poker variant — it has its own module, not the poker pipeline (core D7)', () => {
  assert.ok(!(SUPPORTED_VARIANTS as readonly string[]).includes('blackjack'));
  assert.throws(() => createGameModule('blackjack' as never, []), /no module for variant/);
});
