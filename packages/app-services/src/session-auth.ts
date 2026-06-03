/**
 * Session authentication for the live relay protocol (audit findings 1–3, 6). Every table envelope
 * (join / commit / reveal / action) is SIGNED by the seat's session key so a relay participant
 * cannot forge another seat's message. Browser-safe: uses Web Crypto Ed25519 (no node:crypto), so it
 * runs in the webview and in Node. The signed message binds tableId + hand + seat + the payload, and
 * receivers verify the signature against the public key REGISTERED to the acting seat.
 */

import { bytesToHex } from '@bsv-poker/protocol-types';

// Minimal structural view of Web Crypto subtle (avoids depending on the DOM lib; the runtime object
// exists in Node 24 + modern browsers). Ed25519 sign/verify only.
interface MinimalSubtle {
  generateKey(alg: unknown, extractable: boolean, usages: string[]): Promise<{ publicKey: unknown; privateKey: unknown }>;
  exportKey(format: string, key: unknown): Promise<ArrayBuffer>;
  importKey(format: string, data: Uint8Array, alg: unknown, extractable: boolean, usages: string[]): Promise<unknown>;
  sign(alg: unknown, key: unknown, data: Uint8Array): Promise<ArrayBuffer>;
  verify(alg: unknown, key: unknown, sig: Uint8Array, data: Uint8Array): Promise<boolean>;
}
const subtle = (globalThis as unknown as { crypto: { subtle: MinimalSubtle } }).crypto.subtle;
const ALG = { name: 'Ed25519' } as const;

function hexToBytes(hex: string): Uint8Array {
  const out = new Uint8Array(hex.length / 2);
  for (let i = 0; i < out.length; i++) out[i] = parseInt(hex.slice(i * 2, i * 2 + 2), 16);
  return out;
}

export interface SessionAuth {
  /** Raw Ed25519 public key, hex — the seat identity used for seating + signature checks. */
  readonly pub: string;
  sign(msg: string): Promise<string>;
}

/** Create a fresh session signing key (the player's relay identity for this session). */
export async function createSessionAuth(): Promise<SessionAuth> {
  const kp = await subtle.generateKey(ALG, true, ['sign', 'verify']);
  const pub = bytesToHex(new Uint8Array(await subtle.exportKey('raw', kp.publicKey)));
  return {
    pub,
    async sign(msg: string): Promise<string> {
      const sig = await subtle.sign(ALG, kp.privateKey, new TextEncoder().encode(msg));
      return bytesToHex(new Uint8Array(sig));
    },
  };
}

/** Verify a signature against a raw Ed25519 public key (hex). False on any malformed input. */
export async function verifySig(pubHex: string, msg: string, sigHex: string): Promise<boolean> {
  try {
    const key = await subtle.importKey('raw', hexToBytes(pubHex), ALG, false, ['verify']);
    return await subtle.verify(ALG, key, hexToBytes(sigHex), new TextEncoder().encode(msg));
  } catch {
    return false;
  }
}

/**
 * Canonical signed message for a table envelope — binds tableId, hand, seat, kind/phase, and the
 * payload fields, so a signature for one (table, hand, seat, action) cannot be replayed elsewhere.
 */
export function envelopeMessage(
  tableId: string,
  e: { t: string; seat: number; hand: number; kind?: string; amount?: number; c?: string; r?: string; discard?: readonly number[] },
): string {
  return JSON.stringify([tableId, e.t, e.seat, e.hand, e.kind ?? '', e.amount ?? 0, e.c ?? '', e.r ?? '', e.discard ?? []]);
}
