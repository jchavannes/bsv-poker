using BsvPoker.Core;
using BsvPoker.Core.Games;
using BsvPoker.Crypto;

namespace BsvPoker.Tests;

/// <summary>
/// The full composed hand: every security primitive engaged together (anti-grinding seat order, two-party
/// escrow, dealerless deal with verifiable shuffle proofs, per-card privacy, co-signed settlement) — a
/// complete verified heads-up hand with chips conserved.
/// </summary>
public static class VerifiedHandTests
{
    public static void All()
    {
        Console.WriteLine("verified hand (all security primitives composed):");

        T.Run("a complete heads-up hand: escrow + proven deal + private holes + co-signed settlement", () =>
        {
            var a = Secp256k1.GenerateKeyPair(); var b = Secp256k1.GenerateKeyPair();
            var aUtxo = new OnChainWallet.Utxo("aa".PadRight(64, '1'), 0, 1_000_000, 0, 0);
            var bUtxo = new OnChainWallet.Utxo("bb".PadRight(64, '2'), 1, 1_000_000, 0, 1);
            long stake = 300_000, fee = 2000, settleFee = 1000;

            var r = VerifiedHand.PlayHeadsUp((a.Priv, a.Pub), aUtxo, (b.Priv, b.Pub), bUtxo, stake, fee, settleFee);

            T.Eq(r.Pot, 600_000L, "pot = both stakes");
            T.True(r.ProofsVerified, "all shuffle/remask proofs verify (no cheating)");
            // two distinct hole cards each, a 5-card board, every card distinct (a real deal)
            var all = r.HolesA.Concat(r.HolesB).Concat(r.Board).Select(c => c.Index).ToList();
            T.Eq(all.Count, 9, "2+2+5 cards dealt");
            T.Eq(all.Distinct().Count(), 9, "all dealt cards are distinct (valid shuffle)");
            // escrow is a real two-party 2-of-2 spend; settlement is a valid co-signed spend; value conserved
            T.True(TwoPartyEscrow.Verify(r.EscrowTx, a.Pub, aUtxo.Value, b.Pub, bUtxo.Value, fee), "two-party escrow valid");
            T.True(Chain.VerifyMultisig2of2(r.Settlement, 0, a.Pub, b.Pub, r.Pot), "settlement co-signed + valid");
            T.Eq(r.Settlement.Outs.Sum(o => o.Value), r.Pot - settleFee, "pot - fee paid out (chips conserved)");
        });

        T.Run("running many hands always produces valid distinct deals and verified proofs", () =>
        {
            for (int t = 0; t < 10; t++)
            {
                var a = Secp256k1.GenerateKeyPair(); var b = Secp256k1.GenerateKeyPair();
                var aUtxo = new OnChainWallet.Utxo("aa".PadRight(64, '1'), 0, 1_000_000, 0, 0);
                var bUtxo = new OnChainWallet.Utxo("bb".PadRight(64, '2'), 1, 1_000_000, 0, 1);
                var r = VerifiedHand.PlayHeadsUp((a.Priv, a.Pub), aUtxo, (b.Priv, b.Pub), bUtxo, 200_000);
                T.True(r.ProofsVerified, $"hand {t}: proofs verify");
                var all = r.HolesA.Concat(r.HolesB).Concat(r.Board).Select(c => c.Index).ToList();
                T.Eq(all.Distinct().Count(), 9, $"hand {t}: 9 distinct cards");
                T.True(r.WinnerSeat is 0 or 1, $"hand {t}: a winner (or split)");
            }
        });
    }
}
