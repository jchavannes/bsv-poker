using BsvPoker.Net.Bsv;

namespace BsvPoker.Tests;

/// <summary>
/// The persistent header store: validated headers survive a restart (append-only 80-byte records), the
/// stored chain re-validates from genesis on load (PoW + parent linkage), corruption/truncation is caught,
/// and the tip locator advances so sync resumes instead of restarting from genesis.
/// </summary>
public static class HeaderStoreTests
{
    private const uint EasyBits = 0x207fffff; // regtest powlimit — any hash passes, so PoW search is trivial

    private static BlockHeader Mk(byte[] prevInternal, uint salt)
    {
        for (uint nonce = salt * 100000; ; nonce++)
        {
            var h = new BlockHeader(1, prevInternal, new byte[32], 1_700_000_000, EasyBits, nonce);
            if (h.MeetsPow()) return h;
        }
    }

    // a fresh linked chain of n headers built on the given genesis-parent
    private static List<BlockHeader> Chain(byte[] genesisInternal, int n)
    {
        var hs = new List<BlockHeader>();
        var prev = genesisInternal;
        for (uint i = 0; i < n; i++) { var h = Mk(prev, i + 1); hs.Add(h); prev = h.Hash(); }
        return hs;
    }

    public static void All()
    {
        Console.WriteLine("BSV node — persistent header store:");
        var genesis = new byte[32]; // the chain's first header builds on all-zero prev

        T.Run("append then reload returns the same headers in order", () =>
        {
            var path = Path.Combine(Path.GetTempPath(), "bsvpoker-hs-" + Guid.NewGuid().ToString("N") + ".dat");
            try
            {
                var store = new HeaderStore(path);
                T.Eq(store.Count, 0, "empty to start");
                var chain = Chain(genesis, 5);
                store.Append(chain);
                T.Eq(store.Count, 5, "5 headers on disk");

                var reloaded = new HeaderStore(path).Load();
                T.Eq(reloaded.Count, 5, "reloaded count");
                for (int i = 0; i < 5; i++)
                    T.Eq(reloaded[i].HashHex(), chain[i].HashHex(), "header " + i + " survives reload");
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        });

        T.Run("resume: a second sync appends past the persisted tip", () =>
        {
            var path = Path.Combine(Path.GetTempPath(), "bsvpoker-hs-" + Guid.NewGuid().ToString("N") + ".dat");
            try
            {
                var store = new HeaderStore(path);
                var first = Chain(genesis, 3);
                store.Append(first);
                // the tip locator is the last stored header — a fresh sync continues from there
                var resume = new HeaderStore(path);
                T.Eq(T.Hex(resume.TipOrGenesis(genesis)), T.Hex(first[^1].Hash()), "tip locator = last stored header");
                var more = Chain(first[^1].Hash(), 4);
                resume.Append(more);
                var all = resume.Load();
                T.Eq(all.Count, 7, "3 + 4 persisted");
                T.Eq(HeaderStore.ValidatePrefix(all, genesis), 7, "the whole resumed chain validates from genesis");
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        });

        T.Run("empty store reports genesis as the sync locator", () =>
        {
            var path = Path.Combine(Path.GetTempPath(), "bsvpoker-hs-" + Guid.NewGuid().ToString("N") + ".dat");
            try
            {
                var store = new HeaderStore(path);
                T.Eq(T.Hex(store.TipOrGenesis(genesis)), T.Hex(genesis), "no headers → start from genesis");
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        });

        T.Run("a broken-linkage chain only validates up to the break", () =>
        {
            // splice an orphan into the middle: prefix validation must stop there, never trusting the tail
            var good = Chain(genesis, 3);
            var orphan = Mk(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32), 99);
            var spliced = new List<BlockHeader> { good[0], good[1], orphan, good[2] };
            T.Eq(HeaderStore.ValidatePrefix(spliced, genesis), 2, "valid only through the last linked header");
        });

        T.Run("a tampered (bad-PoW) header fails prefix validation", () =>
        {
            var good = Chain(genesis, 2);
            // forge a child whose PoW does not meet a hard target
            var bad = new BlockHeader(1, good[^1].Hash(), new byte[32], 1_700_000_000, 0x1d00ffff, 1);
            var chain = new List<BlockHeader> { good[0], good[1], bad };
            T.Eq(HeaderStore.ValidatePrefix(chain, genesis), 2, "bad-PoW header excluded");
        });

        T.Run("a truncated (non-80-multiple) file loads only whole headers", () =>
        {
            var path = Path.Combine(Path.GetTempPath(), "bsvpoker-hs-" + Guid.NewGuid().ToString("N") + ".dat");
            try
            {
                var store = new HeaderStore(path);
                store.Append(Chain(genesis, 2));
                // corrupt: append 37 stray bytes (a partial header write, e.g. crash mid-flush)
                File.AppendAllText(path, new string('x', 37));
                var reloaded = new HeaderStore(path).Load();
                T.Eq(reloaded.Count, 2, "partial trailing record ignored, whole headers intact");
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        });
    }
}
