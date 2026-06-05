/**
 * Key & entropy lifecycle — the ONE-GAME key manifest, as code (audit #33). This module is the
 * single, discoverable source for "what secrets exist, how they are derived, and how long they live"
 * so the lifecycle is not only documented (docs/KEY-LIFECYCLE.md) but enforced by a named, tested,
 * exported API that the live client actually uses.
 *
 * The guarantees (no reuse across games): a fresh CSPRNG session root → the seat key (per session); a
 * fresh CSPRNG table entropy per join; a fresh PER-HAND entropy `H(table_entropy ‖ u32le(hand))` so
 * every hand has a distinct, unpredictable secret permutation input (REQ-CRYPTO-010); domain-separated
 * key derivation (labelled). Each value is bound into signatures by the table/hand/seat (envelopeMessage).
 */
import { sha256, ByteWriter } from '@bsv-poker/protocol-types';
import { deriveSeatSeed } from './session-auth.ts';

export { deriveSeatSeed };

/** Concatenate two byte arrays (a ‖ b). */
function concat(a: Uint8Array, b: Uint8Array): Uint8Array {
  const out = new Uint8Array(a.length + b.length);
  out.set(a, 0);
  out.set(b, a.length);
  return out;
}

/** Little-endian uint32 (the canonical 4-byte hand index, matching ByteWriter.u32). */
function u32le(n: number): Uint8Array {
  const w = new ByteWriter();
  w.u32(n);
  return w.toBytes();
}

/**
 * The per-hand secret entropy: `SHA-256(tableEntropy ‖ u32le(handIndex))` (REQ-CRYPTO-010). This is
 * THE derivation the interactive client uses each hand — distinct per hand, deterministic from the
 * table entropy + hand index, and never the raw table entropy itself, so no entropy is reused across
 * hands. `handIndex` must be a 0..2^32-1 integer.
 */
export function perHandEntropy(tableEntropy: Uint8Array, handIndex: number): Uint8Array {
  if (!Number.isInteger(handIndex) || handIndex < 0 || handIndex > 0xffffffff) {
    throw new RangeError(`handIndex out of range: ${handIndex}`);
  }
  return sha256(concat(tableEntropy, u32le(handIndex)));
}

/** A single entry in the key-lifecycle manifest. */
export interface KeyLifecycleEntry {
  readonly name: string;
  readonly origin: 'csprng' | 'derived';
  readonly derivation: string;
  readonly scope: 'session' | 'table' | 'hand';
  readonly reuse: 'never-across-games' | 'never-across-hands';
}

/**
 * The machine-readable one-game key manifest (audit #33). Mirrors docs/KEY-LIFECYCLE.md; kept here so
 * the lifecycle is a discoverable, type-checked artifact a tool/auditor can enumerate, not only prose.
 */
export const KEY_LIFECYCLE: readonly KeyLifecycleEntry[] = [
  { name: 'session-root', origin: 'csprng', derivation: 'crypto.getRandomValues(32)', scope: 'session', reuse: 'never-across-games' },
  { name: 'seat-session-key', origin: 'derived', derivation: 'sessionAuthFromSeed(deriveSeatSeed(root, "bsv-poker/seat-ed25519"))', scope: 'session', reuse: 'never-across-games' },
  { name: 'table-entropy', origin: 'csprng', derivation: 'crypto.getRandomValues(32)', scope: 'table', reuse: 'never-across-games' },
  { name: 'per-hand-entropy', origin: 'derived', derivation: 'SHA-256(table_entropy ‖ u32le(handIndex))', scope: 'hand', reuse: 'never-across-hands' },
] as const;
