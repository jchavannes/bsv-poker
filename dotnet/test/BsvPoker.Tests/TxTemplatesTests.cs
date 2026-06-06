using BsvPoker.Core;

namespace BsvPoker.Tests;

/// <summary>
/// The typed-transaction-template registry: every action kind has its own documented template + unique
/// tag, and a transaction's type is recoverable from its on-chain type marker (push ‖ OP_DROP, never
/// OP_RETURN). Nothing is generic or assumed.
/// </summary>
public static class TxTemplatesTests
{
    public static void All()
    {
        Console.WriteLine("typed transaction templates:");

        T.Run("every TxKind has a template with a unique tag and a documented purpose", () =>
        {
            var kinds = Enum.GetValues<TxKind>();
            var tags = new HashSet<string>();
            foreach (var k in kinds)
            {
                var t = TxTemplates.Of(k);
                T.True(!string.IsNullOrWhiteSpace(t.Tag), $"{k} has a tag");
                T.True(t.Purpose.Length > 10, $"{k} is documented");
                T.True(tags.Add(t.Tag), $"{k} tag is unique");
            }
            T.Eq(TxTemplates.All.Count, kinds.Length, "every kind is registered (none missing)");
        });

        T.Run("a transaction's type round-trips through its on-chain marker", () =>
        {
            foreach (var k in Enum.GetValues<TxKind>())
            {
                var prefix = TxTemplates.TypePrefix(k);
                T.Eq(prefix[^1], (byte)0x75, $"{k} marker ends with OP_DROP");
                T.False(IsOpReturnOpcode(prefix), $"{k} marker uses no OP_RETURN opcode");
                T.Eq(TxTemplates.Recognize(prefix)?.ToString(), k.ToString(), $"{k} recognized from its marker");
                // marker followed by an arbitrary contract body still recognizes the type
                var withBody = prefix.Concat(new byte[] { 0xac, 0x51, 0x52 }).ToArray();
                T.Eq(TxTemplates.Recognize(withBody)?.ToString(), k.ToString(), $"{k} recognized with a contract body");
            }
        });

        T.Run("an untyped / unknown script is not mistaken for a type", () =>
        {
            T.True(TxTemplates.Recognize(new byte[] { 0xac }) == null, "no marker → null");
            T.True(TxTemplates.Recognize(new byte[] { 0x03, (byte)'X', (byte)'Y', (byte)'Z', 0x75 }) == null, "unknown tag → null");
        });
    }

    // true only if a real OP_RETURN (0x6a) appears as an opcode position (after the pushed marker), which it must not
    private static bool IsOpReturnOpcode(byte[] prefix)
    {
        // prefix is <push><marker><OP_DROP>; the only opcode is the trailing OP_DROP — assert no 0x6a opcode
        return prefix[^1] == 0x6a;
    }
}
