using BsvPoker.Core;
using BsvPoker.Crypto;

namespace BsvPoker.Net.Bsv;

/// <summary>
/// SPV funding: how the wallet learns its UTXOs from the chain WITHOUT a server. A payer hands the
/// recipient an SPV envelope — the funding transaction plus a merkle proof that it is in a block — and the
/// recipient verifies it against the header chain it validated itself: the tx is in the block, and the
/// claimed output actually pays one of the recipient's keys. Only then is the UTXO accepted. This is the
/// pure peer-to-peer BSV SPV model (no indexer, no API).
/// </summary>
public static class SpvFunding
{
    /// <param name="Tx">the funding transaction.</param>
    /// <param name="Vout">the output index that pays us.</param>
    /// <param name="BlockHashHex">the block (display hash) that contains it — must be in our validated header chain.</param>
    /// <param name="Branch">the merkle branch proving inclusion.</param>
    /// <param name="TxIndex">the tx's position in the block.</param>
    public sealed record Proof(Chain.Tx Tx, uint Vout, string BlockHashHex, byte[][] Branch, int TxIndex);

    /// <summary>
    /// Verify an SPV funding proof and, if valid, return the resulting wallet UTXO; null if the proof is
    /// bad, the block is not in our validated chain, or the output does not pay <paramref name="myPub33"/>.
    /// </summary>
    public static OnChainWallet.Utxo? Verify(Proof p, HeadersChain chain, byte[] myPub33, uint keyChain, uint keyIndex)
    {
        try
        {
            var entry = chain.Get(p.BlockHashHex);
            if (entry == null) return null;                                  // block not in the validated header chain
            if (p.Vout >= p.Tx.Outs.Count) return null;
            var leaf = Hashes.Sha256d(Chain.Serialize(p.Tx));               // txid in internal byte order
            if (!MerkleProof.Verify(leaf, p.TxIndex, p.Branch, entry.Header.MerkleRoot)) return null;
            var expected = Chain.P2pkhLockForPub(myPub33);
            if (!p.Tx.Outs[(int)p.Vout].Script.AsSpan().SequenceEqual(expected)) return null; // must pay our key
            return new OnChainWallet.Utxo(Chain.Txid(p.Tx), p.Vout, p.Tx.Outs[(int)p.Vout].Value, keyChain, keyIndex);
        }
        catch { return null; }
    }
}
