using BsvPoker.Core;
using BsvPoker.Crypto;

namespace BsvPoker.Tests;

/// <summary>
/// Proves the commutative-encryption mental-poker deal: the deck shuffles correctly AND a player's card
/// is private to them (a non-recipient who strips only the masks they know cannot identify the card),
/// while a board card both can strip is public. All secp256k1, simulated end to end.
/// </summary>
public static class MentalPokerECTests
{
    // a deterministic-ish permutation of [0..n) for the test (derived from a byte seed, no bias needed here)
    private static int[] Perm(int n, byte s)
    {
        var p = Enumerable.Range(0, n).ToArray();
        var rng = new Random(s);
        for (int i = n - 1; i > 0; i--) { int j = rng.Next(i + 1); (p[i], p[j]) = (p[j], p[i]); }
        return p;
    }

    public static void All()
    {
        Console.WriteLine("commutative-encryption mental poker (true per-card privacy, secp256k1):");

        T.Run("curve masks commute and invert: a·(b·G)=b·(a·G); s⁻¹·(s·P)=P", () =>
        {
            var g = Secp256k1.CardBasePoint(0);
            var a = MentalPokerEC.NewScalar();
            var b = MentalPokerEC.NewScalar();
            var ab = Secp256k1.PointMul(Secp256k1.PointMul(g, a), b);
            var ba = Secp256k1.PointMul(Secp256k1.PointMul(g, b), a);
            T.Eq(T.Hex(ab), T.Hex(ba), "masks commute");
            var stripped = Secp256k1.PointMul(Secp256k1.PointMul(g, a), Secp256k1.ScalarInverse(a));
            T.Eq(T.Hex(stripped), T.Hex(g), "a mask is removed by its inverse");
        });

        T.Run("full 2-player deal: every position decodes to a DISTINCT valid card (a real shuffle)", () =>
        {
            const int n = 52;
            var cA = MentalPokerEC.NewScalar(); var cB = MentalPokerEC.NewScalar();
            var dA = MentalPokerEC.NewPerCardScalars(n); var dB = MentalPokerEC.NewPerCardScalars(n);

            var deck = MentalPokerEC.BaseDeck(n);
            deck = MentalPokerEC.ShuffleMask(deck, cA, Perm(n, 1));   // A shuffles + masks
            deck = MentalPokerEC.ShuffleMask(deck, cB, Perm(n, 2));   // B shuffles + masks
            deck = MentalPokerEC.Remask(deck, cA, dA);               // A swaps global→per-card
            deck = MentalPokerEC.Remask(deck, cB, dB);               // B swaps global→per-card

            // reveal ALL d's (as at a full showdown) and decode every position
            var seen = new HashSet<int>();
            for (int k = 0; k < n; k++)
            {
                var m = MentalPokerEC.Unmask(deck[k], new[] { dA[k], dB[k] });
                int card = MentalPokerEC.Identify(m, n);
                T.True(card >= 0, $"position {k} decodes to a real card");
                T.True(seen.Add(card), $"card {card} appears exactly once");
            }
            T.Eq(seen.Count, n, "all 52 distinct cards present — a valid permutation");
        });

        T.Run("PRIVACY: a non-recipient who strips only their own mask cannot identify the card", () =>
        {
            const int n = 52;
            var cA = MentalPokerEC.NewScalar(); var cB = MentalPokerEC.NewScalar();
            var dA = MentalPokerEC.NewPerCardScalars(n); var dB = MentalPokerEC.NewPerCardScalars(n);
            var deck = MentalPokerEC.BaseDeck(n);
            deck = MentalPokerEC.ShuffleMask(deck, cA, Perm(n, 3));
            deck = MentalPokerEC.ShuffleMask(deck, cB, Perm(n, 4));
            deck = MentalPokerEC.Remask(deck, cA, dA);
            deck = MentalPokerEC.Remask(deck, cB, dB);

            int k = 0; // deal position 0 to player A: B privately gives A its dB[0]
            // A (recipient) strips B's and its own mask → recovers the real card
            var aView = MentalPokerEC.Unmask(deck[k], new[] { dB[k], dA[k] });
            T.True(MentalPokerEC.Identify(aView, n) >= 0, "recipient A reads the card");
            // B (non-recipient) has dB[0] but NOT dA[0] — strips only its own mask
            var bView = MentalPokerEC.Unmask(deck[k], new[] { dB[k] });
            T.Eq(MentalPokerEC.Identify(bView, n), -1, "non-recipient B cannot identify A's hole card");
            // and an eavesdropper with neither secret certainly cannot
            T.Eq(MentalPokerEC.Identify(deck[k], n), -1, "the masked card matches no base point");
        });

        T.Run("MULTIWAY (3 players): privacy and correctness hold for N>2", () =>
        {
            const int n = 52, players = 3;
            var c = new byte[players][]; var d = new byte[players][][];
            for (int pl = 0; pl < players; pl++) { c[pl] = MentalPokerEC.NewScalar(); d[pl] = MentalPokerEC.NewPerCardScalars(n); }

            // shuffle phase: each player masks+shuffles in turn; then remask phase: each swaps global→per-card
            var deck = MentalPokerEC.BaseDeck(n);
            for (int pl = 0; pl < players; pl++) deck = MentalPokerEC.ShuffleMask(deck, c[pl], Perm(n, (byte)(10 + pl)));
            for (int pl = 0; pl < players; pl++) deck = MentalPokerEC.Remask(deck, c[pl], d[pl]);
            // now deck[k] = (∏_pl d[pl][k]) · M_{σ(k)}

            // deal position 0 to player 0: the OTHER players reveal their masks at position 0
            int k = 0;
            var othersMasks = Enumerable.Range(1, players - 1).Select(pl => d[pl][k]).Append(d[0][k]); // all masks incl recipient's own
            var p0view = MentalPokerEC.Unmask(deck[k], othersMasks);
            T.True(MentalPokerEC.Identify(p0view, n) >= 0, "recipient (player 0) reads the card");

            // a NON-recipient (player 1) has every mask EXCEPT player 0's → cannot identify the card
            var nonRecip = Enumerable.Range(1, players - 1).Select(pl => d[pl][k]); // players 1..2 only (no d[0])
            var p1view = MentalPokerEC.Unmask(deck[k], nonRecip);
            T.Eq(MentalPokerEC.Identify(p1view, n), -1, "a non-recipient cannot identify another player's hole card");

            // dealing all positions with ALL masks revealed yields a valid 52-card permutation
            var seen = new HashSet<int>();
            for (int pos = 0; pos < n; pos++)
            {
                var all = Enumerable.Range(0, players).Select(pl => d[pl][pos]);
                var m = MentalPokerEC.Unmask(deck[pos], all);
                T.True(seen.Add(MentalPokerEC.Identify(m, n)), $"position {pos} is a distinct card");
            }
            T.Eq(seen.Count, n, "all 52 distinct cards present for the 3-player deal");
        });

        T.Run("BOARD card: when ALL players reveal their per-card scalar, everyone reads the same card", () =>
        {
            const int n = 52;
            var cA = MentalPokerEC.NewScalar(); var cB = MentalPokerEC.NewScalar();
            var dA = MentalPokerEC.NewPerCardScalars(n); var dB = MentalPokerEC.NewPerCardScalars(n);
            var deck = MentalPokerEC.BaseDeck(n);
            deck = MentalPokerEC.ShuffleMask(deck, cA, Perm(n, 5));
            deck = MentalPokerEC.ShuffleMask(deck, cB, Perm(n, 6));
            deck = MentalPokerEC.Remask(deck, cA, dA);
            deck = MentalPokerEC.Remask(deck, cB, dB);

            int k = 7;
            var fromA = MentalPokerEC.Unmask(deck[k], new[] { dA[k], dB[k] });
            var fromB = MentalPokerEC.Unmask(deck[k], new[] { dB[k], dA[k] }); // order-independent (commutes)
            T.Eq(T.Hex(fromA), T.Hex(fromB), "both players recover the identical board point");
            T.True(MentalPokerEC.Identify(fromA, n) >= 0, "and it is a real card");
        });
    }
}
