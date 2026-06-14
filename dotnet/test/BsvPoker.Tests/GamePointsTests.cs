using BsvPoker.Core;
using BsvPoker.Core.Games;
using BsvPoker.Crypto;

namespace BsvPoker.Tests;

/// <summary>
/// D-A/D-B: a scored point is permanently linked to the scorer's IDENTITY and to the GAME. Awards round-trip,
/// tally per (identity, game), do not leak across games or identities, and are consensus-attributed when
/// carried as an n-of-n move (every player co-signs — a point cannot be forged or reassigned).
/// </summary>
public static class GamePointsTests
{
    public static void All()
    {
        Console.WriteLine("game points linked to identity + game (D-A/D-B):");
        var alice = Secp256k1.GenerateKeyPair();
        var bob = Secp256k1.GenerateKeyPair();
        var carol = Secp256k1.GenerateKeyPair();
        var gameA = new byte[16]; gameA[0] = 0xA1;
        var gameB = new byte[16]; gameB[0] = 0xB2;
        var hand1 = new byte[16]; hand1[0] = 1;

        T.Run("a point award round-trips and is bound to the scorer's identity + the game", () =>
        {
            var s = GamePoints.AwardScript(gameA, alice.Pub, 3, hand1);
            var a = GamePoints.Parse(s);
            T.True(a != null, "parses as a Points award");
            T.True(a!.GameId.AsSpan().SequenceEqual(gameA), "game bound");
            T.True(a.IdentityPub.AsSpan().SequenceEqual(alice.Pub), "identity bound");
            T.Eq(a.Points, 3L, "points recovered");
            T.Eq(s[^1], (byte)0xac, "owned by the identity (OP_CHECKSIG)");
            T.True(s[0] != 0x6a, "no OP_RETURN");
        });

        T.Run("tally is per (identity, game): Alice's points in game A do not count in game B or for Bob", () =>
        {
            var awards = new[]
            {
                GamePoints.Parse(GamePoints.AwardScript(gameA, alice.Pub, 2, hand1))!,
                GamePoints.Parse(GamePoints.AwardScript(gameA, alice.Pub, 3, hand1))!,
                GamePoints.Parse(GamePoints.AwardScript(gameA, bob.Pub, 5, hand1))!,
                GamePoints.Parse(GamePoints.AwardScript(gameB, alice.Pub, 9, hand1))!,
            };
            T.Eq(GamePoints.Tally(awards, gameA, alice.Pub), 5L, "Alice scored 5 in game A");
            T.Eq(GamePoints.Tally(awards, gameA, bob.Pub), 5L, "Bob scored 5 in game A");
            T.Eq(GamePoints.Tally(awards, gameB, alice.Pub), 9L, "Alice's game-B points are separate");
            T.Eq(GamePoints.Tally(awards, gameA, carol.Pub), 0L, "Carol scored nothing");
        });

        T.Run("an award carried as an n-of-n move is consensus-attributed (every player co-signs)", () =>
        {
            var players = new[] { (alice.Priv, alice.Pub), (bob.Priv, bob.Pub), (carol.Priv, carol.Pub) };
            var pubs = players.Select(p => p.Item2).ToList();
            var pot = new ZeroConfMoveChain.Committed("cc".PadRight(64, '0'), 0, 1000, pubs);
            var move = GamePoints.BuildAwardMove(pot, players, gameA, alice.Pub, 1, hand1);
            T.True(ZeroConfMoveChain.Verify(move, pubs, pot.Value), "the point award verifies as an all-n move (cannot be forged by a subset)");
            var award = GamePoints.Parse(move.Outs[1].Script);
            T.True(award != null && award.IdentityPub.AsSpan().SequenceEqual(alice.Pub), "the move carries Alice's award");
        });
    }
}
