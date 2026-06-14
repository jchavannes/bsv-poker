using BsvPoker.Core;
using BsvPoker.Crypto;

namespace BsvPoker.Tests;

/// <summary>
/// END-TO-END stealth-point conveyance (US20240152913A1, OP_RETURN-free). Group A's funding point P = e·G is
/// conveyed in a TYPED PUSHDATA output (NOT OP_RETURN). The withdrawing group B reads P straight off the chain,
/// derives c = H(d·P) by threshold ECDH, reconstructs the pool key A_pool = Q + c·G, and spends it with the
/// (d + c) threshold handle — with NO off-chain channel. Hostile cases: a tampered P is rejected, a non-recipient
/// cannot derive the spend key, and NO output anywhere is an OP_RETURN.
/// </summary>
public static class StealthConveyanceTests
{
    public static void All()
    {
        Console.WriteLine("stealth conveyance (on-chain P via typed PUSHDATA, no OP_RETURN):");

        T.Run("POSITIVE: recipient reads P off the output, derives the SAME c and pool key, and the (d+c) threshold spend verifies", () =>
        {
            var groupA = ThresholdEcdsa.Jvrss(n: 3, degree: 1);   // e, P = e·G (the funder)
            var groupB = ThresholdEcdsa.Jvrss(n: 5, degree: 2);   // d, Q = d·G (the 3-of-5 withdrawing group)

            // funder publishes P in a typed PUSHDATA output owned by group B's withdrawal key Q
            var output = StealthConveyance.BuildPointOutput(groupA.PublicKey, groupB.PublicKey);
            T.False(output[0] == 0x6a, "the conveyance output is NOT an OP_RETURN");

            // recipient recovers everything from the on-chain output alone — no off-chain channel
            var rec = StealthConveyance.Recover(output, groupB);
            T.True(rec != null, "recipient parses the output and recovers c / pool key / spend handle");
            var (c, poolKey, spendKey) = rec!.Value;

            // it is the SAME c the funder derives from its side (H(e·Q) = H(d·P)) and the SAME pool key
            var cFromFunder = StealthDistribution.SharedSecret(groupA.Shares, groupB.PublicKey);
            T.True(c.SequenceEqual(cFromFunder), "on-chain-derived c == funder's c (threshold ECDH agrees)");
            T.True(poolKey.SequenceEqual(StealthDistribution.PoolKey(groupB.PublicKey, c)), "pool key A_pool = Q + c·G");
            T.True(spendKey.PublicKey.SequenceEqual(poolKey), "spend handle's public key is A_pool");

            // the threshold (d+c) spend of the pool verifies on the ordinary consensus path
            const string poolTxid = "33" + "00000000000000000000000000000000000000000000000000000000000000";
            const long amount = 50_000, fee = 150;
            var payee = Secp256k1.GenerateKeyPair().Pub;
            var paid = ThresholdCustody.SettleToWinner(poolTxid, 0, amount, payee, fee, spendKey);
            T.True(Chain.VerifyP2pkhInput(paid, 0, spendKey.PublicKey, amount), "the (d+c) threshold spend verifies on Chain.VerifyP2pkhInput");
            T.Eq(paid.Outs[0].Value, amount - fee, "value conserved to the payee");
        });

        T.Run("the conveyance output round-trips P and the owner, and is recognised by its marker", () =>
        {
            var groupA = ThresholdEcdsa.Jvrss(n: 3, degree: 1);
            var groupB = ThresholdEcdsa.Jvrss(n: 3, degree: 1);
            var output = StealthConveyance.BuildPointOutput(groupA.PublicKey, groupB.PublicKey);
            var conv = StealthConveyance.Parse(output);
            T.True(conv != null, "parses as a stealth-point announcement");
            T.True(conv!.P.SequenceEqual(groupA.PublicKey), "P read back is exactly the funder's point");
            T.True(conv.OwnerPub.SequenceEqual(groupB.PublicKey), "owner read back is exactly group B's key Q");
            // ends in <ownerPub> OP_CHECKSIG → a real owned/spendable output, and never an OP_RETURN
            T.Eq(output[^1], (byte)0xac, "script ends in OP_CHECKSIG (owned/spendable)");
            T.False(output[0] == 0x6a, "first opcode is a pushdata marker, not OP_RETURN");
        });

        T.Run("HOSTILE: a tampered P is rejected on parse — no bogus point passes through", () =>
        {
            var groupA = ThresholdEcdsa.Jvrss(n: 3, degree: 1);
            var groupB = ThresholdEcdsa.Jvrss(n: 3, degree: 1);
            var output = StealthConveyance.BuildPointOutput(groupA.PublicKey, groupB.PublicKey);

            // flip a byte inside the conveyed P (the marker is "BSVP:STEALTHP:1" = 15 bytes, then OP_DROP,
            // then the 1-byte push length 0x21, so P's first body byte is well past the marker)
            var tampered = (byte[])output.Clone();
            int pIdx = Array.IndexOf(tampered, (byte)0x21);   // the 33-byte push opcode for P
            T.True(pIdx > 0, "located the P push");
            tampered[pIdx + 1] ^= 0xff;                       // corrupt P's prefix/body → off-curve or wrong point
            tampered[pIdx + 5] ^= 0x55;
            var conv = StealthConveyance.Parse(tampered);
            // either the point is now off-curve (Parse returns null) OR it is a different valid point (≠ P).
            if (conv != null)
                T.False(conv.P.SequenceEqual(groupA.PublicKey), "a tampered P is never accepted AS the funder's P");
            else
                T.True(true, "tampered P is off-curve and rejected outright");

            // and even if some tampered P parses, group B derives a DIFFERENT c ⇒ a DIFFERENT pool key,
            // so the spend handle does NOT match the genuine pool key — the tamper cannot steal the real pool
            var honest = StealthConveyance.Recover(output, groupB)!.Value;
            var attacked = StealthConveyance.Recover(tampered, groupB);
            if (attacked != null)
                T.False(attacked.Value.PoolKey.SequenceEqual(honest.PoolKey), "tampered P yields a different pool key, not the genuine one");
        });

        T.Run("HOSTILE: a non-recipient (knows P off-chain but holds the WRONG d-shares) cannot derive the spend key", () =>
        {
            var groupA = ThresholdEcdsa.Jvrss(n: 3, degree: 1);
            var groupB = ThresholdEcdsa.Jvrss(n: 3, degree: 1);   // the true recipient (holds d)
            var outsider = ThresholdEcdsa.Jvrss(n: 3, degree: 1); // an unrelated coalition (different d')
            var output = StealthConveyance.BuildPointOutput(groupA.PublicKey, groupB.PublicKey);

            var honest = StealthConveyance.Recover(output, groupB)!.Value;

            // the outsider reads the SAME on-chain P but, with its own shares, derives a DIFFERENT c, a DIFFERENT
            // pool key, and a spend handle that does NOT control the genuine pool output.
            var bad = StealthConveyance.Recover(output, outsider)!.Value;
            T.False(bad.C.SequenceEqual(honest.C), "outsider's c differs (it lacks group B's d)");
            T.False(bad.PoolKey.SequenceEqual(honest.PoolKey), "outsider cannot reconstruct A_pool");

            // concretely: a spend signed with the outsider's handle does NOT verify against the genuine pool key
            const string poolTxid = "44" + "00000000000000000000000000000000000000000000000000000000000000";
            const long amount = 40_000, fee = 120;
            var payee = Secp256k1.GenerateKeyPair().Pub;
            var theft = ThresholdCustody.SettleToWinner(poolTxid, 0, amount, payee, fee, bad.SpendKey);
            T.False(Chain.VerifyP2pkhInput(theft, 0, honest.PoolKey, amount), "outsider's signature cannot spend the genuine pool output");
        });

        T.Run("HOSTILE: NO OP_RETURN anywhere — every output script in a real conveyance+payout tx is asserted non-0x6a", () =>
        {
            var groupA = ThresholdEcdsa.Jvrss(n: 3, degree: 1);
            var groupB = ThresholdEcdsa.Jvrss(n: 3, degree: 1);

            // 1) the conveyance output itself
            var conveyance = StealthConveyance.BuildPointOutput(groupA.PublicKey, groupB.PublicKey);
            T.False(conveyance.Length > 0 && conveyance[0] == 0x6a, "conveyance output does not start with OP_RETURN (0x6a)");

            // 2) the pool lock + the actual payout transaction's outputs
            var (_, poolKey, spendKey) = StealthConveyance.Recover(conveyance, groupB)!.Value;
            var poolLock = StealthDistribution.PoolLock(poolKey);
            T.True(poolLock.Length == 25 && poolLock[0] == 0x76, "pool output is a plain P2PKH (no OP_RETURN)");

            const string poolTxid = "55" + "00000000000000000000000000000000000000000000000000000000000000";
            var payee = Secp256k1.GenerateKeyPair().Pub;
            var paid = ThresholdCustody.SettleToWinner(poolTxid, 0, 30_000, payee, 100, spendKey);
            foreach (var o in paid.Outs)
                T.False(o.Script.Length > 0 && o.Script[0] == 0x6a, "no output in the payout tx starts with OP_RETURN");

            // belt-and-braces: the conveyance carrying P proves data DOES ride pushdata-in-script, not OP_RETURN
            T.True(StealthConveyance.Parse(conveyance) != null, "P is conveyed via a parseable typed PUSHDATA output");
        });
    }
}
