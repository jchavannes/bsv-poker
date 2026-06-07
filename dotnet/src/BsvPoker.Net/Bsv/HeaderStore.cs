using System.IO;

namespace BsvPoker.Net.Bsv;

/// <summary>
/// A persistent, append-only store of validated block headers (80 bytes each), so the node does not
/// re-download the header chain on every launch and can resume sync from where it left off. On load the
/// stored chain is re-validated (PoW + parent linkage) so a corrupted/truncated file can never be trusted.
/// </summary>
public sealed class HeaderStore
{
    private readonly string _path;

    public HeaderStore(string path)
    {
        _path = path;
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
    }

    /// <summary>Number of headers on disk (file length / 80).</summary>
    public int Count => File.Exists(_path) ? (int)(new FileInfo(_path).Length / 80) : 0;

    /// <summary>Append validated headers to the store.</summary>
    public void Append(IEnumerable<BlockHeader> headers)
    {
        using var fs = new FileStream(_path, FileMode.Append, FileAccess.Write, FileShare.Read);
        foreach (var h in headers) { var b = h.Serialize(); fs.Write(b, 0, b.Length); }
    }

    /// <summary>Load all stored headers (raw; validate with <see cref="ValidatePrefix"/>).</summary>
    public List<BlockHeader> Load()
    {
        var outp = new List<BlockHeader>();
        if (!File.Exists(_path)) return outp;
        var bytes = File.ReadAllBytes(_path);
        for (int o = 0; o + 80 <= bytes.Length; o += 80) outp.Add(BlockHeader.Parse(bytes.AsSpan(o, 80)));
        return outp;
    }

    /// <summary>The hash (internal order) of the last stored header, or the given genesis if empty — used as the next sync locator.</summary>
    public byte[] TipOrGenesis(byte[] genesisInternal)
    {
        var hs = Load();
        return hs.Count == 0 ? genesisInternal : hs[^1].Hash();
    }

    /// <summary>
    /// Load the persisted headers into an indexed, re-validated <see cref="HeadersChain"/> (each header must
    /// link to its parent and meet PoW, exactly as when first received). This is what SPV funding verifies a
    /// payment's merkle proof against — the wallet trusts a UTXO only if its block is in a chain the client
    /// validated itself. Returns the chain and how many headers were accepted (a corrupt tail stops loading).
    /// </summary>
    public (HeadersChain Chain, int Loaded) BuildChain()
    {
        var chain = new HeadersChain();
        var headers = Load();
        int loaded = 0;
        for (int i = 0; i < headers.Count; i++)
        {
            var r = i == 0 ? chain.AddGenesis(headers[i]) : chain.Add(headers[i]);
            if (r is HeadersChain.AddResult.Accepted or HeadersChain.AddResult.Reorg) loaded++;
            else break; // first header that does not validate/link → stop trusting the rest
        }
        return (chain, loaded);
    }

    /// <summary>Count how many leading headers form a valid chain from <paramref name="genesisInternal"/> (PoW + linkage).</summary>
    public static int ValidatePrefix(IReadOnlyList<BlockHeader> headers, byte[] genesisInternal)
    {
        int valid = 0; var prev = genesisInternal;
        foreach (var h in headers)
        {
            if (!h.MeetsPow() || !h.PrevHash.AsSpan().SequenceEqual(prev)) break;
            prev = h.Hash(); valid++;
        }
        return valid;
    }
}
