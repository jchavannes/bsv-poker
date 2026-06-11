using System.Security.Cryptography;
using BsvPoker.Core;
using BsvPoker.Crypto;

namespace BsvPoker.Tests;

/// <summary>
/// FULLY on-chain multiway betting under dealerless threshold custody — the principal's ruling: the entire
/// game is on-chain, EVERY move is a funded transaction, no exceptions. Each player's chips are REAL satoshis
/// in real UTXOs; every betting action spends the player's coin to pay its wager into the dealerless
/// (t+1)-of-n threshold pot (a plain P2PKH no one holds the key to); the pot accumulates as on-chain outputs;
/// and the winner is paid by a single multi-input THRESHOLD signature. Value is conserved across the whole
/// tape, and every transaction verifies on the ordinary consensus path.
/// </summary>
public static class OnChainBettingTests
{
    private static (OnChainWallet W, byte[] Seed) FundedWallet(long stake, string txid)
    {
        var seed = RandomNumberGenerator.GetBytes(32);
        var w = new OnChainWallet(seed);
        w.Add(new OnChainWallet.Utxo(txid, 0, stake, 0, 0));   // a real funded buy-in, locked to key (0,0)
        return (w, seed);
    }

    public static void All()
    {
        Console.WriteLine("fully on-chain multiway betting under threshold custody (every move a funded tx):");

        T.Run("every betting action funds the threshold pot on-chain; winner paid by a threshold sig; value conserved", () =>
        {
            const int n = 3, t = 1;
            var pot = ThresholdEcdsa.Jvrss(n, t);                  // dealerless pot key (no one holds it)
            var potLock = ThresholdCustody.PotLock(pot);
            const long actionFee = 1, settleFee = 1;   // tiny minimal per-tx fee — economics irrelevant; this is a tech demo

            var pls = new (OnChainWallet W, byte[] Seed)[n];
            for (int i = 0; i < n; i++) pls[i] = FundedWallet(2000, ((byte)(0xA0 + i)).ToString("x2") + new string('0', 62));
            long[] startBal = pls.Select(p => p.W.Balance).ToArray();
            long[] contributed = new long[n];

            // a betting tape: blinds, a raise, a call — EVERY action is its own funded transaction
            var actions = new (int Pl, long Wager)[] { (0, 25), (1, 50), (2, 50), (0, 75), (1, 50) };
            var potUtxos = new List<(string Txid, uint Vout, long Value)>();
            foreach (var (pl, wager) in actions)
            {
                var spend = pls[pl].W.SpendAction(potLock, wager, actionFee);   // real funded tx, wallet advances
                for (int i = 0; i < spend.Inputs.Count; i++)
                {
                    var k = WalletKeys.Account(pls[pl].Seed, spend.Inputs[i].KeyChain, spend.Inputs[i].KeyIndex);
                    T.True(Chain.VerifyP2pkhInput(spend.Tx, i, k.Pub, spend.Inputs[i].Value), "the player's contribution input is validly signed (real coins moving)");
                }
                T.Eq(spend.Tx.Outs[0].Value, wager, "the action funds the pot with exactly the wager");
                T.True(spend.Tx.Outs[0].Script.SequenceEqual(potLock), "the wager is paid to the threshold pot key");
                potUtxos.Add((Chain.Txid(spend.Tx), 0u, wager));
                contributed[pl] += wager;
            }

            long potTotal = potUtxos.Sum(u => u.Value);
            T.Eq(potTotal, actions.Sum(a => a.Wager), "the pot equals the sum of all on-chain wagers");
            for (int i = 0; i < n; i++)
            {
                int acts = actions.Count(a => a.Pl == i);
                T.Eq(pls[i].W.Balance, startBal[i] - contributed[i] - acts * actionFee, "wallet balance reflects the on-chain spend (wagers + fees left the wallet)");
            }

            // settle the WHOLE pot (all its UTXOs) to the winner with a multi-input THRESHOLD signature
            var winner = Secp256k1.GenerateKeyPair().Pub;
            var settle = ThresholdCustody.SettleManyToWinner(potUtxos, winner, settleFee, pot);
            for (int i = 0; i < potUtxos.Count; i++)
                T.True(Chain.VerifyP2pkhInput(settle, i, pot.PublicKey, potUtxos[i].Value), "each pot input is spent by a valid threshold signature");
            T.Eq(settle.Outs[0].Value, potTotal - settleFee, "winner receives the whole pot − fee (value conserved end-to-end)");
            T.True(settle.Outs[0].Script.SequenceEqual(Chain.P2pkhLockForPub(winner)), "the pot is paid to the winner");
        });

        T.Run("a folded/abandoned pot is recoverable to the contributors by a threshold nLockTime refund", () =>
        {
            const int n = 3, t = 1;
            var pot = ThresholdEcdsa.Jvrss(n, t);
            var potLock = ThresholdCustody.PotLock(pot);
            var (w, seed) = FundedWallet(2000, "bb" + new string('0', 62));
            var spend = w.SpendAction(potLock, 120, 1);
            var potUtxo = (Chain.Txid(spend.Tx), 0u, 120L);
            // the contributor can always get the pot back after a timeout via the pre-agreed threshold refund
            var contributor = WalletKeys.Account(seed, 0, 0).Pub;
            var rec = ThresholdCustody.BuildRecovery(potUtxo.Item1, potUtxo.Item2, 120, new[] { (contributor, 120L - 1) }, pot, lockHeight: 900_000);
            T.True(Chain.VerifyP2pkhInput(rec, 0, pot.PublicKey, 120), "threshold nLockTime refund of the pot verifies");
            T.Eq(rec.LockTime, 900_000u, "refund binds the lock height");
        });
    }
}
