/**
 * Minimal BSV Script model + wire serialization (core §6.6). A Script is a sequence of items:
 * either an opcode (number) or a data push (Uint8Array). Serialization yields the exact wire
 * bytes so the build can MEASURE template sizes as reproducible vectors (REQ-TX-011, §19.C).
 *
 * Commitments are carried as pushdata in a live script (`<data> OP_DROP`), NEVER OP_RETURN
 * (core P11/§6.5, REQ-TX-010).
 */

import { ByteWriter, ByteReader, bytesToHex } from '@bsv-poker/protocol-types';
import { OP, BANNED_OPCODES } from './opcodes.ts';

export type ScriptItem = number | Uint8Array;
export type Script = ScriptItem[];

/** Encode a single data push with minimal pushdata opcodes. */
export function pushData(w: ByteWriter, data: Uint8Array): void {
  const n = data.length;
  if (n < OP.OP_PUSHDATA1) {
    w.u8(n);
  } else if (n <= 0xff) {
    w.u8(OP.OP_PUSHDATA1).u8(n);
  } else if (n <= 0xffff) {
    w.u8(OP.OP_PUSHDATA2).u16(n);
  } else {
    throw new RangeError('push too large for this builder');
  }
  for (const b of data) w.u8(b);
}

/** Serialize a Script to wire bytes. Throws if a banned opcode (OP_RETURN) appears. */
export function serializeScript(script: Script): Uint8Array {
  const w = new ByteWriter();
  for (const item of script) {
    if (typeof item === 'number') {
      if (BANNED_OPCODES.includes(item)) {
        throw new Error(`banned opcode in script: 0x${item.toString(16)} (OP_RETURN, core P11)`);
      }
      w.u8(item);
    } else {
      pushData(w, item);
    }
  }
  return w.toBytes();
}

export function scriptSizeBytes(script: Script): number {
  return serializeScript(script).length;
}

export function scriptHex(script: Script): string {
  return bytesToHex(serializeScript(script));
}

/** Does the serialized script contain the OP_RETURN byte (0x6a)? Used by the lint (rule 2). */
export function containsOpReturn(script: Script): boolean {
  // Note: a 0x6a byte INSIDE a data push is data, not an opcode — but the ban is absolute and
  // our builders never push 0x6a as an opcode; we check the opcode stream only.
  return script.some((item) => typeof item === 'number' && item === OP.OP_RETURN);
}

/**
 * deserializeScript — bytes → Script (ScriptItem[]), the inverse of {@link serializeScript}.
 *
 * WHY THIS EXISTS: the in-tree regtest node ({@link import('@bsv-poker/adapters').RegtestNode})
 * receives raw transaction bytes, parses them with the hardened `parseTxWire`, and must then run each
 * input's unlocking script (and the prevout's locking script) through the real interpreter. The
 * interpreter consumes the structured `Script`, so the raw script BYTES must be parsed back into
 * opcodes + pushes — a hostile-input grammar that gets the same bar as the tx parser: bounds-checked,
 * never-throwing, and built on `ByteReader`.
 *
 * WIRE GRAMMAR (Bitcoin Script): each item is either
 *   - 0x00            → OP_0 (an opcode that pushes an empty value);
 *   - 0x01..0x4b      → a direct push of exactly that many data bytes;
 *   - 0x4c PUSHDATA1  → next 1 byte is the length, then that many data bytes;
 *   - 0x4d PUSHDATA2  → next 2 bytes (LE) are the length, then the data;
 *   - 0x4e PUSHDATA4  → next 4 bytes (LE) are the length, then the data;
 *   - >= 0x4f         → an opcode (a single byte).
 *
 * SECURITY BOUNDARY: every read is bounds-checked against the buffer (a push length that exceeds the
 * remaining bytes is REJECTED, never read past — CWE-125/129/400). The total size is bounded
 * (`MAX_SCRIPT_BYTES`). It returns a discriminated result and NEVER throws on hostile input.
 */
export type ScriptParseResult =
  | { readonly ok: true; readonly script: Script }
  | { readonly ok: false; readonly reason: string; readonly offset: number };

/** Hard cap on a single script's wire length (DoS bound; templates are a few dozen bytes). */
export const MAX_SCRIPT_BYTES = 100_000;

export function deserializeScript(bytes: Uint8Array): ScriptParseResult {
  if (!(bytes instanceof Uint8Array)) return { ok: false, reason: 'not a Uint8Array', offset: 0 };
  if (bytes.length > MAX_SCRIPT_BYTES) return { ok: false, reason: `oversize script: ${bytes.length}`, offset: 0 };
  const r = new ByteReader(bytes);
  const out: Script = [];
  // Bounded by the buffer length: each iteration consumes at least one byte (NASA P10).
  while (!r.atEnd) {
    const op = r.tryReadU8();
    if (op === null) return { ok: false, reason: 'truncated opcode', offset: r.offset };
    if (op === 0x00) {
      out.push(OP.OP_0); // 0x00 is the OP_0 opcode (pushes empty)
      continue;
    }
    let len: number | null = null;
    if (op <= 0x4b) {
      len = op; // direct push of `op` bytes
    } else if (op === OP.OP_PUSHDATA1) {
      len = r.tryReadU8();
    } else if (op === OP.OP_PUSHDATA2) {
      len = r.tryReadU16LE();
    } else if (op === OP.OP_PUSHDATA4) {
      len = r.tryReadU32LE();
    } else {
      out.push(op); // a plain opcode
      continue;
    }
    if (len === null) return { ok: false, reason: 'truncated pushdata length', offset: r.offset };
    if (len > r.remaining) return { ok: false, reason: `pushdata length ${len} exceeds remaining bytes`, offset: r.offset };
    const data = r.tryReadBytes(len);
    if (data === null) return { ok: false, reason: 'truncated pushdata', offset: r.offset };
    out.push(data);
  }
  return { ok: true, script: out };
}

/** Non-throwing convenience: returns the Script or null (hostile boundaries that want a value). */
export function tryDeserializeScript(bytes: Uint8Array): Script | null {
  const r = deserializeScript(bytes);
  return r.ok ? r.script : null;
}
