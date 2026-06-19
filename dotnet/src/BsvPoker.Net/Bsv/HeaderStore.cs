using System.IO;

namespace BsvPoker.Net.Bsv;

/// <summary>
/// A persistent, append-only store of validated block headers (80 bytes each), so the node does not
/// re-download the header chain on every launch and can resume sync from where it left off. On load the
/// stored chain is re-validated (PoW + parent linkage) so a corrupted/truncated file can never be trusted.
///
/// The parsed headers and the validated <see cref="HeadersChain"/> are cached in memory. Callers poll these
/// on timers (header sync every ~2s, SPV rescan every ~20s, wallet refresh), and re-reading + re-parsing the
/// whole file — and, in BuildChain, re-validating PoW over every header — on each call is redundant O(n)
/// work once the chain is large. Caching does that work once and reuses it until the next append, avoiding
/// the repeated disk reads and re-validation. The cache is invalidated whenever the file grows (an append
/// through this instance, or an external writer detected via file length), so reads stay correct. All cached
/// state is guarded by a lock because the sync loop and the SPV rescan touch the same instance from
/// different threads.
/// </summary>
public sealed class HeaderStore
{
    private readonly string _path;
    private readonly object _gate = new();
    private List<BlockHeader>? _cache;   // parsed headers in file order; only read/written under _gate
    private long _cacheLen = -1;         // file length the cache reflects (detects external growth)
    private HeadersChain? _chain;        // validated chain built from _cache; published immutable, rebuilt on append
    private int _chainLoaded;            // headers accepted into _chain

    public HeaderStore(string path)
    {
        _path = path;
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
    }

    /// <summary>Number of headers on disk (file length / 80).</summary>
    public int Count => File.Exists(_path) ? (int)(new FileInfo(_path).Length / 80) : 0;

    /// <summary>The parsed headers, reloading from disk only if the file has grown since the cache was built.
    /// Caller must hold <see cref="_gate"/>.</summary>
    private List<BlockHeader> EnsureCache()
    {
        long len = File.Exists(_path) ? new FileInfo(_path).Length : 0;
        if (_cache != null && _cacheLen == len) return _cache;
        var list = new List<BlockHeader>((int)(len / 80));
        if (len > 0)
        {
            var bytes = File.ReadAllBytes(_path);
            for (int o = 0; o + 80 <= bytes.Length; o += 80) list.Add(BlockHeader.Parse(bytes.AsSpan(o, 80)));
        }
        _cache = list; _cacheLen = len; _chain = null; _chainLoaded = 0;
        return _cache;
    }

    /// <summary>Append validated headers to the store and keep the in-memory cache coherent.</summary>
    public void Append(IEnumerable<BlockHeader> headers)
    {
        var list = headers as IReadOnlyList<BlockHeader> ?? headers.ToList();
        lock (_gate)
        {
            long before = File.Exists(_path) ? new FileInfo(_path).Length : 0;
            using (var fs = new FileStream(_path, FileMode.Append, FileAccess.Write, FileShare.Read))
                foreach (var h in list) { var b = h.Serialize(); fs.Write(b, 0, b.Length); }
            // Extend the cache in place if it was current; otherwise drop it so the next read reloads from disk.
            if (_cache != null && _cacheLen == before)
            {
                foreach (var h in list) _cache.Add(h);
                _cacheLen = before + (long)list.Count * 80;
            }
            else _cache = null;
            _chain = null; _chainLoaded = 0;   // the validated chain must be rebuilt to include the new headers
        }
    }

    /// <summary>All stored headers (a snapshot copy, safe to use without holding the lock).</summary>
    public List<BlockHeader> Load()
    {
        lock (_gate) return new List<BlockHeader>(EnsureCache());
    }

    /// <summary>The hash (internal order) of the last stored header, or the given genesis if empty — used as the next sync locator.</summary>
    public byte[] TipOrGenesis(byte[] genesisInternal)
    {
        lock (_gate)
        {
            var hs = EnsureCache();
            return hs.Count == 0 ? genesisInternal : hs[^1].Hash();
        }
    }

    /// <summary>
    /// Load the persisted headers into an indexed, re-validated <see cref="HeadersChain"/> (each header must
    /// link to its parent and meet PoW, exactly as when first received). This is what SPV funding verifies a
    /// payment's merkle proof against — the wallet trusts a UTXO only if its block is in a chain the client
    /// validated itself. Returns the chain and how many headers were accepted (a corrupt tail stops loading).
    /// The result is cached and reused until the next append, so repeated callers do not re-validate the whole
    /// chain; the returned chain is never mutated after it is published.
    /// </summary>
    public (HeadersChain Chain, int Loaded) BuildChain()
    {
        lock (_gate)
        {
            if (_chain != null) return (_chain, _chainLoaded);
            var headers = EnsureCache();
            var chain = new HeadersChain();
            int loaded = 0;
            for (int i = 0; i < headers.Count; i++)
            {
                var r = i == 0 ? chain.AddGenesis(headers[i]) : chain.Add(headers[i]);
                if (r is HeadersChain.AddResult.Accepted or HeadersChain.AddResult.Reorg) loaded++;
                else break; // first header that does not validate/link → stop trusting the rest
            }
            _chain = chain; _chainLoaded = loaded;
            return (_chain, _chainLoaded);
        }
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
