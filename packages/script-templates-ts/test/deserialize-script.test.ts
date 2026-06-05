/**
 * deserializeScript — executable claims (INV-DSC-*). The inverse of serializeScript, used by the
 * in-tree regtest node to run the interpreter on parsed transactions. A hostile-input grammar held to
 * the same bar as the tx parser: bounds-checked, never-throwing, round-trip-faithful.
 */

import { test } from 'node:test';
import assert from 'node:assert/strict';
import { OP } from '../src/opcodes.ts';
import { serializeScript, deserializeScript, tryDeserializeScript, type Script } from '../src/script.ts';

// INV-DSC-1 (positive): round-trip — deserialize(serialize(s)) deep-equals s for a representative script.
test('INV-DSC-1 positive: round-trips opcodes + pushes of every push form', () => {
  const small = new Uint8Array([1, 2, 3]); // direct push (len < 0x4c)
  const med = new Uint8Array(200).fill(7); // OP_PUSHDATA1
  const big = new Uint8Array(1000).fill(9); // OP_PUSHDATA2
  const script: Script = [OP.OP_DUP, OP.OP_HASH160, small, OP.OP_EQUALVERIFY, OP.OP_CHECKSIG, OP.OP_0, med, big, OP.OP_1];
  const bytes = serializeScript(script);
  const r = deserializeScript(bytes);
  assert.ok(r.ok, r.ok ? '' : r.reason);
  if (!r.ok) return;
  // re-serialize must reproduce the SAME bytes (canonical round-trip)
  assert.deepEqual(serializeScript(r.script), bytes);
  assert.equal(r.script.length, script.length);
});

// INV-DSC-2 (positive): an empty script deserializes to an empty Script.
test('INV-DSC-2 positive: empty script', () => {
  const r = deserializeScript(new Uint8Array(0));
  assert.ok(r.ok && r.script.length === 0);
});

// INV-DSC-3 (negative): a truncated pushdata (claims more bytes than remain) is rejected, not OOB-read.
test('INV-DSC-3 negative: truncated direct push rejected', () => {
  // 0x05 says "push 5 bytes" but only 2 follow.
  const r = deserializeScript(new Uint8Array([0x05, 0xaa, 0xbb]));
  assert.equal(r.ok, false);
  if (!r.ok) assert.match(r.reason, /exceeds remaining|truncated/);
});

test('INV-DSC-4 negative: truncated PUSHDATA1/2 length or data rejected', () => {
  assert.equal(deserializeScript(new Uint8Array([OP.OP_PUSHDATA1])).ok, false); // missing length byte
  assert.equal(deserializeScript(new Uint8Array([OP.OP_PUSHDATA1, 0x10, 0x00])).ok, false); // says 16, has 1
  assert.equal(deserializeScript(new Uint8Array([OP.OP_PUSHDATA2, 0x10])).ok, false); // missing 2nd len byte
  assert.equal(deserializeScript(new Uint8Array([OP.OP_PUSHDATA4, 0xff, 0xff, 0xff, 0xff])).ok, false); // 4GB push, no data
});

test('INV-DSC-5 negative: oversize / non-Uint8Array rejected', () => {
  assert.equal(deserializeScript(new Uint8Array(100_001)).ok, false);
  assert.equal(deserializeScript('nope' as unknown as Uint8Array).ok, false);
  assert.equal(tryDeserializeScript(new Uint8Array([0x05, 0x01])), null);
});

// INV-DSC-F1 (fuzz): no random byte string throws or OOB-reads; any accepted parse re-serializes
// to the SAME bytes (the parse is unambiguous).
test('INV-DSC-F1 fuzz: 200k random scripts never throw; accepted parses round-trip', () => {
  let rng = 0x5c21bee5;
  const next = (): number => (rng = (rng * 1103515245 + 12345) & 0x7fffffff);
  for (let iter = 0; iter < 200_000; iter++) {
    const len = next() % 40;
    const buf = new Uint8Array(len);
    for (let i = 0; i < len; i++) buf[i] = next() & 0xff;
    assert.doesNotThrow(() => {
      const r = deserializeScript(buf);
      assert.equal(typeof r.ok, 'boolean');
      if (r.ok) assert.deepEqual(serializeScript(r.script), buf);
    });
  }
});
