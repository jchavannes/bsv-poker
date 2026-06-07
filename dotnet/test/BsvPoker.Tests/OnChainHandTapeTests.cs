using BsvPoker.Core;
using BsvPoker.Core.Games;
using BsvPoker.Crypto;

namespace BsvPoker.Tests;

/// <summary>
/// A whole hand as a SEQUENCE of on-chain transactions ("maximize transactions"): the tape emits a typed
/// transaction for every protocol step plus the real pot escrow and settlement, all funded from one wallet,
/// every tx distinct and consensus-shaped, the typed steps parse back to their kind+owner, and the pot
/// settles to the real winner.
/// </summary>
public static class OnChainHandTapeTests
{
    private static Card C(string s)
    {
        int rank = s[0] switch { 'A' => 14, 'K' => 13, 'Q' => 12, 'J' => 11, 'T' => 10, _ => s[0] - '0' };
        var suit = s[1] switch { 's' => Suit.Spades, 'h' => Suit.Hearts, 'd' => Suit.Diamonds, _ => Suit.Clubs };
        return new Card(rank, suit);
    }

    public static void All()
    {
        Console.WriteLine("on-chain hand TAPE (every step its own transaction):");

        T.Run("a heads-up hand emits the full ordered tape of typed + money transactions", () =>
        {
            var a = Secp256k1.GenerateKeyPair(); var b = Secp256k1.GenerateKeyPair();
            var w = new OnChainWallet(WalletKeys.NewSeed());
            w.Add(new OnChainWallet.Utxo("aa".PadRight(64, '1'), 0, 100_000_000, 0, 0));
            var deck = new[] { C("As"), C("Ah"), C("2c"), C("3d"), C("Ad"), C("Kh"), C("Qs"), C("Jc"), C("9h") };
            long pot = 40000;
            var tape = OnChainHandTape.BuildHoldem(w, a, b, deck, pot, tableId: new byte[16], stepValue: 1000, fee: 500);

            var expected = new[]
            {
                TxKind.TableGenesis, TxKind.GameStart, TxKind.HandStart, TxKind.PotEscrow,
                TxKind.ShuffleStage, TxKind.ShuffleStage,
                TxKind.Deal, TxKind.Deal, TxKind.Deal, TxKind.Deal,
                TxKind.BoardReveal, TxKind.BoardReveal, TxKind.BoardReveal,
                TxKind.Bet, TxKind.Bet, TxKind.Bet, TxKind.Bet,
                TxKind.Showdown, TxKind.Showdown, TxKind.Settlement,
            };
            T.Eq(tape.Steps.Count, expected.Length, "20 transactions in one heads-up hand");
            for (int i = 0; i < expected.Length; i++) T.Eq(tape.Steps[i].Kind.ToString(), expected[i].ToString(), $"step {i} kind");

            // every transaction is distinct (real, separate on-chain txs)
            var txids = tape.Steps.Select(s => Chain.Txid(s.Tx)).ToHashSet();
            T.Eq(txids.Count, tape.Steps.Count, "all txids distinct");

            // every TYPED step parses back to its kind + owner (PotEscrow + Settlement are money txs, not typed)
            foreach (var step in tape.Steps)
            {
                if (step.Kind is TxKind.PotEscrow or TxKind.Settlement) continue;
                var parsed = TxTemplates.Parse(step.Tx.Outs[0].Script);
                T.True(parsed != null, $"{step.Kind} output is a typed output");
                T.Eq(parsed!.Kind.ToString(), step.Kind.ToString(), $"{step.Kind} parses to its kind");
                T.Eq(T.Hex(parsed.OwnerPub), T.Hex(step.OwnerPub), $"{step.Kind} owner round-trips");
            }

            // the Deal steps carry the REAL dealt card indices in their position/card fields
            var deals = tape.Steps.Where(s => s.Kind == TxKind.Deal).ToList();
            for (int pos = 0; pos < 4; pos++)
            {
                var f = TxTemplates.Parse(deals[pos].Tx.Outs[0].Script)!.Fields;
                T.Eq(f[1][0], (byte)pos, $"deal {pos} position");
                T.Eq(f[2][0], (byte)deck[pos].Index, $"deal {pos} carries the real card index");
            }

            // the real pot settles to the winner (seat 0 = trip aces), co-signed 2-of-2
            T.Eq(tape.WinnerSeat, 0, "seat 0 wins");
            T.True(Chain.VerifyMultisig2of2(tape.Settlement, 0, a.Pub, b.Pub, pot), "settlement is a valid 2-of-2 spend");
            T.Eq(T.Hex(tape.Settlement.Outs[0].Script), T.Hex(Chain.P2pkhLockForPub(a.Pub)), "pot paid to the winner");

            // value left the wallet only as fees + the parked step/pot values (no money created)
            T.True(w.Balance < 100_000_000 && w.Balance > 100_000_000 - 1_000_000, "wallet spent only modest fees/values");
        });
    }
}
