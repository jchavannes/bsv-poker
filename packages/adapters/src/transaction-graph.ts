/**
 * Canonical, VALIDATED transaction graph (audit finding #31). The truth a settlement rests on is the
 * validated transaction graph — not an indexer projection. This component reconstructs that graph from
 * raw transactions and validates the consensus-level structural invariants every honest node enforces:
 *
 *   - PARENT EXISTENCE: every input spends an output that exists in the graph (a prior tx) or a
 *     registered root (e.g. a mined coinbase) — no spending of thin air;
 *   - NO DOUBLE-SPEND: each outpoint is spent at most once;
 *   - VALUE CONSERVATION: a tx's outputs never exceed its inputs (no value creation; the difference is
 *     the fee).
 *
 * It uses the project's own hardened `parseTxWire` (bounded, never panics on hostile bytes) and the
 * canonical `hash256` txid — no second tx parser. The result is an authoritative UTXO set + DAG that
 * an indexer/SDK/auditor can rebuild independently from the same transactions (P2) and that matches
 * what the node accepted. This is the canonical graph the indexer (a mere projection, ADR 0005) is NOT.
 */
import { parseTxWire, type ParsedTx } from '@bsv-poker/tx-builder';
import { hash256, bytesToHex, tryHexToBytes } from '@bsv-poker/protocol-types';

/** An unspent output in the graph. */
export interface GraphOutput {
  readonly txid: string;
  readonly vout: number;
  readonly satoshis: bigint;
  readonly script: Uint8Array;
}

export type AddResult =
  | { readonly ok: true; readonly txid: string; readonly fee: bigint }
  | { readonly ok: false; readonly reason: string };

/** Display txid (big-endian hex) of a raw tx: reverse(double-SHA256(raw)) — matches `txidWire`. */
function txidOf(raw: Uint8Array): string {
  return bytesToHex(Uint8Array.from([...hash256(raw)].reverse()));
}

const op = (txid: string, vout: number): string => `${txid}:${vout}`;

export class TransactionGraph {
  private readonly txs = new Map<string, ParsedTx>();
  private readonly utxo = new Map<string, GraphOutput>();
  private readonly spentBy = new Map<string, string>(); // outpoint -> spending txid

  /**
   * Register a pre-existing outpoint (e.g. a mined coinbase) the graph may spend from, without a
   * parent tx in the graph. Roots are the only inputs allowed to have no in-graph producer.
   */
  addRoot(txid: string, vout: number, satoshis: bigint, script: Uint8Array = new Uint8Array()): void {
    this.utxo.set(op(txid, vout), { txid, vout, satoshis, script });
  }

  /**
   * Add a transaction by raw hex. Validates parent existence, no double-spend, and value conservation
   * BEFORE committing it. On success the spent inputs are removed from the UTXO set and the new outputs
   * added; the returned `fee` is inputs − outputs. Never throws on hostile bytes (parse is bounded).
   */
  add(rawHex: string): AddResult {
    const bytes = tryHexToBytes(rawHex);
    if (bytes === null) return { ok: false, reason: 'not valid hex' };
    const r = parseTxWire(bytes);
    if (!r.ok) return { ok: false, reason: `parse failed: ${r.reason}` };
    const tx = r.tx;
    const txid = txidOf(bytes);
    if (this.txs.has(txid)) return { ok: false, reason: `duplicate txid ${txid}` };

    let inSum = 0n;
    const spends: string[] = [];
    for (const inp of tx.inputs) {
      const key = op(inp.prevTxid, inp.vout);
      if (this.spentBy.has(key)) return { ok: false, reason: `double-spend of ${key} (already spent by ${this.spentBy.get(key)})` };
      const parent = this.utxo.get(key);
      if (!parent) return { ok: false, reason: `input ${key} has no producing output in the graph (missing parent)` };
      inSum += parent.satoshis;
      spends.push(key);
    }
    const outSum = tx.outputs.reduce((s, o) => s + o.satoshis, 0n);
    if (outSum > inSum) return { ok: false, reason: `value creation: outputs ${outSum} > inputs ${inSum}` };

    // Commit atomically (all checks passed).
    this.txs.set(txid, tx);
    for (const key of spends) {
      this.spentBy.set(key, txid);
      this.utxo.delete(key);
    }
    tx.outputs.forEach((o, vout) => this.utxo.set(op(txid, vout), { txid, vout, satoshis: o.satoshis, script: o.script }));
    return { ok: true, txid, fee: inSum - outSum };
  }

  /** The current unspent outputs (the canonical UTXO set this graph represents). */
  utxos(): GraphOutput[] {
    return [...this.utxo.values()];
  }

  /** Is `txid` known to the graph? */
  has(txid: string): boolean {
    return this.txs.has(txid);
  }

  /** Is this outpoint unspent in the graph? */
  isUnspent(txid: string, vout: number): boolean {
    return this.utxo.has(op(txid, vout)) && !this.spentBy.has(op(txid, vout));
  }

  /** Number of transactions in the graph (roots excluded). */
  size(): number {
    return this.txs.size;
  }
}
