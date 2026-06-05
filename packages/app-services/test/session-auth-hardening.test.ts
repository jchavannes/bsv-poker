/**
 * Session-key memory-exposure hardening (audit #17). The long-lived Ed25519 signing key — which
 * authorizes every envelope and the on-chain settlement — is NON-EXTRACTABLE: the crypto subsystem
 * holds the raw key, not JS memory, so a process memory dump cannot export it or forge signatures.
 * This proves: (1) signing still works (the non-extractable key signs + verifies); (2) the SessionAuth
 * surface exposes no raw key material; (3) the non-extractable import mechanism actually rejects export.
 */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { webcrypto } from 'node:crypto';
import { sessionAuthFromSeed, verifySig } from '../src/session-auth.ts';

const subtle = webcrypto.subtle;
// PKCS8 DER prefix for a raw Ed25519 seed (same construction session-auth uses).
const ED25519_PKCS8_PREFIX = Uint8Array.from([0x30, 0x2e, 0x02, 0x01, 0x00, 0x30, 0x05, 0x06, 0x03, 0x2b, 0x65, 0x70, 0x04, 0x22, 0x04, 0x20]);

test('the non-extractable signing key still signs + verifies (audit #17)', async () => {
  const auth = await sessionAuthFromSeed(new Uint8Array(32).fill(5));
  const sig = await auth.sign('hello-table');
  assert.equal(await verifySig(auth.pub, 'hello-table', sig), true, 'a valid signature must verify');
  assert.equal(await verifySig(auth.pub, 'tampered', sig), false, 'a different message must NOT verify');
  // Deterministic from the seed: same seed → same pub (key identity is stable).
  const auth2 = await sessionAuthFromSeed(new Uint8Array(32).fill(5));
  assert.equal(auth2.pub, auth.pub, 'same seed must derive the same public key');
});

test('the SessionAuth surface exposes no raw private-key material (audit #17)', async () => {
  const auth = await sessionAuthFromSeed(new Uint8Array(32).fill(7));
  // Only the public key (hex) and the sign function are reachable — no seed/priv/CryptoKey field.
  assert.deepEqual(Object.keys(auth).sort(), ['pub', 'sign']);
  assert.equal(typeof auth.pub, 'string');
  assert.equal(typeof auth.sign, 'function');
});

test('a non-extractable Ed25519 key cannot be exported (the mechanism session-auth relies on)', async () => {
  const der = new Uint8Array(ED25519_PKCS8_PREFIX.length + 32);
  der.set(ED25519_PKCS8_PREFIX);
  der.set(new Uint8Array(32).fill(9), ED25519_PKCS8_PREFIX.length);
  const key = await subtle.importKey('pkcs8', der, 'Ed25519', false, ['sign']); // extractable: false
  await assert.rejects(subtle.exportKey('jwk', key), 'a non-extractable key MUST refuse export');
  await assert.rejects(subtle.exportKey('pkcs8', key), 'a non-extractable key MUST refuse raw export');
  // Sanity: an extractable key DOES export — so the rejection above is the flag, not a broken call.
  const ext = await subtle.importKey('pkcs8', der, 'Ed25519', true, ['sign']);
  assert.ok(await subtle.exportKey('jwk', ext), 'an extractable key exports (control)');
});
