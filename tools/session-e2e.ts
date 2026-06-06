/**
 * Continuous multi-hand session E2E (v3): a real ongoing table. Two players join and play a
 * BOUNDED session of several hands PEER-TO-PEER — fresh per-hand shuffle (REQ-CRYPTO-010), carried
 * stacks, rotating button — and the table's total chips are conserved across hands while both clients
 * stay in lockstep (no divergence error from playSession means every hand converged). Each player is
 * its OWN P2P node; there is NO relay server and NO indexer server.
 */

import { randomBytes } from 'node:crypto';
import assert from 'node:assert/strict';
import type { Action, LegalActions } from '@bsv-poker/protocol-types';
import {
  LobbyClient,
  InteractiveNetworkedTableClient,
  type TableMeta,
  type ClientUpdate,
} from '@bsv-poker/app-services';
import type { P2PTransport } from '@bsv-poker/adapters/p2p-transport';
import { p2pMesh } from './p2p-mesh.ts';

const HANDS = 3;
const META: TableMeta = {
  name: 'session test',
  variant: 'holdem',
  smallBlind: 1,
  bigBlind: 2,
  startingStack: 100,
  maxSeats: 2,
};

const passive = (l: LegalActions, seat: number): Action =>
  l.check ? { kind: 'check', seat, amount: 0 } : l.call ? { kind: 'call', seat, amount: l.call.amount } : { kind: 'fold', seat, amount: 0 };

async function player(transport: P2PTransport, tableId: string, id: string): Promise<number[]> {
  const lobby = new LobbyClient(transport);
  const { seated } = lobby.joinWaitingRoom(tableId, { id, pub: randomBytes(33).toString('hex') }, META, undefined, { allowUnsigned: true });
  const seat = await seated;
  const client = new InteractiveNetworkedTableClient({
    relay: transport,
    tableId,
    mySeat: seat.mySeat,
    seats: seat.seats,
    ruleset: seat.ruleset,
    entropy: randomBytes(32),
    allowUnsigned: true, // test fixture (audit 1)
  });
  let last: ClientUpdate | null = null;
  client.onUpdate((u) => {
    last = u;
    if (u.yourTurn && u.legal) client.submitAction(passive(u.legal, u.mySeat));
  });
  await client.playSession({ maxHands: HANDS });
  // final per-seat stacks as this client saw them
  return last!.state.seats.map((s) => s.stack);
}

async function main(): Promise<void> {
  console.log(`[session-e2e] standing up a 2-node P2P mesh; playing a ${HANDS}-hand session (NO server)…`);
  const mesh = await p2pMesh(2);
  const host = new LobbyClient(mesh.transports[0]!);
  const tableId = await host.createTable(META);

  const [a, b] = await Promise.all([
    player(mesh.transports[0]!, tableId, 'alice'),
    player(mesh.transports[1]!, tableId, 'bob'),
  ]);
  console.log(`[session-e2e] alice's view of final stacks: [${a.join(', ')}]`);
  console.log(`[session-e2e] bob's view of final stacks:   [${b.join(', ')}]`);
  // both saw the same final stacks (lockstep across all hands), and total chips conserved
  assert.deepEqual(a, b, 'both players agree on final stacks after the session');
  assert.equal(a.reduce((x, y) => x + y, 0), META.startingStack * 2, 'total chips conserved across hands');
  mesh.close();
  console.log(`\n[session-e2e] PASS — ${HANDS}-hand continuous table peer-to-peer; players in lockstep, chips conserved, no server.`);
}

main().then(
  () => process.exit(0),
  (e) => {
    console.error('[session-e2e] FAIL:', (e as Error).message);
    process.exit(1);
  },
);
