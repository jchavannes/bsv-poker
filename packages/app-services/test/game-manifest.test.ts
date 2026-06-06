/**
 * The SIGNED one-game key lifecycle manifest (audit #27). Executable claims that a manifest
 * binds each seat key to EXACTLY ONE content-addressed gameId, is co-signed by every seat,
 * and lets a verifier reject cross-game key reuse, replayed manifests, and cross-game
 * envelope replay. Every claim has a positive and a hostile negative case.
 */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { sessionAuthFromSeed, deriveSeatSeed, verifySig, type SessionAuth } from '../src/session-auth.ts';
import {
  buildManifest, verifyManifest, computeGameId, manifestSignMessage,
  verifyNoCrossGameReuse, assertFreshGameId, manifestSeatPubs, manifestSeatKey,
  gameBoundEnvelopeMessage, MANIFEST_VERSION,
  type GameManifest, type GameManifestBody,
} from '../src/game-manifest.ts';

const NONCE = 'ab'.repeat(32);

async function seat(n: number): Promise<SessionAuth> {
  return sessionAuthFromSeed(deriveSeatSeed(new Uint8Array(32).fill(n)));
}

async function manifest(opts?: { nonce?: string; ruleset?: string }): Promise<{ m: GameManifest; auths: SessionAuth[] }> {
  const auths = [await seat(1), await seat(2), await seat(3)];
  const body: GameManifestBody = {
    v: MANIFEST_VERSION,
    ruleset: opts?.ruleset ?? 'holdem',
    stakes: { sb: 1, bb: 2 },
    tableId: 'table-7',
    seats: auths.map((a, i) => ({ seat: i, seatPub: a.pub })),
    nonce: opts?.nonce ?? NONCE,
  };
  return { m: await buildManifest(body, auths), auths };
}

test('a co-signed manifest verifies; its gameId is content-addressed over the seat set', async () => {
  const { m } = await manifest();
  const r = await verifyManifest(m);
  assert.ok(r.ok, r.reason);
  assert.equal(m.gameId, computeGameId(m), 'gameId is SHA-256 of the canonical body');
  assert.equal(m.gameId.length, 64);
  assert.deepEqual(manifestSeatPubs(m).length, 3);
  assert.equal(manifestSeatKey(m, 1), m.seats[1]!.seatPub);
  assert.equal(manifestSeatKey(m, 99), null);
});

test('changing the seat set changes the gameId (a key cannot move to a different game)', async () => {
  const { m } = await manifest();
  const other = await seat(9);
  // swap seat 2's key for a different key → different body → different gameId
  const tampered: GameManifestBody = { ...m, seats: m.seats.map((s) => (s.seat === 2 ? { seat: 2, seatPub: other.pub } : s)) };
  assert.notEqual(computeGameId(tampered), m.gameId, 'the gameId commits to the exact seat→key set');
});

test('a tampered body (gameId no longer matches) is REJECTED', async () => {
  const { m } = await manifest();
  // keep the signatures + gameId but change the stakes → gameId no longer content-addresses the body
  const forged = { ...m, stakes: { sb: 5, bb: 10 } } as GameManifest;
  const r = await verifyManifest(forged);
  assert.equal(r.ok, false);
  assert.match(r.reason, /content-addressed|gameId/);
});

test('a missing or forged seat signature is REJECTED', async () => {
  const { m } = await manifest();
  // drop seat 0's signature
  const { 0: _omit, ...rest } = m.sigs as Record<number, string>;
  assert.equal((await verifyManifest({ ...m, sigs: rest })).ok, false);
  // forge: seat 0 signs a DIFFERENT gameId (replay another game's signature)
  const sigForOther = await (await seat(1)).sign(manifestSignMessage('cd'.repeat(32), 0, m.seats[0]!.seatPub));
  const forged = { ...m, sigs: { ...m.sigs, 0: sigForOther } } as GameManifest;
  const r = await verifyManifest(forged);
  assert.equal(r.ok, false, 'a signature over a different gameId must not satisfy this manifest');
});

test('a reused seat key across two seats is REJECTED', async () => {
  const a = await seat(1), b = await seat(2);
  const body: GameManifestBody = {
    v: MANIFEST_VERSION, ruleset: 'holdem', stakes: { sb: 1, bb: 2 }, tableId: 't',
    seats: [{ seat: 0, seatPub: a.pub }, { seat: 1, seatPub: a.pub }], // SAME key twice
    nonce: NONCE,
  };
  // sign both with a (so signatures verify) — verify must still reject the duplicate key
  const m = await buildManifest(body, [a, b]);
  const r = await verifyManifest(m);
  assert.equal(r.ok, false);
  assert.match(r.reason, /reused seat key/);
});

test('verifyNoCrossGameReuse rejects a seat key used in a prior game', async () => {
  const { m } = await manifest();
  // none of m's keys seen before → ok
  assert.ok(verifyNoCrossGameReuse(m, []).ok);
  // a registry that already saw seat 1's key → reject (keys serve at most ONE game)
  const prior = new Set([m.seats[1]!.seatPub]);
  const r = verifyNoCrossGameReuse(m, prior);
  assert.equal(r.ok, false);
  assert.match(r.reason, /prior game|ONE game/);
});

test('assertFreshGameId rejects a replayed manifest', async () => {
  const { m } = await manifest();
  assert.ok(assertFreshGameId(m, []).ok);
  assert.equal(assertFreshGameId(m, [m.gameId]).ok, false);
});

test('two manifests with different nonces are different games (fresh keys each game)', async () => {
  const { m: g1 } = await manifest({ nonce: 'ab'.repeat(32) });
  const { m: g2 } = await manifest({ nonce: 'cd'.repeat(32) });
  assert.notEqual(g1.gameId, g2.gameId, 'a fresh nonce yields a distinct game');
  // both verify independently
  assert.ok((await verifyManifest(g1)).ok);
  assert.ok((await verifyManifest(g2)).ok);
});

test('gameBoundEnvelopeMessage binds the gameId: a signature for game A does not verify for game B', async () => {
  const { m: gA, auths } = await manifest({ nonce: 'ab'.repeat(32) });
  const { m: gB } = await manifest({ nonce: 'cd'.repeat(32) });
  const env = { t: 'action', seat: 0, hand: 0, kind: 'bet', amount: 2 } as const;
  const msgA = gameBoundEnvelopeMessage(gA.gameId, gA.tableId, env);
  const msgB = gameBoundEnvelopeMessage(gB.gameId, gB.tableId, env);
  assert.notEqual(msgA, msgB, 'the gameId is folded into the per-envelope message');
  const sigA = await auths[0]!.sign(msgA);
  assert.ok(await verifySig(auths[0]!.pub, msgA, sigA), 'valid for game A');
  assert.equal(await verifySig(auths[0]!.pub, msgB, sigA), false, 'NOT valid for game B (cross-game replay blocked)');
});

test('verifyManifest is TOTAL on hostile input (never throws)', async () => {
  for (const bad of [null, 42, {}, { v: 'x' }, { v: MANIFEST_VERSION }, [], 'nope']) {
    const r = await verifyManifest(bad);
    assert.equal(r.ok, false);
  }
});
