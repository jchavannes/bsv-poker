/**
 * Reconnect / resume E2E (core §8.6/§12.3, REQ-NET-007, REQ-DATA-002/003) — SERVERLESS. Two players
 * play a hand peer-to-peer. A THIRD peer (the "observer") is also on the mesh and PERSISTS the table
 * channel it witnesses into a local transcript — exactly what every node does so it can resume. A
 * reconnecting client then rebuilds the hand's final state FROM that peer-held transcript and it
 * matches the players' state byte-for-byte. There is NO indexer server — the transcript is a peer's
 * own persisted view of the gossiped channel (the serverless replacement for the canonical store).
 */

import { randomBytes } from 'node:crypto';
import assert from 'node:assert/strict';
import type { Action, LegalActions } from '@bsv-poker/protocol-types';
import {
  LobbyClient,
  InteractiveNetworkedTableClient,
  rebuildHand,
  rulesetFromMeta,
  type TableMeta,
  type TxRecord,
  type ClientUpdate,
} from '@bsv-poker/app-services';
import type { P2PTransport } from '@bsv-poker/adapters/p2p-transport';
import { p2pMesh } from './p2p-mesh.ts';

const META: TableMeta = { name: 'reconnect', variant: 'holdem', smallBlind: 1, bigBlind: 2, startingStack: 100, maxSeats: 2 };

const passive = (l: LegalActions, seat: number): Action =>
  l.check ? { kind: 'check', seat, amount: 0 } : l.call ? { kind: 'call', seat, amount: l.call.amount } : { kind: 'fold', seat, amount: 0 };

async function player(transport: P2PTransport, tableId: string, id: string): Promise<string> {
  const lobby = new LobbyClient(transport);
  const { seated } = lobby.joinWaitingRoom(tableId, { id, pub: randomBytes(33).toString('hex') }, META, undefined, { allowUnsigned: true });
  const seat = await seated;
  const client = new InteractiveNetworkedTableClient({
    relay: transport, // peer-to-peer table channel — no relay/indexer server
    tableId,
    mySeat: seat.mySeat,
    seats: seat.seats,
    ruleset: seat.ruleset,
    entropy: randomBytes(32),
    allowUnsigned: true, // test fixture (audit 1)
  });
  client.onUpdate((u: ClientUpdate) => {
    if (u.yourTurn && u.legal) client.submitAction(passive(u.legal, u.mySeat));
  });
  await client.play();
  return client.stateHash()!;
}

/**
 * A peer that persists the table channel into a transcript (what every node keeps so it can resume).
 * Dedups identical frames (the protocol re-broadcasts commits/reveals each tick), giving the same
 * record set a canonical store would hold — but sourced entirely from the gossiped channel, no server.
 */
function persistChannel(transport: P2PTransport, tableId: string): { transcript: TxRecord[] } {
  const transcript: TxRecord[] = [];
  const seen = new Set<string>();
  let seq = 0;
  transport.subscribe(tableId, (text) => {
    if (seen.has(text)) return; // collapse the re-broadcasts (idempotent persistence)
    seen.add(text);
    let cls = 'unknown';
    try {
      cls = (JSON.parse(text) as { t?: string }).t ?? 'unknown';
    } catch {
      return; // not a protocol envelope
    }
    transcript.push({ txid: `rec-${seq++}`, class: cls, tableId, raw: btoa(text) });
  });
  return { transcript };
}

async function main(): Promise<void> {
  console.log('[reconnect-e2e] standing up a 3-node P2P mesh (alice, bob, observer) — NO server…');
  const mesh = await p2pMesh(3);
  const [nodeA, nodeB, nodeObserver] = mesh.transports;

  const host = new LobbyClient(nodeA!);
  const tableId = await host.createTable(META);
  // The observer joins the channel BEFORE the hand and persists everything it hears (its local store).
  const observer = persistChannel(nodeObserver!, tableId);

  const [a, b] = await Promise.all([player(nodeA!, tableId, 'alice'), player(nodeB!, tableId, 'bob')]);
  assert.equal(a, b, 'players agree on the hand');
  console.log(`[reconnect-e2e] players' final stateHash: ${a.slice(0, 24)}…`);

  // Give the last gossiped frames a moment to settle into the observer's transcript.
  await new Promise((r) => setTimeout(r, 200));
  const records = observer.transcript;
  console.log(`[reconnect-e2e] observer persisted ${records.length} transcript records from the gossiped channel`);
  const seats = [
    { seat: 0, stack: META.startingStack },
    { seat: 1, stack: META.startingStack },
  ];
  const byClass: Record<string, number> = {};
  for (const r of records) byClass[r.class] = (byClass[r.class] ?? 0) + 1;
  console.log(`[reconnect-e2e] record classes: ${JSON.stringify(byClass)}`);
  const rebuilt = rebuildHand(records, rulesetFromMeta(META), seats, 0, 0);
  console.log(`[reconnect-e2e] rebuilt: complete=${rebuilt.state.handComplete} board=${rebuilt.state.board.length} stacks=${rebuilt.state.seats.map((s) => s.stack).join(',')}`);
  console.log(`[reconnect-e2e] rebuilt-from-transcript stateHash: ${rebuilt.stateHash.slice(0, 24)}…`);
  assert.equal(rebuilt.stateHash, a, 'reconnect: rebuilt-from-transcript state matches the live players');
  assert.equal(rebuilt.state.handComplete, true);
  mesh.close();
  console.log('\n[reconnect-e2e] PASS — rejoined from a PEER-held transcript and rebuilt identical state (REQ-NET-007), no server.');
}

main().then(
  () => process.exit(0),
  (e) => {
    console.error('[reconnect-e2e] FAIL:', (e as Error).message);
    process.exit(1);
  },
);
