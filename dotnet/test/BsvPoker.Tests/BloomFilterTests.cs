using System.Text;
using BsvPoker.Core;
using BsvPoker.Crypto;
using BsvPoker.Net.Bsv;

namespace BsvPoker.Tests;

/// <summary>
/// The SPV connection bloom filter: it must (a) never give a false negative — every inserted element is found;
/// (b) serialize to the exact bytes a real BSV peer expects (the canonical reference vectors); and (c) match
/// the hash160 that a P2PKH payment TO us pushes, which is what makes a peer relay our incoming payment. A
/// false positive only wastes bandwidth (the wallet re-verifies every match), so correctness rests on (a)-(c).
/// </summary>
public static class BloomFilterTests
{
    private static string Hex(IReadOnlyList<byte> b) => Convert.ToHexString(b.ToArray()).ToLowerInvariant();

    public static void All()
    {
        Console.WriteLine("SPV bloom filter (address discovery, no server):");

        T.Run("MurmurHash3 (x86-32) matches known vectors", () =>
        {
            T.Eq(BloomFilter.MurmurHash3(0, Array.Empty<byte>()), 0u, "empty, seed 0");
            T.Eq(BloomFilter.MurmurHash3(1, Array.Empty<byte>()), 0x514E28B7u, "empty, seed 1");
        });

        // The canonical reference filter vector: build for 3 elements at 1% FP, tweak 0, update-all; insert
        // three hashes; the serialized filterload payload must equal the published bytes exactly.
        T.Run("reference filter serializes to the exact wire bytes (tweak 0)", () =>
        {
            var f = new BloomFilter(3, 0.01, 0, BloomFilter.UpdateAll);
            T.True(!f.Contains(Convert.FromHexString("99108ad8ed9bb6274d3980bab5a85c048f0950c8")), "empty filter: no match");
            f.Insert(Convert.FromHexString("99108ad8ed9bb6274d3980bab5a85c048f0950c8"));
            T.True(f.Contains(Convert.FromHexString("99108ad8ed9bb6274d3980bab5a85c048f0950c8")), "inserted → matches");
            T.True(!f.Contains(Convert.FromHexString("19108ad8ed9bb6274d3980bab5a85c048f0950c8")), "non-inserted → no match");
            f.Insert(Convert.FromHexString("b5a2c786d9ef4658287ced5914b37a1b4aa32eee"));
            f.Insert(Convert.FromHexString("b9300670b4c5366e95b2699e8b18bc75e5f729c5"));
            T.True(f.Contains(Convert.FromHexString("b5a2c786d9ef4658287ced5914b37a1b4aa32eee")), "2nd inserted → matches");
            T.True(f.Contains(Convert.FromHexString("b9300670b4c5366e95b2699e8b18bc75e5f729c5")), "3rd inserted → matches");
            T.Eq(Hex(f.ToFilterLoad()), "03614e9b050000000000000001", "exact filterload bytes");
        });

        T.Run("reference filter serializes to the exact wire bytes (with tweak)", () =>
        {
            var f = new BloomFilter(3, 0.01, 2147483649, BloomFilter.UpdateAll);
            f.Insert(Convert.FromHexString("99108ad8ed9bb6274d3980bab5a85c048f0950c8"));
            f.Insert(Convert.FromHexString("b5a2c786d9ef4658287ced5914b37a1b4aa32eee"));
            f.Insert(Convert.FromHexString("b9300670b4c5366e95b2699e8b18bc75e5f729c5"));
            T.Eq(Hex(f.ToFilterLoad()), "03ce4299050000000100008001", "exact filterload bytes (tweak)");
        });

        T.Run("never a false negative across many elements", () =>
        {
            var f = new BloomFilter(500, 0.0001, 12345);
            var items = new List<byte[]>();
            for (int i = 0; i < 500; i++) { var b = Hashes.Sha256d(Encoding.ASCII.GetBytes($"item-{i}")); items.Add(b); f.Insert(b); }
            foreach (var b in items) T.True(f.Contains(b), "every inserted element is found");
        });

        T.Run("filter matches the hash160 a P2PKH payment to us pushes (so peers relay it)", () =>
        {
            var me = Secp256k1.GenerateKeyPair();
            var h160 = Hashes.Hash160(me.Pub);
            var f = new BloomFilter(8, 0.00001, 777);
            f.Insert(h160);
            f.Insert(me.Pub);
            T.True(f.Contains(h160), "our hash160 is in the filter");
            // a real P2PKH output script: OP_DUP OP_HASH160 <20-byte push> OP_EQUALVERIFY OP_CHECKSIG.
            var script = Chain.P2pkhLockForPub(me.Pub);
            var pushed = script.AsSpan(3, 20).ToArray();                 // the 20 bytes a peer tests against the filter
            T.Eq(Hex(pushed), Hex(h160), "the script pushes exactly our hash160");
            T.True(f.Contains(pushed), "a peer testing the output's pushed data would match → relays our payment");
        });

        T.Run("a fresh wallet key NOT in the filter does not match", () =>
        {
            var f = new BloomFilter(8, 0.00001, 42);
            f.Insert(Hashes.Hash160(Secp256k1.GenerateKeyPair().Pub));
            var stranger = Hashes.Hash160(Secp256k1.GenerateKeyPair().Pub);
            T.True(!f.Contains(stranger), "an unrelated address is not (spuriously) matched at this FP rate");
        });
    }
}
