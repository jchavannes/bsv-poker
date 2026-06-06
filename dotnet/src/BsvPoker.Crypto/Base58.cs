using System.Numerics;
using System.Text;

namespace BsvPoker.Crypto;

/// <summary>Base58 + Base58Check (the encoding for BSV addresses and WIF private keys).</summary>
public static class Base58
{
    private const string Alphabet = "123456789ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz";

    public static string Encode(ReadOnlySpan<byte> data)
    {
        int zeros = 0;
        while (zeros < data.Length && data[zeros] == 0) zeros++;
        var num = new BigInteger(data, isUnsigned: true, isBigEndian: true);
        var sb = new StringBuilder();
        while (num > 0)
        {
            num = BigInteger.DivRem(num, 58, out var rem);
            sb.Insert(0, Alphabet[(int)rem]);
        }
        for (int i = 0; i < zeros; i++) sb.Insert(0, '1');
        return sb.ToString();
    }

    public static byte[] Decode(string s)
    {
        BigInteger num = 0;
        foreach (var c in s)
        {
            int idx = Alphabet.IndexOf(c);
            if (idx < 0) throw new FormatException($"invalid base58 char '{c}'");
            num = num * 58 + idx;
        }
        var body = num.IsZero ? Array.Empty<byte>() : num.ToByteArray(isUnsigned: true, isBigEndian: true);
        int zeros = 0;
        while (zeros < s.Length && s[zeros] == '1') zeros++;
        var outb = new byte[zeros + body.Length];
        body.CopyTo(outb, zeros);
        return outb;
    }

    /// <summary>Base58Check = Base58(payload ‖ first4(SHA256d(payload))).</summary>
    public static string CheckEncode(ReadOnlySpan<byte> payload)
    {
        var chk = Hashes.Sha256d(payload);
        var buf = new byte[payload.Length + 4];
        payload.CopyTo(buf);
        Array.Copy(chk, 0, buf, payload.Length, 4);
        return Encode(buf);
    }

    /// <summary>Decode + verify the 4-byte checksum; throws on a corrupt string. Returns the payload.</summary>
    public static byte[] CheckDecode(string s)
    {
        var raw = Decode(s);
        if (raw.Length < 5) throw new FormatException("base58check too short");
        var payload = raw[..^4];
        var chk = Hashes.Sha256d(payload);
        for (int i = 0; i < 4; i++) if (raw[raw.Length - 4 + i] != chk[i]) throw new FormatException("bad base58check checksum");
        return payload;
    }
}
