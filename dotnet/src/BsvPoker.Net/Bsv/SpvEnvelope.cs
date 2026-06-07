using BsvPoker.Core;
using BsvPoker.Crypto;

namespace BsvPoker.Net.Bsv;

/// <summary>
/// BSV peer-to-peer SPV — the real model (Bitcoin whitepaper §8). A coin is delivered as an ENVELOPE: the
/// raw transaction + its merkle proof + the block header that proves it was mined. The SENDER (Alice) has
/// STORED that envelope (from when she received the coin) and hands it to the payee (Bob) IP-to-IP WITH the
/// payment. Bob's wallet VERIFIES it locally — the merkle branch folds up to the header's merkle root AND the
/// header itself meets its proof-of-work — and STORES it, so Bob can in turn hand it to whoever he pays.
/// The proof arrives WITH the money, so verification is instant against the block headers the always-online
/// client maintains — no node query and no chain scan needed to verify a payment. This is SPV (whitepaper
/// §8), not a BTC full node. The client is online 24/7: it keeps headers current and gossips to find peers.
/// </summary>
public sealed record SpvEnvelope(byte[] RawTx, byte[] Header80, byte[][] Branch, int Index)
{
    /// <summary>The display txid of the enveloped transaction.</summary>
    public string Txid() { try { return Chain.Txid(Chain.Deserialize(RawTx)); } catch { return ""; } }

    /// <summary>
    /// Prove the transaction was mined: the merkle proof reaches the header's merkle root AND the header
    /// meets its stated proof-of-work. TOTAL — returns false on any malformed input. No network involved.
    /// </summary>
    public bool Verify()
    {
        try
        {
            var tx = Chain.Deserialize(RawTx);
            var hdr = BlockHeader.Parse(Header80);
            if (!hdr.MeetsPow()) return false;                         // the header is real work
            var txidInternal = Hashes.Sha256d(Chain.Serialize(tx));    // internal (little-endian) txid
            return MerkleProof.Verify(txidInternal, Index, Branch, hdr.MerkleRoot);
        }
        catch { return false; }
    }

    /// <summary>The parsed transaction (or null if malformed).</summary>
    public Chain.Tx? ParseTx() { try { return Chain.Deserialize(RawTx); } catch { return null; } }

    // ---- wire form (hex) so an envelope can ride inside a Bitcoin transaction / be handed IP-to-IP ----
    public string ToWire() =>
        Convert.ToHexString(RawTx) + "|" + Convert.ToHexString(Header80) + "|" +
        string.Join(",", Branch.Select(Convert.ToHexString)) + "|" + Index;

    public static SpvEnvelope FromWire(string s)
    {
        var p = s.Split('|');
        var branch = p[2].Length == 0 ? Array.Empty<byte[]>() : p[2].Split(',').Select(Convert.FromHexString).ToArray();
        return new SpvEnvelope(Convert.FromHexString(p[0]), Convert.FromHexString(p[1]), branch, int.Parse(p[3]));
    }
}
