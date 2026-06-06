/**
 * Waiting-room + real multiplayer E2E (app §A6.3/§A6.5/§A7) — proves two REAL players (not a bot)
 * find a table, join the waiting room, get seated by agreement, and play a full hand interactively
 * over the PEER-TO-PEER mesh, converging byte-for-byte (REQ-TEST-002).
 *
 *   Host creates a 2-seat table (gossiped directory announce) → both join the waiting room → seats
 *   agreed → both run the interactive client (a scripted human acts on each turn) → identical final
 *   state. There is NO relay server and NO indexer server — each player is its own P2P node.
 */

import assert from 'node:assert/strict';
import type { Action, LegalActions } from '@bsv-poker/protocol-types';
import {
  LobbyClient,
  InteractiveNetworkedTableClient,
  type TableMeta,
  type ClientUpdate,
} from '@bsv-poker/app-services';
import { P2PTransport } from '@bsv-poker/adapters/p2p-transport';
import { p2pMesh } from './p2p-mesh.ts';

const passive = (legal: LegalActions, seat: number): Action => {
  if (legal.check) return { kind: 'check', seat, amount: 0 };
  if (legal.call) return { kind: 'call', seat, amount: legal.call.amount };
  return { kind: 'fold', seat, amount: 0 };
};

const META: TableMeta = {
  name: 'Friday night HU',
  variant: 'holdem',
  smallBlind: 1,
  bigBlind: 2,
  startingStack: 100,
  maxSeats: 2,
};

async function player(
  transport: P2PTransport,
  me: { id: string; pub: string },
  tableId: string,
  entropySeed: number,
): Promise<{ stateHash: string }> {
  const lobby = new LobbyClient(transport);
  const { seated } = lobby.joinWaitingRoom(
    tableId,
    me,
    META,
    (players) => console.log(`[${me.id}] waiting room now has ${players.length} player(s): ${players.map((p) => p.id).join(', ')}`),
    { allowUnsigned: true }, // test fixture (audit 2): unsigned join
  );
  const seat = await seated;
  console.log(`[${me.id}] seated at seat ${seat.mySeat} of ${seat.seats.length}`);

  const client = new InteractiveNetworkedTableClient({
    relay: transport, // the player's own P2P node — structural Relay, no server
    tableId,
    mySeat: seat.mySeat,
    seats: seat.seats,
    ruleset: seat.ruleset,
    entropy: Uint8Array.from(Array.from({ length: 32 }, (_, i) => (i * entropySeed + 3) % 251)),
    allowUnsigned: true, // test fixture (audit 1)
  });
  // The "human": act on every turn via the UI-facing update stream.
  client.onUpdate((u: ClientUpdate) => {
    if (u.yourTurn && u.legal) client.submitAction(passive(u.legal, u.mySeat));
  });
  await client.play();
  return { stateHash: client.stateHash()! };
}

async function main(): Promise<void> {
  console.log('[lobby-e2e] standing up a 2-node P2P mesh (NO relay/indexer server)…');
  const mesh = await p2pMesh(2);
  const [nodeAlice, nodeBob] = mesh.transports;

  // Host (Alice's node) creates the table; the announce gossips across the mesh.
  const host = new LobbyClient(nodeAlice!);
  const tableId = await host.createTable(META);
  console.log(`[lobby-e2e] host created table ${tableId} (${META.name}); discovering via gossip…`);
  // Bob's node must SEE it via the gossiped directory before joining.
  const bobLobby = new LobbyClient(nodeBob!);
  const dl = Date.now() + 8000;
  for (;;) {
    if ((await bobLobby.listTables()).some((t) => t.id === tableId)) break;
    if (Date.now() > dl) throw new Error('table never appeared in the gossiped directory');
    await new Promise((r) => setTimeout(r, 100));
  }
  for (const t of await host.listTables()) console.log(`   - ${t.id}: ${t.meta.name} (${t.meta.maxSeats} seats)`);

  // Two real players discover + join the waiting room and play — each on its own P2P node.
  const [a, b] = await Promise.all([
    player(nodeAlice!, { id: 'alice', pub: '02aa' }, tableId, 7),
    player(nodeBob!, { id: 'bob', pub: '03bb' }, tableId, 19),
  ]);

  console.log(`[lobby-e2e] alice final stateHash: ${a.stateHash.slice(0, 24)}…`);
  console.log(`[lobby-e2e] bob   final stateHash: ${b.stateHash.slice(0, 24)}…`);
  assert.equal(a.stateHash, b.stateHash, 'both players converged on identical state');
  mesh.close();
  console.log('\n[lobby-e2e] PASS — two players joined a waiting room and played a real hand peer-to-peer (no bot, no server).');
}

main().then(
  () => process.exit(0),
  (e) => {
    console.error('[lobby-e2e] FAIL:', (e as Error).message);
    process.exit(1);
  },
);
