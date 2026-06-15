using BsvPoker.Core;
using BsvPoker.Core.Games;
using BsvPoker.Crypto;

namespace BsvPoker.Tests;

/// <summary>
/// The multiplayer Blackjack POT: an n-of-n locked output funded by all players (3 players ⇒ 3-of-3), settled
/// by the hand result with the remaining bank split among the players. Proven: the settlement conserves the
/// pot to the satoshi, verifies only when EVERY player signed, and a single missing/forged signature (an
/// (n-1)-collusion) is rejected — no subset of players can move the money.
/// </summary>
public static class BlackjackPotTests
{
    private static Card C(int rank) => new Card(rank, Suit.Clubs);
    private static List<Card> Deck(params int[] r) => r.Select(C).ToList();

    public static void All()
    {
        Console.WriteLine("multiplayer Blackjack pot (n-of-n funded, result-distributed, collusion-safe):");

        T.Run("3-of-3 pot: settle a hand, distribute by result + remaining bank, conserve the pot", () =>
        {
            var ks = Enumerable.Range(0, 3).Select(_ => Secp256k1.GenerateKeyPair()).ToArray();
            var pubs = ks.Select(k => k.Pub).ToList();
            var seeds = ks.Select(k => k.Priv).ToList();

            // p0=[T,T]=20 (win), p1=[5,6]=11 (lose), p2=[9,9]=18 (win); dealer=[T,7]=17
            var g = GroupBlackjack.Create(new long[] { 10, 10, 10 }, Deck(10, 5, 9, 10, 10, 6, 9, 7));
            g.Act(0, BjAction.Stand); g.Act(1, BjAction.Stand); g.Act(2, BjAction.Stand);
            T.True(g.Complete, "the hand completed");

            long dealerBank = 30, pot = dealerBank + 30;
            var final = BlackjackPot.Settle(g, dealerBank);
            T.Eq(final.Sum(), pot, "the distributed amounts sum EXACTLY to the pot (bank + all bets)");
            T.True(final[0] > final[1] && final[2] > final[1], "winners receive more than the loser");

            var unsigned = BlackjackPot.BuildSettlement("ab".PadRight(64, '0'), 0, pot, pubs, final, fee: 0);
            var signed = BlackjackPot.CoSign(unsigned, pubs, pot, seeds);
            T.True(Chain.VerifyMultisigNofN(signed, 0, pubs, pot), "the settlement verifies when ALL THREE players co-signed");
            // value conserved on-chain: outputs sum to the pot (fee 0)
            T.Eq(signed.Outs.Sum(o => o.Value), pot, "the settlement outputs conserve the pot");
        });

        T.Run("(n-1)-collusion FAILS: a settlement missing one player's real signature is rejected", () =>
        {
            var ks = Enumerable.Range(0, 3).Select(_ => Secp256k1.GenerateKeyPair()).ToArray();
            var pubs = ks.Select(k => k.Pub).ToList();
            var attacker = Secp256k1.GenerateKeyPair();

            var g = GroupBlackjack.Create(new long[] { 10, 10, 10 }, Deck(10, 5, 9, 10, 10, 6, 9, 7));
            g.Act(0, BjAction.Stand); g.Act(1, BjAction.Stand); g.Act(2, BjAction.Stand);
            long pot = 30 + 30;
            var final = BlackjackPot.Settle(g, 30);
            var unsigned = BlackjackPot.BuildSettlement("cd".PadRight(64, '0'), 0, pot, pubs, final, 0);

            // players 0 and 1 sign honestly; player 2's slot is signed by an ATTACKER key (collusion to cut p2 out)
            var seeds = new List<byte[]> { ks[0].Priv, ks[1].Priv, attacker.Priv };
            var forged = BlackjackPot.CoSign(unsigned, pubs, pot, seeds);
            T.False(Chain.VerifyMultisigNofN(forged, 0, pubs, pot), "a settlement without player 2's REAL signature is rejected (no n-1 theft)");
        });
    }
}
