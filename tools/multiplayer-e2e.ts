/**
 * Real multiplayer E2E (core §8, REQ-TEST-002 cross-client agreement). Runs TWO independent
 * NetworkedTableClients (Alice seat 0, Bob seat 1) — each its OWN peer-to-peer node — that exchange
 * their entropy commit/reveal and betting actions ONLY over the P2P table channel, each deriving
 * state through its own engine. The test passes iff both clients converge to the byte-identical final
 * state hash — proving the transport is transport-only and the truth is the client-reconstructed tx
 * set (P2/P3). There is NO relay server and NO indexer server.
 */

import assert from 'node:assert/strict';
import type { Action, LegalActions, Ruleset } from '@bsv-poker/protocol-types';
import { NetworkedTableClient } from '@bsv-poker/app-services';
import { p2pMesh } from './p2p-mesh.ts';

const RULES: Ruleset = {
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

// Passive strategy: check when possible, else call, else fold — drives the hand to showdown.
const passive = (legal: LegalActions, seat: number): Action => {
  if (legal.check) return { kind: 'check', seat, amount: 0 };
  if (legal.call) return { kind: 'call', seat, amount: legal.call.amount };
  return { kind: 'fold', seat, amount: 0 };
};

async function main(): Promise<void> {
  console.log('[mp-e2e] standing up a 2-node P2P mesh (NO relay/indexer server)…');
  const mesh = await p2pMesh(2);
  const [relayA, relayB] = mesh.transports; // each player's own P2P node (a structural RelayChannel)
  const tableId = `mp-table-${Date.now()}`;
  await relayA!.createTable(tableId, 'Multiplayer HU');
  const seats = [
    { seat: 0, stack: 100 },
    { seat: 1, stack: 100 },
  ];

  const alice = new NetworkedTableClient({
    relay: relayA!,
    tableId,
    mySeat: 0,
    seats,
    ruleset: RULES,
    entropy: Uint8Array.from(Array.from({ length: 32 }, (_, i) => (i * 7 + 1) % 251)),
  });
  const bob = new NetworkedTableClient({
    relay: relayB!,
    tableId,
    mySeat: 1,
    seats,
    ruleset: RULES,
    entropy: Uint8Array.from(Array.from({ length: 32 }, (_, i) => (i * 13 + 5) % 251)),
  });

  console.log('[mp-e2e] Alice and Bob playing a full hand over the relay…');
  const [ra, rb] = await Promise.all([alice.runHand(passive), bob.runHand(passive)]);

  console.log(`[mp-e2e] Alice final stateHash: ${ra.stateHash.slice(0, 24)}…`);
  console.log(`[mp-e2e] Bob   final stateHash: ${rb.stateHash.slice(0, 24)}…`);
  assert.equal(ra.stateHash, rb.stateHash, 'cross-client agreement: both engines agree exactly');
  assert.equal(ra.state.handComplete, true);
  assert.equal(ra.state.board.length, 5);
  mesh.close();
  console.log('\n[mp-e2e] PASS — two peer-to-peer clients converged to byte-identical state (REQ-TEST-002), no server.');
}

main().then(
  () => process.exit(0),
  (e) => {
    console.error('[mp-e2e] FAIL:', (e as Error).message);
    process.exit(1);
  },
);
