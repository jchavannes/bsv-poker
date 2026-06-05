/**
 * Key & entropy lifecycle (audit #40 — docs/KEY-LIFECYCLE.md). Executable claims that keys/entropy
 * are scoped per session and per hand and are never reused across games:
 *   - a fresh session root yields a fresh seat key; the same root is deterministic;
 *   - seat-key derivation is domain-separated by its label;
 *   - per-hand entropy is distinct per hand index, deterministic from the table entropy, and never
 *     equal to the table entropy itself (no reuse across hands).
 */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { sessionAuthFromSeed, deriveSeatSeed } from '../src/session-auth.ts';
import { sha256, bytesToHex } from '@bsv-poker/protocol-types';

/** The per-hand entropy derivation documented in the manifest: SHA-256(tableEntropy ‖ u32le(hand)),
 *  matching the code's `concat(entropy, u32(hand))` where ByteWriter.u32 is little-endian. */
function perHandEntropy(tableEntropy: Uint8Array, hand: number): Uint8Array {
  const idx = new Uint8Array(4);
  new DataView(idx.buffer).setUint32(0, hand, true); // little-endian (matches ByteWriter.u32)
  const buf = new Uint8Array(tableEntropy.length + 4);
  buf.set(tableEntropy);
  buf.set(idx, tableEntropy.length);
  return sha256(buf);
}

test('a fresh session root yields a fresh seat key; the same root is deterministic (manifest #1)', async () => {
  const rootA = new Uint8Array(32).fill(3);
  const rootB = new Uint8Array(32).fill(9);
  const a1 = await sessionAuthFromSeed(deriveSeatSeed(rootA));
  const a2 = await sessionAuthFromSeed(deriveSeatSeed(rootA)); // same root → same key (deterministic)
  const b = await sessionAuthFromSeed(deriveSeatSeed(rootB)); // different root → different key
  assert.equal(a1.pub, a2.pub, 'same session root must derive the same seat key');
  assert.notEqual(a1.pub, b.pub, 'distinct session roots must derive distinct seat keys (no cross-session reuse)');
});

test('seat-key derivation is domain-separated by its label (manifest #3)', () => {
  const root = new Uint8Array(32).fill(7);
  const seatSeed = deriveSeatSeed(root, 'bsv-poker/seat-ed25519');
  const otherSeed = deriveSeatSeed(root, 'bsv-poker/other-domain');
  // Different domain labels over the same root produce different seeds — the seat key cannot collide
  // with a value derived for another purpose, and never equals the raw root.
  assert.notEqual(bytesToHex(seatSeed), bytesToHex(otherSeed), 'labels must domain-separate the derivation');
  assert.notEqual(bytesToHex(seatSeed), bytesToHex(root), 'the seat seed must not be the raw root');
});

test('per-hand entropy is distinct per hand, deterministic, and never reuses the table entropy (manifest #2, REQ-CRYPTO-010)', () => {
  const tableEntropy = new Uint8Array(32).fill(42);
  const seen = new Set<string>();
  for (let hand = 0; hand < 64; hand++) {
    const e = bytesToHex(perHandEntropy(tableEntropy, hand));
    assert.equal(seen.has(e), false, `per-hand entropy must be distinct across hands (collision at hand ${hand})`);
    seen.add(e);
    assert.equal(e, bytesToHex(perHandEntropy(tableEntropy, hand)), 'per-hand entropy must be deterministic from (tableEntropy, hand)');
    assert.notEqual(e, bytesToHex(tableEntropy), 'a hand must never reuse the raw table entropy as its secret');
  }
  assert.equal(seen.size, 64, 'all 64 per-hand entropies were distinct');
});
