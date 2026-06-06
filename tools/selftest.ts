/**
 * Self-contained stack self-test (core §10.2/§10.3, REQ-VM-001) — the bootstrap's "bring the stack
 * up, run self-tests, print a transcript" step. bsv-poker is fully PEER-TO-PEER, so there is NO Go
 * relay/indexer to build or start; the self-test:
 *   1. stands up a 2-node P2P mesh (the serverless transport — no server);
 *   2. exercises serverless DISCOVERY (presence + gossiped table directory) and a pub/sub round-trip
 *      across the mesh;
 *   3. runs a full heads-up Hold'em hand in-process (the client/engine role) and prints the
 *      transcript (ordered actions + final state hash + payouts);
 *   4. tears the mesh down.
 *
 * Phase-0 note: the BSV regtest node (D6) is the in-tree node (adapters/regtest-node); here the
 * node/chain role is represented by the in-process engine + BS fake.
 */

import { parseHand, type Card, type Ruleset, type Action } from '@bsv-poker/protocol-types';
import { createHoldem } from '@bsv-poker/game-holdem';
import { p2pMesh } from './p2p-mesh.ts';
import assert from 'node:assert/strict';

function fixedDeck(): Card[] {
  const head = ['As', 'Ks', 'Ah', 'Kh', 'Qd', 'Jc', '9h', '4s', '3h'].map((c) => parseHand(c)[0]!);
  const used = new Set(head);
  const rest: Card[] = [];
  for (let c = 0; c < 52; c++) if (!used.has(c)) rest.push(c);
  return [...head, ...rest];
}

function runHand(): { transcript: Action[]; stateHash: string; payouts: unknown } {
  const ruleset: Ruleset = {
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
  const m = createHoldem({ deck: fixedDeck() });
  let s = m.init(ruleset, [
    { seat: 0, stack: 100 },
    { seat: 1, stack: 100 },
  ]);
  const transcript: Action[] = [
    { kind: 'call', seat: 0, amount: 1 },
    { kind: 'check', seat: 1, amount: 0 },
    { kind: 'check', seat: 1, amount: 0 },
    { kind: 'check', seat: 0, amount: 0 },
    { kind: 'check', seat: 1, amount: 0 },
    { kind: 'check', seat: 0, amount: 0 },
    { kind: 'check', seat: 1, amount: 0 },
    { kind: 'check', seat: 0, amount: 0 },
  ];
  for (const a of transcript) s = m.apply(s, a);
  if (!s.handComplete) throw new Error('hand did not complete');
  return { transcript, stateHash: m.stateHash(s), payouts: s.payouts };
}

/** Exercise the serverless P2P transport across a 2-node mesh: discovery (presence + gossiped table
 *  directory) and a pub/sub round-trip — the peer-to-peer replacement for the relay/indexer. */
async function exerciseNetwork(): Promise<void> {
  const mesh = await p2pMesh(2);
  const [a, b] = mesh.transports;
  try {
    // Discovery: two players announce presence; each node's gossiped view sees both.
    await a!.heartbeat('alice', '127.0.0.1:6001');
    await b!.heartbeat('bob', '127.0.0.1:6002');
    const dl = Date.now() + 5000;
    while ((await a!.listPresence()).length < 2 || (await b!.listPresence()).length < 2) {
      if (Date.now() > dl) throw new Error('presence did not gossip to both peers');
      await new Promise((r) => setTimeout(r, 50));
    }
    assert.ok((await a!.listPresence()).length >= 2, 'both players present (gossiped)');

    // Gossiped table directory: A hosts a table; B discovers it via the mesh (no server lookup).
    const tableId = `selftest-table-${Date.now()}`;
    await a!.createTable(tableId, 'Self-test HU');
    while (!(await b!.listTables()).some((t) => t.id === tableId)) {
      if (Date.now() > dl) throw new Error('table did not gossip to the far peer');
      await new Promise((r) => setTimeout(r, 50));
    }
    console.log(`[selftest] table ${tableId} discovered across the mesh via the gossiped directory.`);

    // Pub/sub round-trip: a frame published at A reaches a subscriber at B (peer-to-peer).
    const got: string[] = [];
    b!.subscribe(tableId, (t) => got.push(t));
    await a!.publish(tableId, new TextEncoder().encode('action:bet:6'));
    while (got.length === 0) {
      if (Date.now() > dl) throw new Error('published frame did not reach the peer');
      await new Promise((r) => setTimeout(r, 25));
    }
    assert.equal(got[0], 'action:bet:6', 'frame delivered peer-to-peer');
    console.log(`[selftest] pub/sub round-trip across the mesh OK: "${got[0]}"`);
  } finally {
    mesh.close();
  }
}

async function main(): Promise<void> {
  console.log('[selftest] standing up a 2-node P2P mesh (serverless transport — NO relay/indexer)…');
  console.log('[selftest] exercising serverless discovery + pub/sub across the mesh…');
  await exerciseNetwork();

  console.log('[selftest] running a full heads-up Hold\'em hand (client/engine role)…');
  const { transcript, stateHash, payouts } = runHand();
  console.log('[selftest] TRANSCRIPT:');
  transcript.forEach((a, i) => console.log(`   ${i}: seat ${a.seat} ${a.kind}${a.amount ? ' ' + a.amount : ''}`));
  console.log(`[selftest] final state hash: ${stateHash}`);
  console.log(`[selftest] payouts: ${JSON.stringify(payouts)}`);

  console.log('\n[selftest] PASS — peer-to-peer stack came up end-to-end (no server) and a full hand settled.');
}

main().then(
  () => process.exit(0),
  (e) => {
    console.error('[selftest] FAIL:', (e as Error).message);
    process.exit(1);
  },
);
