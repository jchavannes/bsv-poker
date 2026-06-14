using BsvPoker.Core;
using BsvPoker.Core.Games;
using BsvPoker.Crypto;

namespace BsvPoker.Tests;

/// <summary>
/// The REPLAY tab's "Load a demo on-chain hand" must always succeed and produce a fully-parseable tape, so the
/// replay is genuinely actionable: every step is a real typed transaction whose kind + fields parse back out,
/// the tape ends in a settlement, and the move sequence reads like a hand. Mirrors exactly what ReplayView does.
/// </summary>
public static class ReplayDemoTests
{
    public static void All()
    {
        Console.WriteLine("replay (on-chain hand tape loads, every move parses, ends in settlement):");

        T.Run("the demo Hold'em tape builds and every step is a real, parseable on-chain transaction", () =>
        {
            var seed = new byte[32]; seed[0] = 7; seed[31] = 1;
            var wallet = new OnChainWallet(seed);
            var k = WalletKeys.Account(seed, 0, 0);
            wallet.Add(new OnChainWallet.Utxo(new string('a', 64), 0, 5_000_000, 0, 0));
            var a = (k.Priv, k.Pub);
            var bSeed = new byte[32]; bSeed[0] = 9; bSeed[31] = 2;
            var bk = WalletKeys.Account(bSeed, 0, 0);
            var b = (bk.Priv, bk.Pub);
            var deck = MentalPoker.ShuffledFrom(new[] { MentalPoker.FreshEntropy(), MentalPoker.FreshEntropy() }, Variants.CardSet(Variant.TexasHoldem));

            var tape = OnChainHandTape.BuildHoldem(wallet, a, b, deck, pot: 40_000, tableId: new byte[16], stepValue: 1000, fee: 1000);

            T.True(tape.Steps.Count > 10, $"a full hand emits many on-chain steps (got {tape.Steps.Count})");
            T.Eq(tape.Steps[^1].Kind, TxKind.Settlement, "the tape ends in a settlement (the payout)");
            T.True(tape.Pot == 40_000, "the pot is carried on the tape");

            // Every step has a real txid. The DATA-carrying steps (table/game/hand/deal/board/bet/showdown/
            // shuffle) carry a parseable typed PUSHDATA output; the MONEY steps (the pot escrow, the up-front
            // recovery, the settlement payout) are real multisig/P2PKH transactions, NOT typed-pushdata — those
            // are correctly money-only. So: every data-kind step MUST parse; money-kind steps need not.
            var moneyKinds = new HashSet<TxKind> { TxKind.PotEscrow, TxKind.Recovery, TxKind.Settlement };
            foreach (var step in tape.Steps)
            {
                var txid = Chain.Txid(step.Tx);
                T.True(txid.Length == 64, "each step has a real 32-byte txid");
                bool typed = step.Tx.Outs.Any(o => TxTemplates.Parse(o.Script) != null);
                if (!moneyKinds.Contains(step.Kind))
                    T.True(typed, $"the {step.Kind} step carries a parseable typed output");
            }

            // the steps include the human-recognisable beats a replay narrates
            var kinds = tape.Steps.Select(s => s.Kind).ToHashSet();
            foreach (var need in new[] { TxKind.PotEscrow, TxKind.Deal, TxKind.BoardReveal, TxKind.Bet, TxKind.Showdown, TxKind.Settlement })
                T.True(kinds.Contains(need), $"the replay contains a {need} step");
        });
    }
}
