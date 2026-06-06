// A played card becomes a 1-sat NFT — lazily, peer-issued from the player's own sats, NO banker;
// concealed until played; provable by reveal; network-agnostic.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { issueCardOnPlay, verifyCardNftReveal, isCardNftOutput, CARD_NFT_SATS } from '../src/index.ts';
import { makeRealCT } from '@bsv-poker/crypto-mentalpoker';
import { genKeyPair, containsOpReturn } from '@bsv-poker/script-templates-ts';
import type { TxInput } from '@bsv-poker/tx-builder';

const deckId = 'ab'.repeat(32); // 32-byte hex
const serial = 7;
const face = 42;
const blind = new Uint8Array(32).fill(9);
const owner = genKeyPair();
const funding: TxInput = { prevTxid: 'cd'.repeat(32), vout: 0, sequence: 0xffffffff }; // the player's OWN sats

test('a played card is issued as a 1-sat NFT from the PLAYER\'s own sats (no banker)', async () => {
  const commitment = await makeRealCT().conceal(deckId, serial, face, blind);
  const issued = issueCardOnPlay({ deckId, serial, commitment, ownerPub: owner.pubCompressed, funding });

  // exactly ONE input — the player's own funding; there is no bank/dealer input
  assert.equal(issued.tx.inputs.length, 1);
  assert.equal(issued.tx.inputs[0]!.prevTxid, funding.prevTxid);
  // the NFT is a 1-sat output, card-shaped, with NO OP_RETURN
  assert.equal(issued.tx.outputs[0]!.satoshis, CARD_NFT_SATS);
  assert.equal(isCardNftOutput(issued.tx.outputs[0]!), true);
  assert.equal(containsOpReturn(issued.tx.outputs[0]!.locking), false);
  assert.equal(issued.outpoint.txid.length, 64);
});

test('the issued NFT provably opens to the played card (reveal); a wrong card cannot forge it', async () => {
  const commitment = await makeRealCT().conceal(deckId, serial, face, blind);
  assert.equal(await verifyCardNftReveal(commitment, face, blind), true); // the real card
  assert.equal(await verifyCardNftReveal(commitment, face + 1, blind), false); // wrong face — concealment holds
  assert.equal(await verifyCardNftReveal(commitment, face, new Uint8Array(32).fill(8)), false); // wrong blind
});

test('concealment: the commitment reveals nothing about the face before issuance', async () => {
  // two different faces under the same blind produce different commitments, and neither commitment
  // discloses its face without the (face,blind) opening — you can only CHECK a guess, not read it.
  const cA = await makeRealCT().conceal(deckId, serial, 3, blind);
  const cB = await makeRealCT().conceal(deckId, serial, 4, blind);
  assert.notEqual(cA, cB);
  assert.equal(await verifyCardNftReveal(cA, 4, blind), false); // can't pass A off as face 4
});
