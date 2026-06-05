/**
 * On-chain bond FORFEITURE E2E (audit finding 3, on-chain half — the accountability penalty).
 *
 * STANDALONE: runs entirely against the project's OWN in-tree node (`@bsv-poker/adapters/regtest-node`)
 * — no external process, no separate project. The node runs the real Script interpreter and ENFORCES
 * nLockTime finality, so the maturity gate below is proven for real (no disclaimers).
 *
 * A per-seat bond is locked with `bondRevealOrForfeitLocking`: two mutually-exclusive branches —
 *   REVEAL  → the owner reclaims their bond by revealing the committed preimage + signing;
 *   FORFEIT → after maturity (the spending tx's nLockTime, enforced by the node), the POT BENEFICIARY
 *             claims the bond, so an absent player is penalised.
 *
 * Proven against the in-tree node:
 *   1. a responsive owner reclaims their own bond via REVEAL (positive);
 *   2. a wrong preimage on REVEAL fails IN-SCRIPT (negative — cannot steal by guessing);
 *   3. the beneficiary's FORFEIT claim is REJECTED before maturity by the nLockTime finality gate
 *      (negative — the node enforces it);
 *   4. after maturity the FORFEIT claim confirms; the absent owner's bond is gone, conserving exactly
 *      the bond value; the owner can no longer reclaim it (double-spend rejected).
 *
 * Signatures are bare DER over the BIP-143 sighash — the convention the in-tree interpreter verifies.
 */

import { randomBytes } from 'node:crypto';
import assert from 'node:assert/strict';
import { RegtestNode, p2pkhScript } from '@bsv-poker/adapters/regtest-node';
import {
  genKeyPair,
  signPreimage,
  bondRevealOrForfeitLocking,
  bondReclaimByRevealUnlocking,
  bondForfeitClaimUnlocking,
  type Script,
  type KeyPair,
} from '@bsv-poker/script-templates-ts';
import { bytesToHex, sha256, type BranchBinding } from '@bsv-poker/protocol-types';
import { type Tx, serializeTxWire, txidWire, sighashMessage } from '@bsv-poker/tx-builder';

const SUBSIDY = 5_000_000_000;
const BIND: BranchBinding = { gid: 'a1'.repeat(8), rulesetHash: 'b2'.repeat(32), round: 0, stateHash: 'c3'.repeat(32), actingSeat: -1, successorCommitment: '00'.repeat(32) };

/** Bare-DER signature over the sighash (the in-tree interpreter verifies ECDSA-over-SHA256 of it). */
const sigT = (msg: Uint8Array, k: KeyPair): Uint8Array => signPreimage(msg, k.priv);
const hex = (b: Uint8Array): string => bytesToHex(b);

async function main(): Promise<void> {
  const node = new RegtestNode();
  const k0 = genKeyPair(); // miner / funder
  const owner = genKeyPair(); // bond owner's reveal key
  const bene = genKeyPair(); // pot beneficiary (claims forfeited bonds)
  const BOND = SUBSIDY - 1000;

  // Fund a bond output from a coinbase: owner-reclaimable by revealing `preimage`, else forfeitable
  // to `bene` after maturity.
  const fundBond = async (preimage: Uint8Array): Promise<{ txid: string; script: Script }> => {
    const cb = await node.generateBlock(hex(k0.pubCompressed));
    await node.generateBlock(hex(k0.pubCompressed)); // a second block (regtest convenience)
    const commitment = sha256(preimage);
    const script = bondRevealOrForfeitLocking(BIND, commitment, owner.pubCompressed, bene.pubCompressed);
    const tx: Tx = { version: 1, inputs: [{ prevTxid: cb.coinbaseTxid, vout: 0, sequence: 0xffffffff }], outputs: [{ satoshis: BOND, locking: script }], nLockTime: 0 };
    const ss: Script = [sigT(sighashMessage(tx, 0, p2pkhScript(k0.pubCompressed), SUBSIDY), k0), k0.pubCompressed];
    const r = await node.submitTx(bytesToHex(serializeTxWire(tx, [ss])));
    assert.equal(r.ok, true, `bond funding rejected: ${r.reason}`);
    await node.generateBlock(hex(k0.pubCompressed));
    return { txid: txidWire(tx, [ss]), script };
  };

  // ---- Bond #1: responsive owner RECLAIMS via REVEAL ------------------------------------------
  const preimage1 = new Uint8Array(randomBytes(32));
  const bond1 = await fundBond(preimage1);

  // NEGATIVE 2: a WRONG preimage fails OP_SHA256/OP_EQUALVERIFY in-script.
  {
    const tx: Tx = { version: 1, inputs: [{ prevTxid: bond1.txid, vout: 0, sequence: 0xffffffff }], outputs: [{ satoshis: BOND - 1000, locking: p2pkhScript(owner.pubCompressed) }], nLockTime: 0 };
    const msg = sighashMessage(tx, 0, bond1.script, BOND);
    const ss = bondReclaimByRevealUnlocking(sigT(msg, owner), new Uint8Array(randomBytes(32)));
    const r = await node.submitTx(bytesToHex(serializeTxWire(tx, [ss])));
    assert.equal(r.ok, false, 'a WRONG preimage must fail the REVEAL branch in-script');
    console.log(`[onchain-forfeit] wrong-preimage REVEAL rejected in-script: "${r.reason}"`);
  }

  // POSITIVE 1: the correct preimage + owner signature reclaims the bond.
  {
    const tx: Tx = { version: 1, inputs: [{ prevTxid: bond1.txid, vout: 0, sequence: 0xffffffff }], outputs: [{ satoshis: BOND - 1000, locking: p2pkhScript(owner.pubCompressed) }], nLockTime: 0 };
    const msg = sighashMessage(tx, 0, bond1.script, BOND);
    const ss = bondReclaimByRevealUnlocking(sigT(msg, owner), preimage1);
    const r = await node.submitTx(bytesToHex(serializeTxWire(tx, [ss])));
    assert.equal(r.ok, true, `owner REVEAL reclaim rejected: ${r.reason}`);
    await node.generateBlock(hex(k0.pubCompressed));
    const reclaimed = await node.outpointStatus(txidWire(tx, [ss]), 0);
    assert.equal((await node.outpointStatus(bond1.txid, 0)).unspent, false, 'bond #1 consumed by REVEAL');
    assert.equal(reclaimed.unspent, true, 'owner received the reclaimed bond');
    assert.equal(reclaimed.value, BOND - 1000, 'reclaim conserves the bond value (minus fee)');
    console.log(`[onchain-forfeit] owner RECLAIMED bond #1 via REVEAL → ${reclaimed.value} sats`);
  }

  // ---- Bond #2: ABSENT owner (never reveals) → beneficiary FORFEITS after maturity ------------
  const preimage2 = new Uint8Array(randomBytes(32));
  const bond2 = await fundBond(preimage2);
  const maturity = (await node.height()) + 3; // agreed maturity height (the anchored deadline)

  const buildForfeit = (nLockTime: number): { raw: string; txid: string } => {
    // nLockTime is enforced because the spending input is non-final (sequence < 0xffffffff).
    const tx: Tx = { version: 1, inputs: [{ prevTxid: bond2.txid, vout: 0, sequence: 0xfffffffe }], outputs: [{ satoshis: BOND - 1000, locking: p2pkhScript(bene.pubCompressed) }], nLockTime };
    const msg = sighashMessage(tx, 0, bond2.script, BOND);
    const ss = bondForfeitClaimUnlocking(sigT(msg, bene));
    return { raw: bytesToHex(serializeTxWire(tx, [ss])), txid: txidWire(tx, [ss]) };
  };

  // NEGATIVE 3: the FORFEIT claim before maturity is REJECTED by the node's nLockTime finality gate.
  const claim = buildForfeit(maturity);
  const premature = await node.submitTx(claim.raw);
  assert.equal(premature.ok, false, 'premature FORFEIT must be rejected before maturity (nLockTime finality)');
  console.log(`[onchain-forfeit] premature FORFEIT (height ${await node.height()} < maturity ${maturity}) REJECTED by the node: "${premature.reason}"`);

  // Advance the chain to maturity, then the claim is admissible.
  while ((await node.height()) < maturity) await node.generateBlock(hex(k0.pubCompressed));
  const matured = await node.submitTx(claim.raw);
  assert.equal(matured.ok, true, `FORFEIT at maturity rejected: ${matured.reason}`);

  // POSITIVE 4: the FORFEIT confirms; the absent owner's bond is forfeited, conserving exactly the
  // bond value, and the owner can NEVER reclaim it (the outpoint is already spent).
  {
    await node.generateBlock(hex(k0.pubCompressed));
    assert.equal((await node.outpointStatus(bond2.txid, 0)).unspent, false, 'bond #2 consumed by FORFEIT');
    const forfeited = await node.outpointStatus(claim.txid, 0);
    assert.equal(forfeited.unspent, true, 'beneficiary received the forfeited bond');
    assert.equal(forfeited.value, BOND - 1000, 'forfeiture conserves exactly the bond value (minus fee)');
    console.log(`[onchain-forfeit] beneficiary FORFEITED absent owner's bond #2 → ${forfeited.value} sats`);

    // The absent owner can NO LONGER reclaim it: the outpoint is already spent (double-spend rejected).
    const tx: Tx = { version: 1, inputs: [{ prevTxid: bond2.txid, vout: 0, sequence: 0xffffffff }], outputs: [{ satoshis: BOND - 1000, locking: p2pkhScript(owner.pubCompressed) }], nLockTime: 0 };
    const msg = sighashMessage(tx, 0, bond2.script, BOND);
    const ss = bondReclaimByRevealUnlocking(sigT(msg, owner), preimage2);
    const late = await node.submitTx(bytesToHex(serializeTxWire(tx, [ss])));
    assert.equal(late.ok, false, 'owner cannot reclaim a bond already forfeited (double-spend)');
    console.log(`[onchain-forfeit] post-forfeit owner reclaim correctly rejected: "${late.reason}"`);
  }

  console.log('\n[onchain-forfeit] PASS — bond reveal-or-FORFEIT proven on the in-tree node: owner reclaims via REVEAL, a wrong preimage fails IN-SCRIPT, a premature FORFEIT is REJECTED by the nLockTime finality gate, the beneficiary settles the FORFEIT at maturity conserving exactly the bond, and the forfeited owner can never reclaim it (double-spend rejected). Standalone — no external node.');
}

main().then(
  () => process.exit(0),
  (e) => {
    console.error('[onchain-forfeit] FAIL:', (e as Error).message);
    process.exit(1);
  },
);
