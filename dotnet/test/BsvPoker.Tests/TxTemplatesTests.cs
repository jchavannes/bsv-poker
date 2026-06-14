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

        T.Run("typed outputs use MINIMAL pushes (consensus MINIMALDATA) and still round-trip", () =>
        {
            // single bytes 1..16 must encode as OP_1..OP_16, empty as OP_0, 0x81 as OP_1NEGATE — else the
            // network rejects the spend with "Data push larger than necessary" (proven on the regtest node).
            var fields = new byte[][] { new byte[] { 2 }, Array.Empty<byte>(), new byte[] { 0x81 }, new byte[] { 16 } };
            var script = TxTemplates.BuildOutput(TxKind.TableGenesis, fields, owner);
            T.True(Array.IndexOf(script, (byte)0x52) >= 0, "single byte 2 → OP_2 (0x52), not a length-prefixed push");
            T.True(Array.IndexOf(script, (byte)0x60) >= 0, "single byte 16 → OP_16 (0x60)");
            T.True(Array.IndexOf(script, (byte)0x4f) >= 0, "0x81 → OP_1NEGATE (0x4f)");
            // no length-prefixed 1-byte push of a small value (0x01 0x02) appears in the MARKER+FIELDS region the
            // builder controls. The trailing 35 bytes are the owner push (0x21 <33-byte pubkey>) + OP_CHECKSIG: a
            // RANDOM pubkey can legitimately contain the bytes 0x01 followed by a 1..16 value, which is data, not a
            // non-minimal push — so the scan must exclude that region (otherwise this test flakes on ~1 run in N).
            int dataEnd = script.Length - 35;   // exclude <0x21><33-byte ownerPub><OP_CHECKSIG>
            for (int i = 0; i + 1 < dataEnd; i++)
                T.False(script[i] == 0x01 && script[i + 1] >= 1 && script[i + 1] <= 16, "no non-minimal 1-byte push of 1..16");
            var parsed = TxTemplates.Parse(script);
            T.True(parsed != null && parsed.Fields.Length == 4, "still parses");
            T.Eq(T.Hex(parsed!.Fields[0]), "02", "OP_2 decodes to [0x02]");
            T.Eq(parsed.Fields[1].Length, 0, "OP_0 decodes to empty");
            T.Eq(T.Hex(parsed.Fields[2]), "81", "OP_1NEGATE decodes to [0x81]");
            T.Eq(T.Hex(parsed.Fields[3]), "10", "OP_16 decodes to [0x10]");
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
