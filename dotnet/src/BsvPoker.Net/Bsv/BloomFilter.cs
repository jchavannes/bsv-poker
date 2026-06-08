using System.Buffers.Binary;

namespace BsvPoker.Net.Bsv;

/// <summary>
/// A Bitcoin connection bloom filter for SPV: the wallet loads its own addresses (the hash160 of each
/// receive/change key, and the full public keys) into a probabilistic set and hands it to its peers with a
/// <c>filterload</c> message. Peers then relay back ONLY the transactions whose pushed data matches the
/// filter — the wallet's own incoming/outgoing transactions — together with <c>merkleblock</c> proofs, with
/// NO server or address-indexer in the path. This is how the wallet discovers a payment sent to its address:
/// the filter matches the P2PKH output (which pushes our hash160), the peer sends us that transaction, and we
/// verify it ourselves against the headers we validated.
///
/// The construction follows the network's connection-bloom-filter rules exactly so real BSV peers accept it:
/// MurmurHash3 (x86, 32-bit) seeded by <c>hashFn*0xFBA4C795 + tweak</c>, bit index modulo the filter's bit
/// length. The false-positive rate is a privacy trade-off, never a correctness one: a match is always
/// re-verified locally (the output must actually pay one of our keys), so a false positive only wastes a
/// little bandwidth and can never credit money that is not ours.
/// </summary>
public sealed class BloomFilter
{
    // network consensus caps on a connection bloom filter
    public const int MaxFilterBytes = 36_000;
    public const int MaxHashFuncs = 50;

    // BLOOM_UPDATE flags: how the peer extends the filter when a match is found.
    public const byte UpdateNone = 0;
    public const byte UpdateAll = 1;        // add every matched output's outpoint so we also see the spend
    public const byte UpdateP2PubKeyOnly = 2;

    private readonly byte[] _data;
    private readonly uint _hashFuncs;
    private readonly uint _tweak;
    private readonly byte _flags;

    public IReadOnlyList<byte> Data => _data;
    public uint HashFuncs => _hashFuncs;
    public uint Tweak => _tweak;
    public byte Flags => _flags;

    /// <param name="elements">number of items we expect to insert (sizes the filter).</param>
    /// <param name="falsePositiveRate">target FP rate, e.g. 0.0001 (privacy vs. bandwidth; correctness is unaffected).</param>
    /// <param name="tweak">a random per-filter salt so different wallets hash differently.</param>
    public BloomFilter(int elements, double falsePositiveRate, uint tweak, byte flags = UpdateAll)
    {
        if (elements < 1) elements = 1;
        if (falsePositiveRate <= 0 || falsePositiveRate >= 1) falsePositiveRate = 0.0001;
        // nFilterBytes = -1/(ln2^2) * N * ln(P) / 8, capped
        int bytes = (int)Math.Min(
            (-1.0 / (Math.Log(2) * Math.Log(2)) * elements * Math.Log(falsePositiveRate)) / 8.0,
            MaxFilterBytes);
        if (bytes < 1) bytes = 1;
        _data = new byte[bytes];
        // nHashFuncs = nFilterBytes*8 / N * ln2, capped
        _hashFuncs = (uint)Math.Min(_data.Length * 8.0 / elements * Math.Log(2), MaxHashFuncs);
        if (_hashFuncs < 1) _hashFuncs = 1;
        _tweak = tweak;
        _flags = flags;
    }

    /// <summary>Reconstruct a filter from wire parameters (used by tests and by a peer-side reference).</summary>
    public BloomFilter(byte[] data, uint hashFuncs, uint tweak, byte flags)
    {
        _data = (byte[])data.Clone(); _hashFuncs = hashFuncs; _tweak = tweak; _flags = flags;
    }

    /// <summary>Insert one element (an address hash160, a public key, a txid, an outpoint, …).</summary>
    public void Insert(ReadOnlySpan<byte> element)
    {
        if (_data.Length == 0) return;
        uint bits = (uint)_data.Length * 8;
        for (uint i = 0; i < _hashFuncs; i++)
        {
            uint h = MurmurHash3(i * 0xFBA4C795u + _tweak, element) % bits;
            _data[h >> 3] |= (byte)(1 << (int)(h & 7));
        }
    }

    /// <summary>True if the element is (probabilistically) present. No false negatives; rare false positives.</summary>
    public bool Contains(ReadOnlySpan<byte> element)
    {
        if (_data.Length == 0) return false;
        uint bits = (uint)_data.Length * 8;
        for (uint i = 0; i < _hashFuncs; i++)
        {
            uint h = MurmurHash3(i * 0xFBA4C795u + _tweak, element) % bits;
            if ((_data[h >> 3] & (byte)(1 << (int)(h & 7))) == 0) return false;
        }
        return true;
    }

    /// <summary>Serialize as a <c>filterload</c> payload: varint(len)+data + uint32 nHashFuncs + uint32 nTweak + 1 flag byte.</summary>
    public byte[] ToFilterLoad()
    {
        var b = new List<byte>();
        BsvVersion.WriteVarInt(b, (ulong)_data.Length);
        b.AddRange(_data);
        var hf = new byte[4]; BinaryPrimitives.WriteUInt32LittleEndian(hf, _hashFuncs); b.AddRange(hf);
        var tw = new byte[4]; BinaryPrimitives.WriteUInt32LittleEndian(tw, _tweak); b.AddRange(tw);
        b.Add(_flags);
        return b.ToArray();
    }

    /// <summary>MurmurHash3 (x86, 32-bit) — exactly the variant Bitcoin connection bloom filters use.</summary>
    public static uint MurmurHash3(uint seed, ReadOnlySpan<byte> data)
    {
        const uint c1 = 0xcc9e2d51, c2 = 0x1b873593;
        uint h1 = seed;
        int len = data.Length;
        int blocks = len / 4;
        for (int i = 0; i < blocks; i++)
        {
            uint k1 = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(i * 4, 4));
            k1 *= c1; k1 = RotL(k1, 15); k1 *= c2;
            h1 ^= k1; h1 = RotL(h1, 13); h1 = h1 * 5 + 0xe6546b64;
        }
        uint t = 0;
        int tail = blocks * 4;
        switch (len & 3)
        {
            case 3: t ^= (uint)data[tail + 2] << 16; goto case 2;
            case 2: t ^= (uint)data[tail + 1] << 8; goto case 1;
            case 1: t ^= data[tail]; t *= c1; t = RotL(t, 15); t *= c2; h1 ^= t; break;
        }
        h1 ^= (uint)len;
        h1 ^= h1 >> 16; h1 *= 0x85ebca6b; h1 ^= h1 >> 13; h1 *= 0xc2b2ae35; h1 ^= h1 >> 16;
        return h1;
    }

    private static uint RotL(uint x, int r) => (x << r) | (x >> (32 - r));
}
