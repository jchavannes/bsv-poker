using System.Buffers.Binary;
using System.Text;
using BsvPoker.Crypto;

namespace BsvPoker.Net.Bsv;

/// <summary>
/// A Bitcoin P2P wire message and its codec. Envelope (24-byte header + payload):
///   magic(4) ‖ command(12, ASCII, null-padded) ‖ length(4 LE) ‖ checksum(4 = SHA-256d(payload)[..4]) ‖ payload
/// Strict, bounds-checked parsing: a bad magic, an over-long payload, or a wrong checksum is rejected.
/// </summary>
public sealed record BsvMessage(string Command, byte[] Payload)
{
    public const int HeaderSize = 24;
    public const int MaxPayload = 32 * 1024 * 1024; // 32 MiB ceiling for a single framed message

    /// <summary>Serialize this message with the given network magic.</summary>
    public byte[] Encode(byte[] magic)
    {
        if (magic.Length != 4) throw new ArgumentException("magic must be 4 bytes");
        var cmd = Encoding.ASCII.GetBytes(Command);
        if (cmd.Length > 12) throw new ArgumentException("command too long");
        var buf = new byte[HeaderSize + Payload.Length];
        magic.CopyTo(buf, 0);
        Array.Copy(cmd, 0, buf, 4, cmd.Length);                 // remaining command bytes stay zero (null pad)
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(16, 4), (uint)Payload.Length);
        var checksum = Hashes.Sha256d(Payload);
        Array.Copy(checksum, 0, buf, 20, 4);
        Payload.CopyTo(buf, HeaderSize);
        return buf;
    }

    public enum DecodeStatus { Ok, NeedMore, BadMagic, TooLarge, BadChecksum }

    /// <summary>
    /// Try to decode one message from the front of <paramref name="buffer"/> for the given network magic.
    /// On Ok, <paramref name="consumed"/> is the number of bytes used. NeedMore means wait for more bytes.
    /// </summary>
    public static DecodeStatus TryDecode(ReadOnlySpan<byte> buffer, byte[] magic, out BsvMessage? message, out int consumed)
    {
        message = null; consumed = 0;
        if (buffer.Length < HeaderSize) return DecodeStatus.NeedMore;
        if (!buffer[..4].SequenceEqual(magic)) return DecodeStatus.BadMagic;
        int len = (int)BinaryPrimitives.ReadUInt32LittleEndian(buffer.Slice(16, 4));
        if (len < 0 || len > MaxPayload) return DecodeStatus.TooLarge;
        if (buffer.Length < HeaderSize + len) return DecodeStatus.NeedMore;
        var payload = buffer.Slice(HeaderSize, len).ToArray();
        var expect = Hashes.Sha256d(payload);
        if (!buffer.Slice(20, 4).SequenceEqual(expect.AsSpan(0, 4))) return DecodeStatus.BadChecksum;
        // command = ASCII up to the first NUL
        int cmdLen = 0; while (cmdLen < 12 && buffer[4 + cmdLen] != 0) cmdLen++;
        var command = Encoding.ASCII.GetString(buffer.Slice(4, cmdLen));
        message = new BsvMessage(command, payload);
        consumed = HeaderSize + len;
        return DecodeStatus.Ok;
    }
}
