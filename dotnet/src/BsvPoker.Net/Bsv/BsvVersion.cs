using System.Buffers.Binary;
using System.Text;

namespace BsvPoker.Net.Bsv;

/// <summary>Build and parse the P2P <c>version</c> message payload (the first thing peers exchange).</summary>
public static class BsvVersion
{
    public const int ProtocolVersion = 70016;
    public const string UserAgent = "/BsvPoker:0.1/";

    private static void WriteVarStr(List<byte> b, string s)
    {
        var bytes = Encoding.ASCII.GetBytes(s);
        WriteVarInt(b, (ulong)bytes.Length);
        b.AddRange(bytes);
    }

    public static void WriteVarInt(List<byte> b, ulong n)
    {
        if (n < 0xfd) b.Add((byte)n);
        else if (n <= 0xffff) { b.Add(0xfd); var t = new byte[2]; BinaryPrimitives.WriteUInt16LittleEndian(t, (ushort)n); b.AddRange(t); }
        else if (n <= 0xffffffff) { b.Add(0xfe); var t = new byte[4]; BinaryPrimitives.WriteUInt32LittleEndian(t, (uint)n); b.AddRange(t); }
        else { b.Add(0xff); var t = new byte[8]; BinaryPrimitives.WriteUInt64LittleEndian(t, n); b.AddRange(t); }
    }

    private static void WriteNetAddr(List<byte> b, ulong services)
    {
        var s = new byte[8]; BinaryPrimitives.WriteUInt64LittleEndian(s, services); b.AddRange(s); // services
        b.AddRange(new byte[16]); // IPv6/IPv4-mapped address — zeros (we do not advertise a routable addr here)
        b.AddRange(new byte[2]);  // port (BE) — zero
    }

    public static byte[] Build(int startHeight, ulong nonce, ulong services = 0, bool relay = true)
    {
        var b = new List<byte>(96);
        var i32 = new byte[4]; BinaryPrimitives.WriteInt32LittleEndian(i32, ProtocolVersion); b.AddRange(i32);
        var u64 = new byte[8]; BinaryPrimitives.WriteUInt64LittleEndian(u64, services); b.AddRange(u64);
        var ts = new byte[8]; BinaryPrimitives.WriteInt64LittleEndian(ts, DateTimeOffset.UtcNow.ToUnixTimeSeconds()); b.AddRange(ts);
        WriteNetAddr(b, services); // addr_recv
        WriteNetAddr(b, services); // addr_from
        var nb = new byte[8]; BinaryPrimitives.WriteUInt64LittleEndian(nb, nonce); b.AddRange(nb);
        WriteVarStr(b, UserAgent);
        var sh = new byte[4]; BinaryPrimitives.WriteInt32LittleEndian(sh, startHeight); b.AddRange(sh);
        b.Add((byte)(relay ? 1 : 0));
        return b.ToArray();
    }

    public sealed record Info(int Version, ulong Services, string UserAgent, int StartHeight, ulong Nonce);

    /// <summary>Best-effort parse of a version payload; throws on a truncated/invalid payload.</summary>
    public static Info Parse(ReadOnlySpan<byte> p)
    {
        int o = 0;
        int version = BinaryPrimitives.ReadInt32LittleEndian(p.Slice(o, 4)); o += 4;
        ulong services = BinaryPrimitives.ReadUInt64LittleEndian(p.Slice(o, 8)); o += 8;
        o += 8;            // timestamp
        o += 26 + 26;      // addr_recv + addr_from
        ulong nonce = BinaryPrimitives.ReadUInt64LittleEndian(p.Slice(o, 8)); o += 8;
        ulong uaLen = ReadVarInt(p, ref o);
        if (uaLen > 256 || o + (int)uaLen > p.Length) throw new FormatException("bad user-agent length");
        var ua = Encoding.ASCII.GetString(p.Slice(o, (int)uaLen)); o += (int)uaLen;
        int startHeight = o + 4 <= p.Length ? BinaryPrimitives.ReadInt32LittleEndian(p.Slice(o, 4)) : 0;
        return new Info(version, services, ua, startHeight, nonce);
    }

    public static ulong ReadVarInt(ReadOnlySpan<byte> p, ref int o)
    {
        byte first = p[o++];
        if (first < 0xfd) return first;
        if (first == 0xfd) { var v = BinaryPrimitives.ReadUInt16LittleEndian(p.Slice(o, 2)); o += 2; return v; }
        if (first == 0xfe) { var v = BinaryPrimitives.ReadUInt32LittleEndian(p.Slice(o, 4)); o += 4; return v; }
        { var v = BinaryPrimitives.ReadUInt64LittleEndian(p.Slice(o, 8)); o += 8; return v; }
    }
}
