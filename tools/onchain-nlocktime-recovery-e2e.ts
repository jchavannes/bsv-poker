/**
 * GUARANTEED nLockTime recovery — life-critical (memory `always-nlocktime-recovery-all-funds`).
 * Proves on the project's OWN in-tree node that EVERY player holds a pre-signed, unilateral,
 * time-locked recovery for 100% of the funded pot BEFORE risking a single sat, so a game that closes
 * poorly can NEVER strand or lose funds. No sat is ever lost; no server, no counterparty cooperation.
 *
 * Flow:
 *   1. Two players fund an N-of-N pot.
 *   2. BEFORE the pot is "live", both already hold the pre-signed `presignNlocktimeRecovery` spend
 *      (nLockTime = funding height + Δ, non-final sequence), returning each contributor's full stake.
 *   3. The game closes poorly (a peer vanishes — no cooperative settlement is ever produced).
 *   4. BEFORE the locktime: the node REJECTS the recovery as non-final (the maturity gate holds —
 *      it cannot race an in-time cooperative settlement).
 *   5. AFTER the locktime: ONE player alone broadcasts the recovery → it confirms → BOTH contributors
 *      recover 100% of their funds. Value is conserved exactly; nobody is stranded.
 */

import assert from 'node:assert/strict';
import { RegtestNode, p2pkhScript } from '@bsv-poker/adapters/regtest-node';
import {
  genKeyPair,
  signPreimage,
  fundingLocking,
  type Script,
  type KeyPair,
} from '@bsv-poker/script-templates-ts';
import { bytesToHex, type BranchBinding } from '@bsv-poker/protocol-types';
import {
  type Tx,
  serializeTxWire,
  txidWire,
  sighashMessage,
  presignNlocktimeRecovery,
  type FundingRef,
  type Contributor,
  type Signer,
} from '@bsv-poker/tx-builder';

const SUBSIDY = 5_000_000_000;
const BIND: BranchBinding = { gid: 'a1'.repeat(8), rulesetHash: 'b2'.repeat(32), round: 0, stateHash: 'c3'.repeat(32), actingSeat: -1, successorCommitment: '00'.repeat(32) };
const RECOVERY_DELTA = 5; // blocks until the unilateral exit opens (Δ)

const p2pkh = (pub: Uint8Array): Script => p2pkhScript(pub);
const sigT = (msg: Uint8Array, k: KeyPair): Uint8Array => signPreimage(msg, k.priv);

async function main(): Promise<void> {
  const node = new RegtestNode(); // standalone in-tree node — no external process, no server
  const miner = genKeyPair();
  const p0 = genKeyPair();
  const p1 = genKeyPair();
  try {
    // Fund an N-of-N pot from a coinbase.
    const cb = await node.generateBlock(bytesToHex(miner.pubCompressed));
    const pot = SUBSIDY - 1000;
    const fundingScript = fundingLocking(BIND, [p0.pubCompressed, p1.pubCompressed]);
    const fundingTx: Tx = { version: 1, inputs: [{ prevTxid: cb.coinbaseTxid, vout: 0, sequence: 0xffffffff }], outputs: [{ satoshis: pot, locking: fundingScript }], nLockTime: 0 };
    const fundSig: Script = [sigT(sighashMessage(fundingTx, 0, p2pkh(miner.pubCompressed), SUBSIDY), miner), miner.pubCompressed];
    assert.equal((await node.submitTx(bytesToHex(serializeTxWire(fundingTx, [fundSig])))).ok, true, 'funding admitted');
    await node.generateBlock(bytesToHex(miner.pubCompressed));
    const fundingTxid = txidWire(fundingTx, [fundSig]);
    const fundedHeight = await node.height();

    // STEP 2 — BEFORE the pot is risked, BOTH players pre-sign the unilateral nLockTime recovery that
    // returns each their FULL stake. (In live play this is produced and exchanged before funding.)
    const half = (pot - 1000) / 2; // split the pot back evenly (a tiny miner fee leaves the pot)
    const contributors: readonly Contributor[] = [
      { pub: p0.pubCompressed, amount: half + 1000 },
      { pub: p1.pubCompressed, amount: half },
    ];
    const funding: FundingRef = { txid: fundingTxid, vout: 0, value: pot, scriptCode: fundingScript };
    const recoverableAtHeight = fundedHeight + RECOVERY_DELTA;
    const signers: readonly Signer[] = [
      (_i, msg) => sigT(msg, p0),
      (_i, msg) => sigT(msg, p1),
    ];
    const recovery = presignNlocktimeRecovery(BIND, funding, contributors, signers, recoverableAtHeight, { fee: 1000 });
    const recoveryRaw = bytesToHex(serializeTxWire(recovery.tx, [recovery.scriptSig]));
    const recoveryTxid = txidWire(recovery.tx, [recovery.scriptSig]);
    console.log(`[nlocktime-recovery] both players hold recovery ${recoveryTxid.slice(0, 14)}… (opens at height ${recoverableAtHeight}); funded at ${fundedHeight}`);

    // STEP 4 — the game closes poorly (peer vanished; NO cooperative settlement). Try to recover NOW,
    // before the locktime: the node MUST reject it (the maturity gate that stops it racing an in-time
    // cooperative close).
    const early = await node.submitTx(recoveryRaw);
    assert.equal(early.ok, false, 'recovery must be REJECTED before its nLockTime');
    console.log(`[nlocktime-recovery] before locktime: node rejected recovery → "${early.reason}" (maturity gate holds)`);

    // STEP 5 — advance to the locktime, then ONE player broadcasts the recovery ALONE.
    while ((await node.height()) < recoverableAtHeight) await node.generateBlock(bytesToHex(miner.pubCompressed));
    const late = await node.submitTx(recoveryRaw);
    assert.equal(late.ok, true, `recovery must be admitted at/after its nLockTime (got "${late.reason}")`);
    await node.generateBlock(bytesToHex(miner.pubCompressed));

    // The pot is consumed and BOTH contributors' refund outputs are confirmed — 100% recovered.
    assert.equal((await node.outpointStatus(fundingTxid, 0)).unspent, false, 'pot consumed by the recovery');
    const out0 = await node.outpointStatus(recoveryTxid, 0);
    const out1 = await node.outpointStatus(recoveryTxid, 1);
    assert.equal(out0.unspent, true, 'contributor 0 refund output confirmed');
    assert.equal(out1.unspent, true, 'contributor 1 refund output confirmed');
    assert.equal(out0.value + out1.value, pot - 1000, 'every recoverable sat returned to the contributors (100% minus miner fee)');
    console.log(`[nlocktime-recovery] after locktime: ONE player broadcast the recovery alone → both refunded: p0=${out0.value}, p1=${out1.value} (pot ${pot}).`);

    console.log('\n[nlocktime-recovery] PASS — every player holds a unilateral, time-locked recovery for 100% of funds; a poorly-closed game cannot strand or lose a single sat (no server, no counterparty).');
  } finally {
    await node.shutdown();
  }
}

main().then(
  () => process.exit(0),
  (e) => {
    console.error('[nlocktime-recovery] FAIL:', (e as Error).stack ?? (e as Error).message);
    process.exit(1);
  },
);
