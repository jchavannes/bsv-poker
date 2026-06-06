/**
 * Fully serverless LOBBY + seating + hand — NO SERVER, NO CAST. This closes the gap the central
 * relay used to fill that a bare gossip transport did not: DISCOVERY. Three real players, each its
 * own P2P node in a chain A—B—C (A and C never directly connect), do the WHOLE flow peer-to-peer:
 *
 *   1. Host A hosts a table via `LobbyClient.createTable` — which gossips a directory announce.
 *   2. B and C DISCOVER the table via `LobbyClient.listTables` — the gossiped directory, no server
 *      lookup. The announce reaches C only by flooding across the mesh (A→B→C), proving serverless
 *      discovery works over an arbitrary connected graph.
 *   3. All three join the waiting room with SIGNED (Ed25519) commit-reveal seating (audit 2/3/#27)
 *      and agree on the identical seat order.
 *   4. All three play an interactive hand and converge byte-for-byte (REQ-TEST-002).
 *
 * The `relay` each client uses is a `P2PTransport` passed DIRECTLY (it is a structural `Relay`) —
 * there is no relay-go, no indexer-go, no HTTP, no cast. If this passes, a real player can find and
 * play a game with no server process anywhere.
 */
import assert from 'node:assert/strict';
import { P2PTransport } from '@bsv-poker/adapters/p2p-transport';
import {
  LobbyClient,
  InteractiveNetworkedTableClient,
  sessionAuthFromSeed,
  universalBot,
  type TableMeta,
  type ClientUpdate,
  type SessionAuth,
} from '@bsv-poker/app-services';

const META: TableMeta = {
  name: 'Serverless 6-max (P2P)',
  variant: 'holdem',
  smallBlind: 1,
  bigBlind: 2,
  startingStack: 100,
  maxSeats: 3,
};

async function discover(lobby: LobbyClient, name: string, timeoutMs: number): Promise<string> {
  const deadline = Date.now() + timeoutMs;
  for (;;) {
    const open = await lobby.listTables();
    const hit = open.find((t) => t.meta.name === name);
    if (hit) return hit.id;
    if (Date.now() > deadline) throw new Error(`table "${name}" never appeared in the gossiped directory`);
    await new Promise((r) => setTimeout(r, 100));
  }
}

async function playSeated(
  transport: P2PTransport,
  auth: SessionAuth,
  id: string,
  tableId: string,
  entropySeed: number,
): Promise<{ stateHash: string; chips: number; mySeat: number }> {
  const lobby = new LobbyClient(transport);
  const { seated } = lobby.joinWaitingRoom(
    tableId,
    { id, pub: auth.pub, sign: (msg) => auth.sign(msg) }, // SIGNED join (audit 2/3) — no test fixture
    META,
    (players) => console.log(`[${id}] waiting room: ${players.length}/${META.maxSeats}`),
  );
  const seat = await seated;
  const seatPubs = seat.players.map((p) => p.pub); // seat-ordered → seatPubs[mySeat] === auth.pub
  console.log(`[${id}] seated at seat ${seat.mySeat}/${seat.seats.length}`);

  const client = new InteractiveNetworkedTableClient({
    relay: transport, // the P2P mesh IS the transport — structural Relay, no cast, no server
    tableId,
    mySeat: seat.mySeat,
    seats: seat.seats,
    ruleset: seat.ruleset,
    entropy: Uint8Array.from(Array.from({ length: 32 }, (_, i) => (i * entropySeed + 3) % 251)),
    auth,
    seatPubs,
  });
  client.onUpdate((u: ClientUpdate) => {
    if (u.yourTurn && u.legal) client.submitAction(universalBot(u.legal, u.mySeat));
  });
  const final = await client.play();
  const chips = final.seats.reduce((a, p) => a + p.stack, 0);
  return { stateHash: client.stateHash()!, chips, mySeat: seat.mySeat };
}

async function main(): Promise<void> {
  // Three peers in a CHAIN A—B—C (A and C never directly connect).
  const portA = 9401, portB = 9402, portC = 9403;
  const tA = new P2PTransport(portA);
  const tB = new P2PTransport(portB);
  const tC = new P2PTransport(portC);
  await tA.start([]);
  await tB.start([{ host: '127.0.0.1', port: portA }]);
  await tC.start([{ host: '127.0.0.1', port: portB }]);

  const dl = Date.now() + 10_000;
  while (!(tA.peerCount() >= 1 && tB.peerCount() >= 2 && tC.peerCount() >= 1)) {
    if (Date.now() > dl) throw new Error(`mesh did not form: A=${tA.peerCount()} B=${tB.peerCount()} C=${tC.peerCount()}`);
    await new Promise((r) => setTimeout(r, 50));
  }
  console.log(`[p2p-lobby] mesh up (chain A—B—C) — NO server.`);

  // Distinct identities for the three players.
  const [authA, authB, authC] = await Promise.all([
    sessionAuthFromSeed(new Uint8Array(32).fill(41)),
    sessionAuthFromSeed(new Uint8Array(32).fill(42)),
    sessionAuthFromSeed(new Uint8Array(32).fill(43)),
  ]);

  // 1) Host A hosts a table — gossips a directory announce.
  const hostLobby = new LobbyClient(tA);
  const tableId = await hostLobby.createTable(META);
  console.log(`[p2p-lobby] host A created table ${tableId}; gossiping it to the mesh…`);

  // 2) B and C DISCOVER it via the gossiped directory (announce must flood A→B→C to reach C).
  const lobbyB = new LobbyClient(tB);
  const lobbyC = new LobbyClient(tC);
  const [seenByB, seenByC] = await Promise.all([
    discover(lobbyB, META.name, 8_000),
    discover(lobbyC, META.name, 8_000),
  ]);
  assert.equal(seenByB, tableId, 'B discovered a different table id than A hosted');
  assert.equal(seenByC, tableId, 'C (far end of the chain) must discover the table via gossip — serverless directory');
  console.log(`[p2p-lobby] B and C discovered table ${tableId} via the gossiped directory (no server lookup).`);

  // 3) + 4) All three join the (signed) waiting room and play a hand entirely peer-to-peer.
  const results = await Promise.all([
    playSeated(tA, authA!, 'alice', tableId, 7),
    playSeated(tB, authB!, 'bob', tableId, 19),
    playSeated(tC, authC!, 'carol', tableId, 31),
  ]);

  for (let i = 1; i < results.length; i++) {
    assert.equal(results[i]!.stateHash, results[0]!.stateHash, `player ${i} diverged from player 0`);
  }
  assert.equal(results[0]!.chips, 3 * META.startingStack, 'chips conserved');
  console.log(`[p2p-lobby] all three converged: stateHash ${results[0]!.stateHash.slice(0, 16)}…, chips ${results[0]!.chips}.`);

  for (const t of [tA, tB, tC]) t.close();
  console.log('\n[p2p-lobby] PASS — find a game AND play it FULLY peer-to-peer: serverless directory discovery + signed commit-reveal seating + a converged hand, across a gossip mesh, with no relay/indexer/server and no cast.');
}

main().then(
  () => process.exit(0),
  (e) => {
    console.error('[p2p-lobby] FAIL:', (e as Error).stack ?? (e as Error).message);
    process.exit(1);
  },
);
