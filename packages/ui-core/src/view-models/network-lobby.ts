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

/** Variant ids (structural mirror of protocol-types' Variant — ui-core stays import-light). */
export type VariantId = 'holdem' | 'omaha' | 'stud' | 'draw' | 'razz';

/** Seat-range metadata per variant (structural mirror of app-services VARIANT_INFO so the
 * view-model can validate seat counts without importing app-services). The app passes the real
 * VARIANT_INFO labels into the UI; these bounds keep the pure validation self-contained. */
export const VARIANT_SEAT_RANGE: Record<VariantId, { readonly minSeats: number; readonly maxSeats: number }> = {
  holdem: { minSeats: 2, maxSeats: 9 },
  omaha: { minSeats: 2, maxSeats: 9 },
  stud: { minSeats: 2, maxSeats: 8 },
  draw: { minSeats: 2, maxSeats: 6 },
  razz: { minSeats: 2, maxSeats: 8 },
};

/** The TableMeta shape consumed by app-services LobbyClient (kept structural to avoid importing
 * app-services into ui-core — ui-core must stay free of app-services). */
export interface NetworkTableMeta {
  readonly name: string;
  readonly variant: VariantId;
  readonly smallBlind: number;
  readonly bigBlind: number;
  readonly startingStack: number;
  readonly maxSeats: number;
  /** Omaha hi-lo split toggle (only meaningful for omaha). Carried in the meta for display. */
  readonly hiLo?: boolean;
}

export interface SessionIdentity {
  /** Human-facing player id (e.g. "player-3f9a"). */
  readonly id: string;
  /** Random hex used for deterministic seat ordering (NOT a real key — see file header). */
  readonly pub: string;
}

export interface NetworkTableForm {
  readonly name: string;
  readonly variant: VariantId;
  readonly smallBlind: number;
  readonly bigBlind: number;
  readonly startingStack: number;
  readonly maxSeats: number;
  /** Omaha hi-lo split (ignored for other variants). */
  readonly hiLo?: boolean;
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
  const range = VARIANT_SEAT_RANGE[form.variant];
  if (!range) {
    errors.push('Unknown variant.');
  } else if (
    !(Number.isInteger(form.maxSeats) && form.maxSeats >= range.minSeats && form.maxSeats <= range.maxSeats)
  ) {
    errors.push(`Seats must be a whole number between ${range.minSeats} and ${range.maxSeats} for this variant.`);
  }
  return { ok: errors.length === 0, errors };
}

/** Assemble the relay TableMeta from a validated form (any of the five variants). */
export function metaFromNetworkForm(form: NetworkTableForm): NetworkTableMeta {
  const hiLo = form.variant === 'omaha' ? Boolean(form.hiLo) : false;
  return {
    name: form.name.trim(),
    variant: form.variant,
    smallBlind: form.smallBlind,
    bigBlind: form.bigBlind,
    startingStack: form.startingStack,
    maxSeats: form.maxSeats,
    hiLo,
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
