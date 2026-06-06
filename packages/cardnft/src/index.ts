/**
 * @bsv-poker/cardnft — a played poker card as a lazily-issued 1-sat BSV NFT (UTXO).
 *
 * MODEL (bsv-poker, TRUE P2P, NO banker): cards are concealed by mental poker — even the holder
 * cannot tell a card before it is revealed. A card becomes a REAL on-chain NFT ONLY when it is
 * PLAYED: at play time the player issues a 1-sat output, peer-issued from THEIR OWN sats (a funding
 * input they control), whose locking script carries the card's public identity (deckId ‖ serial) and
 * its hiding commitment `H(face‖blind)` as pushdata (`<state> OP_DROP <ownerPub> OP_CHECKSIG`, never
 * OP_RETURN). There is NO dealer/bank/central issuer — the sats and the NFT come from the player.
 *
 * Network-agnostic: the SAME model applies on regtest, testnet and mainnet — the network is only a
 * parameter elsewhere (address/wallet), never a branch in this logic.
 *
 * WHAT/HOW/WHY:
 *  - WHAT: cardNftLocking/issueCardOnPlay produce the 1-sat card NFT; verifyCardNftReveal proves the
 *    issued NFT opens to the played card; isCardNftOutput recognises one.
 *  - HOW: the hiding commitment is the mental-poker commitment (`@bsv-poker/crypto-mentalpoker`
 *    conceal = H(face‖blind)); issuance spends the player's funding UTXO and creates the 1-sat NFT.
 *  - WHY: "issued only when played" + "no banker" — an unplayed card stays concealed and has no
 *    on-chain footprint; when played it becomes the player's own peer-issued NFT, provable by reveal.
 */
import { OP, serializeScript, containsOpReturn, type Script } from '@bsv-poker/script-templates-ts';
import { txid, type Tx, type TxInput, type TxOutput } from '@bsv-poker/tx-builder';
import { makeRealCT } from '@bsv-poker/crypto-mentalpoker';

export const CARD_NFT_SATS = 1;
const CARD_TAG = new TextEncoder().encode('BSVPOKER_CARD_NFT_V1');

function hexToBytes(h: string, label: string): Uint8Array {
  if (typeof h !== 'string' || h.length % 2 !== 0 || !/^[0-9a-fA-F]*$/.test(h)) throw new Error(`${label}: bad hex`);
  const out = new Uint8Array(h.length / 2);
  for (let i = 0; i < out.length; i++) out[i] = parseInt(h.slice(i * 2, i * 2 + 2), 16);
  return out;
}
function u32be(n: number): Uint8Array {
  if (!Number.isInteger(n) || n < 0 || n > 0xffffffff) throw new Error('serial out of u32 range');
  return new Uint8Array([(n >>> 24) & 0xff, (n >>> 16) & 0xff, (n >>> 8) & 0xff, n & 0xff]);
}
function concat(...parts: Uint8Array[]): Uint8Array {
  let n = 0;
  for (const p of parts) n += p.length;
  const out = new Uint8Array(n);
  let o = 0;
  for (const p of parts) {
    out.set(p, o);
    o += p.length;
  }
  return out;
}

/** Public on-chain card identity blob: TAG ‖ deckId(32) ‖ serial(u32 BE) ‖ commitment(32). */
export function cardNftState(deckId: string, serial: number, commitment: string): Uint8Array {
  const deck = hexToBytes(deckId, 'deckId');
  const cmt = hexToBytes(commitment, 'commitment');
  if (deck.length !== 32) throw new Error('deckId must be 32 bytes');
  if (cmt.length !== 32) throw new Error('commitment must be 32 bytes');
  return concat(CARD_TAG, deck, u32be(serial), cmt);
}

/** 1-sat card NFT locking: `<state> OP_DROP <ownerPub> OP_CHECKSIG` (no OP_RETURN). */
export function cardNftLocking(deckId: string, serial: number, commitment: string, ownerPub: Uint8Array): Script {
  if (!(ownerPub instanceof Uint8Array) || ownerPub.length !== 33) throw new Error('ownerPub must be 33-byte compressed');
  return [cardNftState(deckId, serial, commitment), OP.OP_DROP, ownerPub, OP.OP_CHECKSIG];
}

export interface IssueArgs {
  readonly deckId: string;
  readonly serial: number;
  readonly commitment: string;
  readonly ownerPub: Uint8Array;
  readonly funding: TxInput; // the PLAYER'S OWN sats UTXO — peer-issued, no banker
  readonly change?: TxOutput;
}
export interface CardNftIssue {
  readonly tx: Tx;
  readonly outpoint: { readonly txid: string; readonly vout: number };
  readonly locking: Script;
}

/**
 * Issue a played card as a 1-sat NFT — ONLY at play time, peer-issued from the player's OWN sats
 * (`funding`). NO banker/dealer/central issuer. Network-agnostic (same model on every network).
 */
export function issueCardOnPlay(a: IssueArgs): CardNftIssue {
  const locking = cardNftLocking(a.deckId, a.serial, a.commitment, a.ownerPub);
  const nft: TxOutput = { satoshis: CARD_NFT_SATS, locking };
  const outputs: TxOutput[] = a.change ? [nft, a.change] : [nft];
  const tx: Tx = { version: 1, inputs: [a.funding], outputs, nLockTime: 0 };
  return { tx, outpoint: { txid: txid(tx), vout: 0 }, locking };
}

/** Reveal proof: the NFT's commitment opens to (face, blind) — i.e. it IS the played card. Total. */
export async function verifyCardNftReveal(commitment: string, face: number, blind: Uint8Array): Promise<boolean> {
  try {
    return await makeRealCT().verifyReveal(commitment, face, blind);
  } catch {
    return false;
  }
}

/** Structural check: a 1-sat output shaped as a card NFT (no OP_RETURN). */
export function isCardNftOutput(o: TxOutput): boolean {
  if (o.satoshis !== CARD_NFT_SATS) return false;
  if (containsOpReturn(o.locking)) return false;
  const s = o.locking;
  return (
    s.length === 4 &&
    s[0] instanceof Uint8Array &&
    (s[0] as Uint8Array).length === CARD_TAG.length + 32 + 4 + 32 &&
    s[1] === OP.OP_DROP &&
    s[2] instanceof Uint8Array &&
    (s[2] as Uint8Array).length === 33 &&
    s[3] === OP.OP_CHECKSIG
  );
}

export { serializeScript };
