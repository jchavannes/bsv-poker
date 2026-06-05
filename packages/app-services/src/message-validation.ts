/**
 * Trust-boundary input validation (REQ-APP-103). Every message crossing a trust boundary — relay /
 * peer envelopes (and, on desktop, IPC) — is validated before use; anything unrecognized or
 * malformed is REJECTED (returns null), never partially trusted. This is the structural guard the
 * networked client applies to inbound envelopes from the (untrusted) relay channel.
 */

export type EnvelopeKind = 'commit' | 'reveal' | 'action' | 'timeout-claim';

export interface WireEnvelope {
  readonly t: EnvelopeKind;
  readonly seat: number;
  readonly hand: number;
  readonly c?: string; // commit: H(entropy) hex
  readonly r?: string; // reveal: entropy hex
  readonly kind?: string; // action: ActionKind
  readonly amount?: number; // action: optional wager
  readonly discard?: readonly number[]; // action: draw discard slot set
  readonly prev?: string; // action: prior state hash bound into the signature (audit 8)
  readonly d?: number; // timeout-claim: anchored deadline block height (audit 3)
  readonly h?: number; // action/reveal: anchored block height it was emitted at — the timeout floor (audit 3)
  readonly subject?: number; // timeout-claim: the seat being dropped (signed BY `seat`, ABOUT `subject`)
}

const isHeight = (v: unknown): v is number => typeof v === 'number' && Number.isInteger(v) && v >= 0;

const isHex = (v: unknown): v is string => typeof v === 'string' && /^[0-9a-f]+$/i.test(v) && v.length > 0;
const isSeatOrHand = (v: unknown): v is number => typeof v === 'number' && Number.isInteger(v) && v >= 0;

/** Validate an inbound envelope; return the typed envelope or null if it must be rejected. */
export function validateEnvelope(raw: unknown): WireEnvelope | null {
  if (!raw || typeof raw !== 'object') return null;
  const o = raw as Record<string, unknown>;
  if (o.t !== 'commit' && o.t !== 'reveal' && o.t !== 'action' && o.t !== 'timeout-claim') return null; // unrecognized → reject
  if (!isSeatOrHand(o.seat) || !isSeatOrHand(o.hand)) return null;

  if (o.t === 'timeout-claim') {
    // A claim, SIGNED BY claimant seat `seat`, that seat `subject` failed to act by anchored block
    // height `d` (audit 3). subject must be a distinct, valid seat from the signer.
    if (!isHeight(o.d)) return null;
    if (!isSeatOrHand(o.subject) || o.subject === o.seat) return null;
    return { t: 'timeout-claim', seat: o.seat, hand: o.hand, d: o.d, subject: o.subject };
  }
  if (o.t === 'commit') {
    if (!isHex(o.c)) return null;
    return { t: 'commit', seat: o.seat, hand: o.hand, c: o.c };
  }
  if (o.t === 'reveal') {
    if (!isHex(o.r)) return null;
    if (o.h !== undefined && !isHeight(o.h)) return null;
    const rev: WireEnvelope = { t: 'reveal', seat: o.seat, hand: o.hand, r: o.r };
    return o.h !== undefined ? { ...rev, h: o.h } : rev;
  }
  // action
  if (typeof o.kind !== 'string' || o.kind.length === 0) return null;
  if (o.amount !== undefined && (typeof o.amount !== 'number' || !Number.isFinite(o.amount))) return null;
  if (o.discard !== undefined && !(Array.isArray(o.discard) && o.discard.every((d) => typeof d === 'number' && Number.isInteger(d) && d >= 0))) return null;
  if (o.prev !== undefined && typeof o.prev !== 'string') return null;
  if (o.h !== undefined && !isHeight(o.h)) return null;
  let env: WireEnvelope = { t: 'action', seat: o.seat, hand: o.hand, kind: o.kind };
  if (o.amount !== undefined) env = { ...env, amount: o.amount };
  if (o.discard !== undefined) env = { ...env, discard: o.discard as number[] };
  if (o.prev !== undefined) env = { ...env, prev: o.prev };
  if (o.h !== undefined) env = { ...env, h: o.h };
  return env;
}

/** Parse a JSON wire frame and validate it; null on bad JSON or a rejected envelope. */
export function parseAndValidate(frame: string): WireEnvelope | null {
  try {
    return validateEnvelope(JSON.parse(frame));
  } catch {
    return null;
  }
}
