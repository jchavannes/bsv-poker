/**
 * game-manifest.ts — the SIGNED one-game key lifecycle manifest (audit #27).
 *
 * `key-lifecycle.ts` enumerates WHAT secrets exist and their scope; this module is the
 * cryptographic artifact that binds each seat's session key to EXACTLY ONE game and lets
 * any party (relay, indexer, peer) REJECT cross-game key reuse. The guarantees:
 *
 *  - **Content-addressed gameId.** `gameId = SHA-256(canonical manifest body)`, where the
 *    body fixes the ruleset, stakes, tableId, the exact seat→seatPub set, and a fresh
 *    nonce. The gameId therefore COMMITS to the precise set of seat keys: you cannot move
 *    a seat key into a different game (different seat set / rules / nonce) without changing
 *    the gameId, so a key is valid for one game only.
 *  - **Every seat consents.** Each seat signs the gameId with its own session key, so the
 *    manifest is a co-signed attestation "this is my seat key, for this one game".
 *  - **Cross-game reuse is rejected.** `verifyNoCrossGameReuse` rejects any manifest whose
 *    seat keys appeared in a prior game; `assertFreshGameId` rejects a replayed gameId.
 *  - **Envelopes bind the gameId.** `gameBoundEnvelopeMessage` folds the gameId into the
 *    per-envelope signed message, so an action/commit/reveal signed for game A cannot be
 *    replayed in game B even at the same table.
 *
 * Verification is TOTAL and fail-closed: any structural defect, gameId mismatch, missing
 * seat signature, or invalid signature returns `{ ok: false }` — it never throws.
 */
import { sha256, bytesToHex } from '@bsv-poker/protocol-types';
import { verifySig, envelopeMessage, type SessionAuth } from './session-auth.ts';

export const MANIFEST_VERSION = 'bsv-poker/game-manifest-v1';
const SIG_DOMAIN = 'bsv-poker/game-manifest-sig-v1';
const ENV_DOMAIN = 'bsv-poker/game-env-v1';
const MAX_SEATS = 22; // a generous upper bound; real tables are ≤ 10

/** A seat and the Ed25519 session public key (raw, 32-byte → 64 hex) controlling it. */
export interface ManifestSeat {
  readonly seat: number;
  readonly seatPub: string;
}

/** The signed body of the manifest (everything the gameId commits to). */
export interface GameManifestBody {
  readonly v: typeof MANIFEST_VERSION;
  readonly ruleset: string;                          // e.g. 'holdem' | 'omaha' | …
  readonly stakes: { readonly sb: number; readonly bb: number };
  readonly tableId: string;
  readonly seats: readonly ManifestSeat[];
  readonly nonce: string;                            // 32-byte hex; fresh per game
}

/** A full one-game manifest: the body + its content-addressed id + each seat's signature. */
export interface GameManifest extends GameManifestBody {
  readonly gameId: string;                           // = SHA-256(canonical body)
  readonly sigs: Readonly<Record<number, string>>;   // seat → Ed25519 sig over the sign-message
}

export interface ManifestCheck { readonly ok: boolean; readonly reason: string }

const isHex = (s: unknown, len?: number): s is string =>
  typeof s === 'string' && (len === undefined ? s.length > 0 && s.length % 2 === 0 : s.length === len) && /^[0-9a-f]*$/.test(s);

/**
 * Canonical, deterministic encoding of the manifest body (fixed field order; seats sorted
 * ascending by seat). The gameId is SHA-256 of THIS string, so two parties that agree on
 * the body agree on the gameId byte-for-byte.
 */
export function canonicalManifestBody(b: GameManifestBody): string {
  const seats = [...b.seats].sort((x, y) => x.seat - y.seat).map((s) => ({ seat: s.seat, seatPub: s.seatPub }));
  return JSON.stringify({
    v: MANIFEST_VERSION,
    ruleset: b.ruleset,
    stakes: { sb: b.stakes.sb, bb: b.stakes.bb },
    tableId: b.tableId,
    seats,
    nonce: b.nonce,
  });
}

/** The content-addressed gameId for a manifest body: SHA-256(canonical body) as hex. */
export function computeGameId(b: GameManifestBody): string {
  return bytesToHex(sha256(new TextEncoder().encode(canonicalManifestBody(b))));
}

/** The exact message a seat signs to consent to the one-game binding (binds gameId+seat+pub). */
export function manifestSignMessage(gameId: string, seat: number, seatPub: string): string {
  return JSON.stringify([SIG_DOMAIN, gameId, seat, seatPub]);
}

/**
 * Build a fully co-signed manifest from a body and the seats' session keys. Every seat in
 * the body must have a matching signer (by public key); each signs the content-addressed
 * gameId. Throws only on a programming error (a seat with no matching signer) — verification
 * is the total/fail-closed path.
 */
export async function buildManifest(body: GameManifestBody, signers: readonly SessionAuth[]): Promise<GameManifest> {
  const gameId = computeGameId(body);
  const byPub = new Map(signers.map((s) => [s.pub, s]));
  const sigs: Record<number, string> = {};
  for (const { seat, seatPub } of body.seats) {
    const signer = byPub.get(seatPub);
    if (!signer) throw new Error(`buildManifest: no signer for seat ${seat} (${seatPub.slice(0, 12)}…)`);
    sigs[seat] = await signer.sign(manifestSignMessage(gameId, seat, seatPub));
  }
  return { ...body, seats: [...body.seats].sort((x, y) => x.seat - y.seat), gameId, sigs };
}

/**
 * Verify a manifest is well-formed, content-addressed, and fully co-signed (TOTAL,
 * fail-closed). On success the gameId provably commits to exactly this seat→seatPub set,
 * so each seat key is bound to this one game.
 */
export async function verifyManifest(m: unknown): Promise<ManifestCheck> {
  try {
    if (!m || typeof m !== 'object') return { ok: false, reason: 'manifest is not an object' };
    const o = m as GameManifest;
    if (o.v !== MANIFEST_VERSION) return { ok: false, reason: `bad version: ${String(o.v)}` };
    if (typeof o.ruleset !== 'string' || o.ruleset.length === 0) return { ok: false, reason: 'ruleset must be a non-empty string' };
    if (!o.stakes || typeof o.stakes !== 'object') return { ok: false, reason: 'missing stakes' };
    const { sb, bb } = o.stakes;
    if (!Number.isInteger(sb) || !Number.isInteger(bb) || sb < 0 || bb < 0 || sb + bb <= 0) return { ok: false, reason: 'stakes must be non-negative integers, not both zero' };
    if (typeof o.tableId !== 'string' || o.tableId.length === 0) return { ok: false, reason: 'tableId must be a non-empty string' };
    if (!isHex(o.nonce, 64)) return { ok: false, reason: 'nonce must be 32-byte hex (64 chars)' };
    if (!Array.isArray(o.seats) || o.seats.length < 2 || o.seats.length > MAX_SEATS) return { ok: false, reason: `seats must be 2..${MAX_SEATS}` };

    // seats: sorted ascending, unique seat indices, unique seat keys, each pub a 32-byte Ed25519 key
    const seatIds = new Set<number>();
    const pubs = new Set<string>();
    let prev = -1;
    for (const s of o.seats) {
      if (!s || typeof s !== 'object') return { ok: false, reason: 'malformed seat entry' };
      if (!Number.isInteger(s.seat) || s.seat < 0) return { ok: false, reason: `seat index must be a non-negative integer: ${String(s.seat)}` };
      if (s.seat <= prev) return { ok: false, reason: 'seats must be sorted ascending and unique' };
      prev = s.seat;
      if (!isHex(s.seatPub, 64)) return { ok: false, reason: `seatPub must be a 32-byte Ed25519 key (64 hex): seat ${s.seat}` };
      if (seatIds.has(s.seat)) return { ok: false, reason: `duplicate seat ${s.seat}` };
      if (pubs.has(s.seatPub)) return { ok: false, reason: `reused seat key across seats: ${s.seatPub.slice(0, 12)}…` };
      seatIds.add(s.seat); pubs.add(s.seatPub);
    }

    // content-addressed: the gameId MUST equal SHA-256(canonical body) — so it commits to the seat set
    if (!isHex(o.gameId, 64)) return { ok: false, reason: 'gameId must be 32-byte hex' };
    const expect = computeGameId(o);
    if (o.gameId !== expect) return { ok: false, reason: 'gameId does not match the manifest body (not content-addressed)' };

    // every seat must have consented: a valid Ed25519 signature over the sign-message
    if (!o.sigs || typeof o.sigs !== 'object') return { ok: false, reason: 'missing sigs' };
    for (const { seat, seatPub } of o.seats) {
      const sig = (o.sigs as Record<number, string>)[seat];
      if (!isHex(sig)) return { ok: false, reason: `missing/invalid signature for seat ${seat}` };
      const okSig = await verifySig(seatPub, manifestSignMessage(o.gameId, seat, seatPub), sig);
      if (!okSig) return { ok: false, reason: `seat ${seat} signature does not verify (not consented by its key)` };
    }
    return { ok: true, reason: `verified ${o.seats.length}-seat one-game manifest (gameId ${o.gameId.slice(0, 12)}…)` };
  } catch (e) {
    return { ok: false, reason: `manifest verification threw: ${e instanceof Error ? e.message : String(e)}` };
  }
}

/** The seat public keys bound by a manifest (for cross-game reuse tracking). */
export function manifestSeatPubs(m: GameManifest): string[] {
  return m.seats.map((s) => s.seatPub);
}

/**
 * Reject CROSS-GAME key reuse (the heart of the one-game lifecycle): a seat key bound by
 * this manifest must NOT have served in any prior game. `priorSeatPubs` is the set of every
 * seat key already used (the verifier/indexer accumulates it across games).
 */
export function verifyNoCrossGameReuse(m: GameManifest, priorSeatPubs: Iterable<string>): ManifestCheck {
  const prior = priorSeatPubs instanceof Set ? priorSeatPubs : new Set(priorSeatPubs);
  for (const s of m.seats) {
    if (prior.has(s.seatPub)) return { ok: false, reason: `seat key ${s.seatPub.slice(0, 12)}… was used in a prior game (keys serve at most ONE game)` };
  }
  return { ok: true, reason: `no seat key reused from a prior game (${m.seats.length} fresh keys)` };
}

/** Reject a REPLAYED gameId (a manifest re-presented after its game). */
export function assertFreshGameId(m: GameManifest, priorGameIds: Iterable<string>): ManifestCheck {
  const prior = priorGameIds instanceof Set ? priorGameIds : new Set(priorGameIds);
  if (prior.has(m.gameId)) return { ok: false, reason: `gameId ${m.gameId.slice(0, 12)}… already seen (replayed manifest)` };
  return { ok: true, reason: 'fresh gameId' };
}

/**
 * A per-envelope signed message ADDITIONALLY bound to the manifest gameId (audit #27). The
 * base `envelopeMessage` binds tableId/hand/seat/payload; folding the gameId means a
 * signature minted for game A's action/commit/reveal cannot be replayed in game B, even at
 * the same table id. The indexer/peers verify against THIS message once a manifest is
 * registered for the table.
 */
export function gameBoundEnvelopeMessage(
  gameId: string,
  tableId: string,
  e: Parameters<typeof envelopeMessage>[1],
): string {
  return JSON.stringify([ENV_DOMAIN, gameId, envelopeMessage(tableId, e)]);
}

/** Convenience: the seatPub bound to `seat` by the manifest (or null if not seated). */
export function manifestSeatKey(m: GameManifest, seat: number): string | null {
  return m.seats.find((s) => s.seat === seat)?.seatPub ?? null;
}
