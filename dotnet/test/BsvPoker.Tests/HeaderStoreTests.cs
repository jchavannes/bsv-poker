using BsvPoker.Core;
using BsvPoker.Crypto;
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

    private static byte[] RandomBytes() => System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);

    // mine a header carrying a specific merkle root (so an SPV proof against it verifies)
    private static BlockHeader MineHeaderWithRoot(byte[] prevInternal, byte[] merkleRoot)
    {
        for (uint nonce = 1; ; nonce++)
        {
            var h = new BlockHeader(1, prevInternal, merkleRoot, 1_700_000_000, EasyBits, nonce);
            if (h.MeetsPow()) return h;
        }
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

        T.Run("BuildChain re-validates the persisted headers into an indexed chain", () =>
        {
            var path = Path.Combine(Path.GetTempPath(), "bsvpoker-hs-" + Guid.NewGuid().ToString("N") + ".dat");
            try
            {
                var store = new HeaderStore(path);
                var chain = Chain(genesis, 4);
                store.Append(chain);
                var (built, loaded) = new HeaderStore(path).BuildChain();
                T.Eq(loaded, 4, "all 4 re-validated");
                T.Eq(built.Height, 3, "tip height (0-based) after genesis + 3");
                T.True(built.Knows(chain[^1].HashHex()), "tip indexed by hash");
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        });

        T.Run("BuildChain stops at a corrupt tail (never trusts unlinked headers)", () =>
        {
            var path = Path.Combine(Path.GetTempPath(), "bsvpoker-hs-" + Guid.NewGuid().ToString("N") + ".dat");
            try
            {
                var store = new HeaderStore(path);
                var good = Chain(genesis, 3);
                store.Append(good);
                store.Append(new[] { Mk(RandomBytes(), 77) }); // an orphan appended after the valid chain
                var (_, loaded) = new HeaderStore(path).BuildChain();
                T.Eq(loaded, 3, "only the linked prefix is trusted");
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        });

        T.Run("SPV funding verifies against the PERSISTED, self-validated chain", () =>
        {
            var path = Path.Combine(Path.GetTempPath(), "bsvpoker-hs-" + Guid.NewGuid().ToString("N") + ".dat");
            try
            {
                var me = Secp256k1.GenerateKeyPair();
                var other = Secp256k1.GenerateKeyPair();
                // a funding tx paying us 55000 at vout 1, placed in a block
                var fundTx = new BsvPoker.Core.Chain.Tx(2,
                    new() { new("ab".PadRight(64, '7'), 0, Array.Empty<byte>(), 0xffffffff) },
                    new() { new(9000, BsvPoker.Core.Chain.P2pkhLockForPub(other.Pub)),
                            new(55000, BsvPoker.Core.Chain.P2pkhLockForPub(me.Pub)) }, 0);
                var leaf = Hashes.Sha256d(BsvPoker.Core.Chain.Serialize(fundTx));
                var leaves = new List<byte[]>();
                for (int i = 0; i < 4; i++) { var b = new byte[32]; b[0] = (byte)(i + 1); leaves.Add(b); }
                int idx = 2; leaves.Insert(idx, leaf);
                var root = MerkleProof.Root(leaves);
                var branch = MerkleProof.Branch(leaves, idx);

                // the block carrying that tx is the genesis of a persisted chain (validated on load)
                var header = MineHeaderWithRoot(genesis, root);
                var store = new HeaderStore(path);
                store.Append(new[] { header });
                var (chain, loaded) = new HeaderStore(path).BuildChain();
                T.Eq(loaded, 1, "persisted block validated");

                var proof = new SpvFunding.Proof(fundTx, 1, header.HashHex(), branch, idx);
                var utxo = SpvFunding.Verify(proof, chain, me.Pub, 0, 0);
                T.True(utxo != null, "UTXO learned from a persisted, self-validated block");
                T.Eq(utxo!.Value, 55000L, "value picked up");

                // a proof whose block is NOT in the persisted chain is rejected
                var bogus = new SpvFunding.Proof(fundTx, 1, new string('0', 64), branch, idx);
                T.True(SpvFunding.Verify(bogus, chain, me.Pub, 0, 0) == null, "unknown block rejected");
            }
            finally { if (File.Exists(path)) File.Delete(path); }
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
