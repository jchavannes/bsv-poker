using System.Text;
using BsvPoker.Core;
using BsvPoker.Crypto;

namespace BsvPoker.Tests;

/// <summary>
/// The typed-transaction-template registry (thorough): every action kind has its own documented template
/// with a unique tag AND a named field schema, and a typed output round-trips kind + every field + owner.
/// No OP_RETURN; nothing generic or assumed.
/// </summary>
public static class TxTemplatesTests
{
    public static void All()
    {
        Console.WriteLine("typed transaction templates:");
        var owner = Secp256k1.GenerateKeyPair().Pub;

        T.Run("every TxKind has a unique tag, a documented purpose, and a named field schema", () =>
        {
            var kinds = Enum.GetValues<TxKind>();
            var tags = new HashSet<string>();
            foreach (var k in kinds)
            {
                var t = TxTemplates.Of(k);
                T.True(tags.Add(t.Tag), $"{k} tag unique");
                T.True(t.Purpose.Length > 10, $"{k} documented purpose");
                T.True(t.Fields.Length >= 1 && t.Fields.All(f => !string.IsNullOrWhiteSpace(f)), $"{k} has named fields");
            }
            T.Eq(TxTemplates.All.Count, kinds.Length, "every kind registered (none missing)");
        });

        T.Run("every type's typed output round-trips kind + all fields + owner (no OP_RETURN)", () =>
        {
            foreach (var k in Enum.GetValues<TxKind>())
            {
                var schema = TxTemplates.Of(k).Fields;
                var fields = schema.Select((name, i) => Encoding.ASCII.GetBytes($"{name}#{i}:{k}")).ToArray();
                var script = TxTemplates.BuildOutput(k, fields, owner);
                var parsed = TxTemplates.Parse(script);
                T.True(parsed != null, $"{k} parses");
                T.Eq(parsed!.Kind.ToString(), k.ToString(), $"{k} kind round-trips");
                T.Eq(parsed.Fields.Length, schema.Length, $"{k} field count");
                for (int i = 0; i < schema.Length; i++) T.Eq(T.Hex(parsed.Fields[i]), T.Hex(fields[i]), $"{k} field {schema[i]}");
                T.Eq(T.Hex(parsed.OwnerPub), T.Hex(owner), $"{k} owner round-trips");
                T.Eq(script[^1], (byte)0xac, $"{k} ends in OP_CHECKSIG (owner spends)");
            }
        });

        T.Run("wrong field count is rejected (the schema is enforced, no assumptions)", () =>
        {
            T.Throws(() => TxTemplates.BuildOutput(TxKind.ChatDirect, new[] { new byte[] { 1 } }, owner), "DM needs 3 fields");
        });

        T.Run("an untyped / unknown script is not mistaken for a typed transaction", () =>
        {
            T.True(TxTemplates.Parse(new byte[] { 0xac }) == null, "no marker → null");
            T.True(TxTemplates.Parse(new byte[] { 0x03, (byte)'X', (byte)'Y', (byte)'Z', 0x75, 0xac }) == null, "unknown tag → null");
        });
    }
}
