using System.Buffers.Binary;

namespace BsvPoker.Core.Games;

/// <summary>
/// D-A/D-B: POINTS are permanently linked to the scorer's IDENTITY and to the GAME. A point award is a typed
/// on-chain record committing (gameId, identityPub, points, handId) — "if Alice scores a point, it is Alice's
/// point", forever, in that game. Awards ride as moves in the n-of-n <see cref="ZeroConfMoveChain"/>, so they
/// are consensus-attributed (every player co-signs) and cannot be forged or moved to another game/identity.
/// </summary>
public static class GamePoints
{
    public sealed record Award(byte[] GameId, byte[] IdentityPub, long Points, byte[] HandId);

    /// <summary>The typed Points output for one award, owned (spendable) by the scorer's identity key. No OP_RETURN.</summary>
    public static byte[] AwardScript(byte[] gameId16, byte[] identityPub33, long points, byte[] handId16)
    {
        if (gameId16.Length != 16) throw new ArgumentException("gameId must be 16 bytes");
        if (identityPub33.Length != 33) throw new ArgumentException("identityPub must be 33-byte compressed");
        if (handId16.Length != 16) throw new ArgumentException("handId must be 16 bytes");
        var pts = new byte[8]; BinaryPrimitives.WriteInt64BigEndian(pts, points);
        return TxTemplates.BuildOutput(TxKind.Points, new[] { gameId16, identityPub33, pts, handId16 }, identityPub33);
    }

    /// <summary>Parse a Points award from a typed output; null if it is not a Points output.</summary>
    public static Award? Parse(byte[] script)
    {
        var p = TxTemplates.Parse(script);
        if (p is not { Kind: TxKind.Points } || p.Fields.Length != 4) return null;
        if (p.Fields[0].Length != 16 || p.Fields[1].Length != 33 || p.Fields[2].Length != 8 || p.Fields[3].Length != 16) return null;
        return new Award(p.Fields[0], p.Fields[1], BinaryPrimitives.ReadInt64BigEndian(p.Fields[2]), p.Fields[3]);
    }

    /// <summary>Build the point award as a zero-conf n-of-n MOVE (every player co-signs), so the attribution is
    /// consensus-enforced and bound into the game's move-chain.</summary>
    public static Chain.Tx BuildAwardMove(ZeroConfMoveChain.Committed pot, IReadOnlyList<(byte[] Priv, byte[] Pub)> players,
        byte[] gameId16, byte[] identityPub33, long points, byte[] handId16, long fee = 1)
    {
        var pubs = players.Select(p => p.Pub).ToList();
        var pts = new byte[8]; BinaryPrimitives.WriteInt64BigEndian(pts, points);
        var move = ZeroConfMoveChain.BuildMove(pot.Txid, pot.Vout, pot.Value, pubs, TxKind.Points, identityPub33,
            new[] { gameId16, identityPub33, pts, handId16 }, fee);
        var sigs = players.Select(p => ZeroConfMoveChain.Sign(move, pubs, pot.Value, p.Priv)).ToList();
        return ZeroConfMoveChain.Apply(move, sigs);
    }

    /// <summary>Tally all of an identity's points IN A GIVEN GAME (awards for another game/identity do not count).</summary>
    public static long Tally(IEnumerable<Award> awards, byte[] gameId16, byte[] identityPub33)
        => awards.Where(a => a.GameId.AsSpan().SequenceEqual(gameId16) && a.IdentityPub.AsSpan().SequenceEqual(identityPub33))
                 .Sum(a => a.Points);
}
