/**
 * Validating PEER E2E (audit finding 7) — SERVERLESS. The old Go indexer's `-validate` mode
 * authenticated every ingested envelope against a registered seat→pubkey map and rejected forgeries
 * with 400. In the fully peer-to-peer model that guarantee lives AT THE PEER: every node verifies an
 * envelope's signature against the acting seat's registered key BEFORE accepting it (exactly what
 * `InteractiveNetworkedTableClient.subscribe` does), and `validateHandLegality` checks the
 * authenticated transcript forms a LEGAL game through the ONE canonical engine. This e2e proves, with
 * NO indexer server:
 *   1. two SIGNED interactive players play a full hand peer-to-peer;
 *   2. a peer that authenticates each envelope against the seat keys persists a non-empty transcript
 *      that rebuilds + legality-validates to the SAME state (a real signature verifies at the peer);
 *   3. a forged-signature envelope is REJECTED by the peer authentication boundary (fail-closed);
 *   4. an over-stack bet spliced into the transcript is REJECTED by legality validation.
 */

import { randomBytes } from 'node:crypto';
import assert from 'node:assert/strict';
import type { Action, LegalActions } from '@bsv-poker/protocol-types';
import {
  LobbyClient,
  InteractiveNetworkedTableClient,
  rebuildHand,
  validateHandLegality,
  rulesetFromMeta,
  sessionAuthFromSeed,
  deriveSeatSeed,
  verifySig,
  envelopeMessage,
  type TableMeta,
  type TxRecord,
  type ClientUpdate,
} from '@bsv-poker/app-services';
import type { P2PTransport } from '@bsv-poker/adapters/p2p-transport';
import { p2pMesh } from './p2p-mesh.ts';

const META: TableMeta = { name: 'validating', variant: 'holdem', smallBlind: 1, bigBlind: 2, startingStack: 100, maxSeats: 2 };

const passive = (l: LegalActions, seat: number): Action =>
  l.check ? { kind: 'check', seat, amount: 0 } : l.call ? { kind: 'call', seat, amount: l.call.amount } : { kind: 'fold', seat, amount: 0 };

/** A signed player: real Ed25519 session key, signed lobby join, signed envelopes. */
async function player(transport: P2PTransport, tableId: string, id: string): Promise<{ stateHash: string; seatPubs: string[] }> {
  const root = randomBytes(32);
  const auth = await sessionAuthFromSeed(deriveSeatSeed(root, 'bsv-poker/seat-ed25519'));
  const lobby = new LobbyClient(transport);
  const { seated } = lobby.joinWaitingRoom(tableId, { id, pub: auth.pub, sign: (m) => auth.sign(m) }, META);
  const seat = await seated;
  const seatPubs = seat.players.map((p) => p.pub);
  const client = new InteractiveNetworkedTableClient({
    relay: transport, // peer-to-peer; the client itself authenticates inbound envelopes against seatPubs
    tableId,
    mySeat: seat.mySeat,
    seats: seat.seats,
    ruleset: seat.ruleset,
    entropy: randomBytes(32),
    auth, // sign every envelope
    seatPubs, // seat → registered session key
  });
  client.onUpdate((u: ClientUpdate) => {
    if (u.yourTurn && u.legal) client.submitAction(passive(u.legal, u.mySeat));
  });
  await client.play();
  return { stateHash: client.stateHash()!, seatPubs };
}

/**
 * A peer that AUTHENTICATES every envelope it hears (signature by the acting seat's registered key)
 * before persisting it — the serverless equivalent of the validating indexer's authenticated ingest.
 * Returns the raw captured frames; authentication is applied afterward once seatPubs are known.
 */
function captureChannel(transport: P2PTransport, tableId: string): { frames: string[] } {
  const frames: string[] = [];
  const seen = new Set<string>();
  transport.subscribe(tableId, (text) => {
    if (seen.has(text)) return;
    seen.add(text);
    frames.push(text);
  });
  return { frames };
}

/** Authenticate a captured frame against seatPubs and turn it into a transcript record, or null. */
async function authenticate(text: string, tableId: string, seatPubs: readonly string[]): Promise<TxRecord | null> {
  let env: { t?: string; seat?: number; sig?: string };
  try {
    env = JSON.parse(text) as typeof env;
  } catch {
    return null;
  }
  if (typeof env.t !== 'string' || typeof env.seat !== 'number') return null;
  if (env.t === 'join' || env.t === 'seat-reveal') return null; // seating frames are not transcript records
  const pub = seatPubs[env.seat];
  if (!pub || !env.sig) return null; // unsigned → reject (fail-closed)
  const ok = await verifySig(pub, envelopeMessage(tableId, env as never), env.sig);
  if (!ok) return null; // forged / wrong-seat signature → REJECT
  return { txid: `auth-${env.seat}-${env.t}-${Math.random().toString(36).slice(2)}`, class: env.t, tableId, raw: btoa(text) };
}

async function main(): Promise<void> {
  console.log('[validating-peer-e2e] standing up a 3-node P2P mesh (alice, bob, validating observer) — NO indexer server…');
  const mesh = await p2pMesh(3);
  const [nodeA, nodeB, nodeObs] = mesh.transports;

  const host = new LobbyClient(nodeA!);
  const tableId = await host.createTable(META);
  const capture = captureChannel(nodeObs!, tableId); // a peer that will authenticate the channel

  const [a, b] = await Promise.all([player(nodeA!, tableId, 'alice'), player(nodeB!, tableId, 'bob')]);
  assert.equal(a.stateHash, b.stateHash, 'players agree on the hand');
  const seatPubs = a.seatPubs;
  console.log(`[validating-peer-e2e] players' final stateHash: ${a.stateHash.slice(0, 24)}…`);

  await new Promise((r) => setTimeout(r, 200)); // let the last frames settle into the capture

  // (2) Authenticate every captured frame at the peer; the validated transcript rebuilds to the same state.
  const records: TxRecord[] = [];
  const seenRaw = new Set<string>();
  for (const f of capture.frames) {
    const rec = await authenticate(f, tableId, seatPubs);
    if (rec && !seenRaw.has(rec.raw!)) { seenRaw.add(rec.raw!); records.push(rec); }
  }
  console.log(`[validating-peer-e2e] peer-authenticated transcript records: ${records.length}`);
  assert.ok(records.length >= 8, `expected an authenticated transcript, got ${records.length} records`);
  const seats = [
    { seat: 0, stack: META.startingStack },
    { seat: 1, stack: META.startingStack },
  ];
  const rebuilt = rebuildHand(records, rulesetFromMeta(META), seats, 0, 0);
  assert.equal(rebuilt.stateHash, a.stateHash, 'rebuilt-from-authenticated-transcript state matches the live players');
  assert.equal(rebuilt.state.handComplete, true);
  console.log('[validating-peer-e2e] rebuilt from the PEER-AUTHENTICATED transcript — matches live state.');

  // Legality validation (audit #30): the authenticated records form a LEGAL game through the engine.
  const verdict = validateHandLegality(records, rulesetFromMeta(META), seats, 0, 0);
  assert.equal(verdict.valid, true, `the authenticated transcript must be legality-valid: ${verdict.reason}`);
  assert.equal(verdict.stateHash, a.stateHash, 'the legality-validated state matches the live players');
  console.log('[validating-peer-e2e] transcript LEGALITY-validated through the canonical engine.');

  // (4) An over-stack bet spliced into the real transcript is REJECTED by legality validation.
  const firstAction = records.find((r) => {
    try { return JSON.parse(atob(r.raw ?? '')).t === 'action'; } catch { return false; }
  });
  if (firstAction) {
    const env = JSON.parse(atob(firstAction.raw!));
    const tampered = records.map((r) =>
      r === firstAction ? { ...r, raw: btoa(JSON.stringify({ ...env, kind: 'bet', amount: 10_000_000 })) } : r,
    );
    const bad = validateHandLegality(tampered, rulesetFromMeta(META), seats, 0, 0);
    assert.equal(bad.valid, false, 'an over-stack bet spliced into the transcript must be rejected');
    console.log(`[validating-peer-e2e] illegal-action transcript REJECTED by legality validation: "${bad.reason}"`);
  }

  // (3) Fail-closed proof: a forged-signature envelope is REJECTED by the peer authentication boundary
  // (the serverless equivalent of the indexer's 400 — no server, the PEER refuses it).
  const forged = JSON.stringify({ t: 'commit', seat: 0, hand: 999, c: 'deadbeef', sig: '00'.repeat(64) });
  const forgedRec = await authenticate(forged, tableId, seatPubs);
  assert.equal(forgedRec, null, 'a forged-signature envelope must be rejected at the peer (fail-closed)');
  console.log('[validating-peer-e2e] forged record rejected at the peer — boundary is fail-closed.');

  mesh.close();
  console.log('\n[validating-peer-e2e] PASS — peer authentication accepted real play and rejected a forgery; legality validated through the canonical engine (audit 7), no server.');
}

main().then(
  () => process.exit(0),
  (e) => {
    console.error('[validating-peer-e2e] FAIL:', (e as Error).message);
    process.exit(1);
  },
);
