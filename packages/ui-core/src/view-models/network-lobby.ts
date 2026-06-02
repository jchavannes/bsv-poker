/**
 * Networked-lobby view-models (§A6.3/§A7) — pure helpers for the relay-backed multiplayer flow:
 * a per-session identity (random player id + identity pubkey hex used for deterministic seat
 * ordering), validation of the create-table form into a relay TableMeta shape, and a pure
 * waiting-room status projection. No React, no I/O — strip-friendly for `node --test`.
 *
 * NOTE: the "pub" here is a random per-session hex used ONLY for seat ordering in the waiting
 * room (LobbyClient sorts joined players by pub). It is NOT a real secp256k1 identity key — the
 * on-chain custody/identity path is the Node SDK path (§A2.3), not this browser bundle.
 */

/** The TableMeta shape consumed by app-services LobbyClient (kept structural to avoid importing
 * app-services into ui-core — ui-core must stay free of app-services). */
export interface NetworkTableMeta {
  readonly name: string;
  readonly variant: 'holdem';
  readonly smallBlind: number;
  readonly bigBlind: number;
  readonly startingStack: number;
  readonly maxSeats: number;
}

export interface SessionIdentity {
  /** Human-facing player id (e.g. "player-3f9a"). */
  readonly id: string;
  /** Random hex used for deterministic seat ordering (NOT a real key — see file header). */
  readonly pub: string;
}

export interface NetworkTableForm {
  readonly name: string;
  readonly smallBlind: number;
  readonly bigBlind: number;
  readonly startingStack: number;
  readonly maxSeats: number;
}

export interface NetworkTableValidation {
  readonly ok: boolean;
  readonly errors: readonly string[];
}

/** Random-byte source — defaults to the platform crypto (browser & Node 24). Injectable for tests. */
export type RandomBytes = (n: number) => Uint8Array;

/** Minimal structural view of the Web Crypto getRandomValues (present in browsers and Node 24),
 * typed locally so this module needs neither the DOM `lib` nor a named `Crypto` global type. */
interface RandomSource {
  getRandomValues<T extends ArrayBufferView>(array: T): T;
}

const defaultRandomBytes: RandomBytes = (n: number): Uint8Array => {
  const out = new Uint8Array(n);
  (globalThis as { crypto: RandomSource }).crypto.getRandomValues(out);
  return out;
};

export function bytesToHexLower(bytes: Uint8Array): string {
  let s = '';
  for (const b of bytes) s += b.toString(16).padStart(2, '0');
  return s;
}

/** Generate a fresh per-session identity: a short readable id + a 33-byte "pub" hex. */
export function generateIdentity(randomBytes: RandomBytes = defaultRandomBytes): SessionIdentity {
  const idTag = bytesToHexLower(randomBytes(2));
  // 33 bytes mirrors a compressed pubkey width; the leading byte is forced to 02/03 cosmetically.
  const pubBytes = randomBytes(33);
  pubBytes[0] = (pubBytes[0]! & 1) === 0 ? 0x02 : 0x03;
  return { id: `player-${idTag}`, pub: bytesToHexLower(pubBytes) };
}

export function validateNetworkTable(form: NetworkTableForm): NetworkTableValidation {
  const errors: string[] = [];
  if (form.name.trim().length === 0) errors.push('Table name is required.');
  if (!(form.smallBlind > 0)) errors.push('Small blind must be positive.');
  if (!(form.bigBlind > form.smallBlind)) errors.push('Big blind must exceed the small blind.');
  if (!(form.startingStack >= form.bigBlind * 2)) {
    errors.push('Starting stack must be at least two big blinds.');
  }
  if (!(Number.isInteger(form.maxSeats) && form.maxSeats >= 2 && form.maxSeats <= 9)) {
    errors.push('Seats must be a whole number between 2 and 9.');
  }
  return { ok: errors.length === 0, errors };
}

/** Assemble the relay TableMeta from a validated form (Hold'em only in this phase). */
export function metaFromNetworkForm(form: NetworkTableForm): NetworkTableMeta {
  return {
    name: form.name.trim(),
    variant: 'holdem',
    smallBlind: form.smallBlind,
    bigBlind: form.bigBlind,
    startingStack: form.startingStack,
    maxSeats: form.maxSeats,
  };
}

export interface WaitingRoomVM {
  /** Players seen in the waiting room so far. */
  readonly players: readonly { id: string; pub: string }[];
  readonly joined: number;
  readonly capacity: number;
  readonly full: boolean;
  /** "Waiting for players (n/maxSeats)…" or "Table full — seating…". */
  readonly statusText: string;
}

export function waitingRoomVM(
  players: readonly { id: string; pub: string }[],
  capacity: number,
): WaitingRoomVM {
  const joined = players.length;
  const full = joined >= capacity;
  return {
    players,
    joined,
    capacity,
    full,
    statusText: full
      ? 'Table full — agreeing seats and starting…'
      : `Waiting for players (${joined}/${capacity})…`,
  };
}

/** Label for a seat in networked play: the opponent's player id (or "(you)" for the hero). */
export function networkSeatLabel(
  players: readonly { id: string; pub: string }[],
): (seat: { seat: number; isHero: boolean }) => string {
  return (seat) => {
    if (seat.isHero) return '(you)';
    const p = players[seat.seat];
    return p ? `(${p.id})` : '(opponent)';
  };
}
