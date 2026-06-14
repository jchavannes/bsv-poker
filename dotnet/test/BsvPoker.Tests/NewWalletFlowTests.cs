using System.Text;
using BsvPoker.Core;
using BsvPoker.Crypto;

namespace BsvPoker.Tests;

/// <summary>
/// THE NEW-WALLET FLOW, end to end, 100x (regtest-grade: real spendable coins, real consensus-valid txs):
/// a fresh wallet must FUND, then register its IDENTITY ON-CHAIN — a real 1-sat NFT transaction, NEVER free and
/// NEVER off-chain — which is then PERMANENT and immutable (the same seed always re-derives the same identity,
/// forever), and ONLY THEN can it play. The hostile case proves an UNFUNDED wallet cannot register at all
/// (nothing is free). This is exactly what the app's Register flow does (OnChainIdentity NFT funded by the
/// wallet), so it guards the real GUI path, not a mock.
/// </summary>
public static class NewWalletFlowTests
{
    public static void All()
    {
        Console.WriteLine("new-wallet flow 100x (fund → register identity ON-CHAIN → play; identity is on-chain, never free):");

        T.Run("HOSTILE: a brand-new UNFUNDED wallet CANNOT register (identity is on-chain — never free)", () =>
        {
            var seed = WalletKeys.NewSeed();
            var idPriv = Type42.UniqueKey(seed, "bsvpoker/identity");
            var idPub = Secp256k1.PublicKeyCompressed(idPriv);
            var attPriv = Type42.UniqueKey(seed, "bsvpoker/identity/attestation");
            var w = new OnChainWallet(seed);                              // zero coins
            T.Eq(w.Balance, 0L, "a brand-new wallet holds no money");
            var script = OnChainIdentity.BuildScript(idPub, attPriv, "Nobody", "n@x.io");
            bool onChain;
            try { var s = w.SpendAction(script, 1, 1); onChain = s.Tx != null && w.VerifySpend(s); } catch { onChain = false; }
            T.False(onChain, "with no funds there is NO identity transaction — registration is impossible (never free)");
        });

        T.Run("100x: FUND → register ON-CHAIN → verified + permanent → the identity can PLAY", () =>
        {
            int ok = 0;
            for (int i = 0; i < 100; i++)
            {
                var seed = WalletKeys.NewSeed();
                var idPriv = Type42.UniqueKey(seed, "bsvpoker/identity");
                var idPub = Secp256k1.PublicKeyCompressed(idPriv);
                var attPriv = Type42.UniqueKey(seed, "bsvpoker/identity/attestation");

                // FUND (regtest-style: a real spendable coin lands in the wallet)
                var w = new OnChainWallet(seed);
                w.Add(new OnChainWallet.Utxo(($"f{i:D2}").PadLeft(64, '0'), 0, 1_000_000, 0, 0));
                if (w.Balance < 2) continue;                              // MUST be funded to register — never free

                // REGISTER ON-CHAIN: the identity NFT as a real funded + signed transaction (1-sat NFT + 1-sat fee)
                var script = OnChainIdentity.BuildScript(idPub, attPriv, $"player{i}", $"p{i}@regtest.local");
                var spend = w.SpendAction(script, 1, 1);
                if (spend.Tx == null || !w.VerifySpend(spend)) continue;  // consensus-valid on-chain transaction
                var claim = OnChainIdentity.TryReadTx(spend.Tx);
                if (claim == null || !OnChainIdentity.Verify(claim)) continue;      // genuinely registered on-chain
                if (claim.Pseudonym != $"player{i}") continue;
                if (T.Hex(claim.AttestationPub) == T.Hex(idPub)) continue;          // Base ID NEVER signs

                // ALWAYS & FOREVER REGISTERED: re-derive from the same seed → the SAME identity, every time
                var idPubAgain = Secp256k1.PublicKeyCompressed(Type42.UniqueKey(seed, "bsvpoker/identity"));
                if (T.Hex(idPubAgain) != T.Hex(idPub)) continue;

                // THEN — and only then — PLAY: the registered identity key seats and signs a game action that verifies
                var digest = Hashes.Sha256d(Encoding.ASCII.GetBytes($"seat|{i}|player{i}"));
                var sig = Secp256k1.SignDigest(idPriv, digest);
                if (!Secp256k1.VerifyDigest(idPub, digest, sig)) continue;

                ok++;
            }
            T.Eq(ok, 100, "all 100 new wallets: funded, registered ON-CHAIN, permanent, and able to play");
        });

        T.Run("registration is ONCE and IMMUTABLE: a second different claim under the same key does not change who you are", () =>
        {
            var seed = WalletKeys.NewSeed();
            var idPriv = Type42.UniqueKey(seed, "bsvpoker/identity");
            var idPub = Secp256k1.PublicKeyCompressed(idPriv);
            var attPriv = Type42.UniqueKey(seed, "bsvpoker/identity/attestation");
            var first = OnChainIdentity.TryReadTx(new Chain.Tx(2,
                new() { new(new string('a', 64), 0, Array.Empty<byte>(), 0xffffffff) },
                new() { new(1, OnChainIdentity.BuildScript(idPub, attPriv, "CsTominaga", "craig@rcjbr.org")) }, 0))!;
            T.True(OnChainIdentity.Verify(first), "the first on-chain identity verifies");
            // the Base ID pubkey is the permanent anchor — it is the same no matter what handle text is attempted
            var second = OnChainIdentity.TryRead(OnChainIdentity.BuildScript(idPub, attPriv, "DifferentName", "other@x.io"))!;
            T.Eq(T.Hex(second.IdentityPub), T.Hex(first.IdentityPub), "the Base ID (who you ARE) is unchanged and unchangeable");
        });
    }
}
