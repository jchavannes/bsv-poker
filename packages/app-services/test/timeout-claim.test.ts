/**
 * Accountable action timeout (audit finding 3) — the LIVE drop-and-continue mechanism.
 *
 * A seat that stops acting must not freeze the table forever, yet it must NOT be droppable
 * prematurely or by a forged message. The mechanism: each reveal/action is anchored to a shared,
 * monotone chain height (`h`); a seat may be dropped with the engine's check-or-fold DEFAULT only
 * once that shared height passes an anchored deadline (`floor + window`), evidenced by a peer's
 * Ed25519-signed `timeout-claim` (signed BY the claimant, ABOUT the subject). Both honest clients
 * re-derive the identical default and converge byte-for-byte (P2). These tests drive real signed
 * InteractiveNetworkedTableClient instances over an in-memory relay with a test-controlled clock.
 */

import { test } from 'node:test';
import assert from 'node:assert/strict';
import { randomBytes } from 'node:crypto';
import { InteractiveNetworkedTableClient, type ClientUpdate, type TablePlayer } from '../src/interactive-client.ts';
import { offlineRuleset, universalBot } from '../src/offline.ts';
import { sessionAuthFromSeed, envelopeMessage, type SessionAuth } from '../src/session-auth.ts';
import type { RelayClient } from '../src/network.ts';

/** In-memory relay hub: publish fans an opaque frame out to EVERY subscriber, INCLUDING the
 *  publisher (the real relay echoes a table channel to all its subscribers; clients rely on seeing
 *  their own commit/reveal). Delivery is async (microtask) like a network stream. */
class MemHub {
  private readonly subs = new Map<string, Set<(t: string) => void>>();
  subscribe(table: string, cb: (t: string) => void): () => void {
    let set = this.subs.get(table);
    if (!set) this.subs.set(table, (set = new Set()));
    set.add(cb);
    return () => set!.delete(cb);
  }
  publish(table: string, bytes: Uint8Array): number {
    const text = new TextDecoder().decode(bytes);
    const set = this.subs.get(table);
    if (!set) return 0;
    for (const cb of [...set]) queueMicrotask(() => cb(text));
    return set.size;
  }
  /** Inject a raw frame as if some party published it (for forged-message tests). */
  inject(table: string, text: string): void {
    this.publish(table, new TextEncoder().encode(text));
  }
}

/** A RelayClient-shaped view over the shared hub, used by one client. */
function relayOver(hub: MemHub): RelayClient {
  return {
    subscribe: (table: string, cb: (t: string) => void) => hub.subscribe(table, cb),
    publish: async (table: string, bytes: Uint8Array) => hub.publish(table, bytes),
  } as unknown as RelayClient;
}

/**
 * A shared, monotone chain height the TEST drives. While the gate is shut it reads 0 — the fixed
 * floor under which the commit/reveal handshake completes and below which NO deadline can pass. Once
 * opened it advances GRADUALLY on the wall-clock (one block per `tickMs`), modelling the real
 * premise that chain height is a slow clock while the relay propagates actions near-instantly: an
 * honest seat's action (delivered in a microtask) always beats its deadline, so only a seat that
 * NEVER acts is eventually passed. All clients read the same height (the same shared chain).
 */
class GatedHeight {
  private openedAt = 0;
  private readonly tickMs: number;
  constructor(tickMs: number) {
    this.tickMs = tickMs;
  }
  open(): void {
    this.openedAt = Date.now();
  }
  source = async (): Promise<number> => (this.openedAt === 0 ? 0 : Math.floor((Date.now() - this.openedAt) / this.tickMs));
}

const TABLE = 'tbl-timeout';
const WINDOW = 3;

interface Seat {
  auth: SessionAuth;
  client: InteractiveNetworkedTableClient;
}

async function buildTable(hub: MemHub, source: () => Promise<number>, stallSeat: number, aggressorSeat = 0): Promise<Seat[]> {
  const ruleset = offlineRuleset('holdem', 3);
  const seatDefs: TablePlayer[] = [0, 1, 2].map((seat) => ({ seat, stack: 100 }));
  const auths = await Promise.all([0, 1, 2].map((i) => sessionAuthFromSeed(new Uint8Array(32).fill(i + 1))));
  const pubs = auths.map((a) => a.pub);
  // The aggressor opens a raise/bet whenever legal, so the stalled seat faces a wager and its timeout
  // DEFAULT is a fold (checking is not free) — the strongest demonstration of the drop.
  const strategyFor = (legal: ClientUpdate['legal'], seat: number) => {
    if (seat === aggressorSeat && legal) {
      if (legal.raise) return { kind: 'raise' as const, seat, amount: legal.raise.min };
      if (legal.bet) return { kind: 'bet' as const, seat, amount: legal.bet.min };
    }
    return universalBot(legal!, seat);
  };
  return auths.map((auth, mySeat) => {
    const client = new InteractiveNetworkedTableClient({
      relay: relayOver(hub),
      tableId: TABLE,
      mySeat,
      seats: seatDefs,
      ruleset,
      entropy: randomBytes(32),
      auth,
      seatPubs: pubs,
      heightSource: source,
      timeoutWindow: WINDOW,
    });
    // Honest seats auto-play; the stall seat NEVER acts on its turn (its promise hangs).
    client.onUpdate((u: ClientUpdate) => {
      if (u.yourTurn && u.legal && mySeat !== stallSeat) client.submitAction(strategyFor(u.legal, u.mySeat));
    });
    return { auth, client };
  });
}

/** Wait until every client has initialised its hand state (the commit/reveal handshake is done). */
async function awaitHandshake(seats: Seat[]): Promise<void> {
  const start = Date.now();
  while (!seats.every((s) => s.client.getState() !== null)) {
    if (Date.now() - start > 5000) throw new Error('handshake did not complete');
    await new Promise((r) => setTimeout(r, 10));
  }
}

test('a stalled seat is dropped at the anchored deadline and the two honest clients CONVERGE (audit 3)', async () => {
  const hub = new MemHub();
  const height = new GatedHeight(40); // one block every 40ms once opened
  const seats = await buildTable(hub, height.source, 2);

  const h0 = seats[0]!.client.play();
  const h1 = seats[1]!.client.play();
  void seats[2]!.client.play(); // joins the handshake, then stalls forever on its turn

  await awaitHandshake(seats);
  height.open(); // let the shared clock pass the anchored deadline

  const [s0, s1] = await Promise.all([h0, h1]);
  seats[2]!.client.abort();

  // Byte-identical convergence (P2): both honest clients reached the same final state.
  assert.equal(seats[0]!.client.stateHash(), seats[1]!.client.stateHash(), 'honest clients diverged after the drop');
  assert.equal(s0.handComplete, true);
  assert.equal(s1.handComplete, true);
  // The stalled seat was folded by the default — it did not contest the pot.
  assert.equal(s0.seats.find((p) => p.seat === 2)!.folded, true, 'the stalled seat should have been folded by the timeout default');
});

test('NO premature drop while below the deadline; a forged low-deadline claim is rejected (audit 3)', async () => {
  const hub = new MemHub();
  const height = new GatedHeight(40); // gate stays CLOSED below → height reads 0 (< any deadline)
  const seats = await buildTable(hub, height.source, 2);

  const h0 = seats[0]!.client.play();
  const h1 = seats[1]!.client.play();
  void seats[2]!.client.play();
  await awaitHandshake(seats);

  // Inject a properly-SIGNED claim from seat 1 against the stalled seat 2 with d=0 — below the
  // deadline (floor 0 + window 3). An honest client must reject it on BOTH guards (d >= floor+window
  // is false, and the chain height 0 >= d=0 alone is not enough), so seat 2 must NOT be dropped.
  const forged = { t: 'timeout-claim', seat: 1, hand: 0, subject: 2, d: 0 };
  const sig = await seats[1]!.auth.sign(envelopeMessage(TABLE, forged));
  hub.inject(TABLE, JSON.stringify({ ...forged, sig }));

  // Grace period: with the clock pinned below the deadline and only a forged claim present, the
  // table must stay frozen — neither honest client completes and seat 2 is not folded.
  await new Promise((r) => setTimeout(r, 400));
  assert.equal(seats[0]!.client.getState()!.handComplete, false, 'completed prematurely — dropped a seat before the deadline');
  assert.equal(seats[0]!.client.getState()!.seats.find((p) => p.seat === 2)!.folded, false, 'forged/premature claim wrongly dropped the seat');

  // Now legitimately pass the deadline → the seat is dropped and the honest clients converge.
  height.open();
  const [s0] = await Promise.all([h0, h1]);
  seats[2]!.client.abort();
  assert.equal(seats[0]!.client.stateHash(), seats[1]!.client.stateHash(), 'honest clients diverged');
  assert.equal(s0.seats.find((p) => p.seat === 2)!.folded, true);
});
