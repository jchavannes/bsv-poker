/**
 * Variant-generic, multi-seat multiplayer E2E (v3). Proves real players can choose a variant and
 * sit N-handed: runs a 3-handed Texas Hold'em and a 2-handed Omaha (plus stud/draw/razz) PEER-TO-PEER
 * through the lobby + interactive client, asserting all players converge byte-for-byte (REQ-TEST-002).
 * Each player is its OWN P2P node — there is NO relay server and NO indexer server.
 *
 * (Hold'em/Omaha use the check/bet/call/raise/fold action set, so a passive auto-strategy drives
 * them headlessly. Stud/Razz/Draw add bring-in/draw actions that a human supplies in the UI; their
 * engines are covered by the module unit tests — this harness proves the networked generic path.)
 */

import { randomBytes } from 'node:crypto';
import assert from 'node:assert/strict';
import type { Variant } from '@bsv-poker/protocol-types';
import {
  LobbyClient,
  InteractiveNetworkedTableClient,
  universalBot,
  type TableMeta,
  type ClientUpdate,
} from '@bsv-poker/app-services';
import type { P2PTransport } from '@bsv-poker/adapters/p2p-transport';
import { p2pMesh } from './p2p-mesh.ts';

async function joinAndPlay(transport: P2PTransport, tableId: string, meta: TableMeta, id: string): Promise<string> {
  const lobby = new LobbyClient(transport);
  const pub = randomBytes(33).toString('hex');
  const { seated } = lobby.joinWaitingRoom(tableId, { id, pub }, meta, undefined, { allowUnsigned: true });
  const seat = await seated;
  const client = new InteractiveNetworkedTableClient({
    relay: transport, // the player's own P2P node — no server
    tableId,
    mySeat: seat.mySeat,
    seats: seat.seats,
    ruleset: seat.ruleset,
    entropy: randomBytes(32),
    allowUnsigned: true, // test fixture (audit 1)
  });
  client.onUpdate((u: ClientUpdate) => {
    if (u.yourTurn && u.legal) client.submitAction(universalBot(u.legal, u.mySeat));
  });
  await client.play();
  return client.stateHash()!;
}

async function scenario(variant: Variant, seats: number): Promise<void> {
  const meta: TableMeta = {
    name: `${variant} ${seats}-handed`,
    variant,
    smallBlind: 1,
    bigBlind: 2,
    startingStack: 100,
    maxSeats: seats,
  };
  // One P2P node per seat (each player is a real, separate peer); host on node 0.
  const mesh = await p2pMesh(seats);
  try {
    const host = new LobbyClient(mesh.transports[0]!);
    const tableId = await host.createTable(meta);
    console.log(`\n[multi-e2e] ${variant} ${seats}-handed → table ${tableId}`);
    const hashes = await Promise.all(
      Array.from({ length: seats }, (_, i) => joinAndPlay(mesh.transports[i]!, tableId, meta, `p${i}`)),
    );
    for (let i = 1; i < hashes.length; i++) {
      assert.equal(hashes[i], hashes[0], `${variant}: seat ${i} diverged`);
    }
    console.log(`[multi-e2e] ${variant} ${seats}-handed: all ${seats} players converged → ${hashes[0]!.slice(0, 20)}…`);
  } finally {
    mesh.close();
  }
}

async function main(): Promise<void> {
  console.log('[multi-e2e] running multiplayer scenarios fully peer-to-peer (NO relay/indexer server)…');
  await scenario('holdem', 3); // multi-seat (3-handed)
  await scenario('omaha', 2); // 4 hole cards, 2+3
  await scenario('stud', 2); // ante + bring-in, up/down cards
  await scenario('draw', 2); // discard + redraw
  await scenario('razz', 2); // ace-to-five low

  console.log('\n[multi-e2e] PASS — ALL FIVE variants play multiplayer peer-to-peer (incl. 3-handed), no server.');
}

main().then(
  () => process.exit(0),
  (e) => {
    console.error('[multi-e2e] FAIL:', (e as Error).message);
    process.exit(1);
  },
);
