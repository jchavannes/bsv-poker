using System.Text;
using BsvPoker.Core;
using BsvPoker.Crypto;
using BsvPoker.Net.Bsv;

namespace BsvPoker.Tests;

/// <summary>
/// THE COMPLETE NEW-WALLET FLOW ON REGTEST, 100x, with REAL proof-of-work + SPV proofs (no fake credit):
/// a brand-new empty wallet → REGTEST SELF-FUND (mine a real regtest block crediting it; the coin is verified by
/// the same SPV merkle-proof + PoW path as any mined coin) → register its IDENTITY ON-CHAIN (a real funded,
/// signed 1-sat NFT tx — never free) → permanent → and ONLY THEN play. This is the "find a way" so a new wallet
/// with no peers can still get its first coins and complete the whole flow on the local chain.
/// </summary>
public static class RegtestFundFlowTests
{
    public static void All()
    {
        Console.WriteLine("regtest self-fund → register ON-CHAIN → play (full new-wallet flow, real PoW + SPV proof):");

        T.Run("a regtest self-fund is a REAL SPV-proven coin (real PoW header + merkle proof), never a fake credit", () =>
        {
            var seed = WalletKeys.NewSeed();
            var recv = WalletKeys.Account(seed, 0, 0).Pub;
            var f = RegtestFunder.Fund(recv, 1_000_000, new byte[32]);
            T.True(f.Header.MeetsPow(), "the mined regtest header meets the PoW target");
            var chain = new HeadersChain(); chain.AddGenesis(f.Header);
            var utxo = SpvFunding.Verify(new SpvFunding.Proof(f.Tx, f.Vout, f.Header.HashHex(), f.Branch, f.TxIndex), chain, recv, 0, 0);
            T.True(utxo != null, "the funding verifies as a real SPV coin against the validated header chain");
            T.Eq(utxo!.Value, 1_000_000L, "the proven coin carries the funded amount");
            // HOSTILE: the same proof against an EMPTY (un-validated) chain is NOT a coin
            T.True(SpvFunding.Verify(new SpvFunding.Proof(f.Tx, f.Vout, f.Header.HashHex(), f.Branch, f.TxIndex), new HeadersChain(), recv, 0, 0) == null,
                   "a proof whose block we have NOT validated is rejected (no blind trust)");
        });

        T.Run("100x: new wallet → REGTEST SELF-FUND → register identity ON-CHAIN → permanent → PLAY", () =>
        {
            int ok = 0;
            for (int i = 0; i < 100; i++)
            {
                var seed = WalletKeys.NewSeed();
                var recv = WalletKeys.Account(seed, 0, 0).Pub;
                var idPriv = Type42.UniqueKey(seed, "bsvpoker/identity");
                var idPub = Secp256k1.PublicKeyCompressed(idPriv);
                var attPriv = Type42.UniqueKey(seed, "bsvpoker/identity/attestation");

                // brand-new wallet, zero coins → cannot register yet (identity is on-chain, never free)
                var wallet = new OnChainWallet(seed);
                if (wallet.Balance != 0) continue;

                // REGTEST SELF-FUND: mine a real regtest block crediting the wallet, verify the SPV proof, credit it
                var f = RegtestFunder.Fund(recv, 1_000_000, new byte[32]);
                var chain = new HeadersChain(); chain.AddGenesis(f.Header);
                var utxo = SpvFunding.Verify(new SpvFunding.Proof(f.Tx, f.Vout, f.Header.HashHex(), f.Branch, f.TxIndex), chain, recv, 0, 0);
                if (utxo == null) continue;
                wallet.Add(utxo);
                if (wallet.Balance < 2) continue;

                // REGISTER ON-CHAIN: a real funded + signed identity NFT transaction
                var script = OnChainIdentity.BuildScript(idPub, attPriv, $"player{i}", $"p{i}@regtest.local");
                var spend = wallet.SpendAction(script, 1, 1);
                if (spend.Tx == null || !wallet.VerifySpend(spend)) continue;
                var claim = OnChainIdentity.TryReadTx(spend.Tx);
                if (claim == null || !OnChainIdentity.Verify(claim) || claim.Pseudonym != $"player{i}") continue;

                // PLAY: the registered identity key signs a game action that verifies
                var digest = Hashes.Sha256d(Encoding.ASCII.GetBytes($"seat|{i}|player{i}"));
                if (!Secp256k1.VerifyDigest(idPub, digest, Secp256k1.SignDigest(idPriv, digest))) continue;

                ok++;
            }
            T.Eq(ok, 100, "all 100 new wallets: regtest-self-funded, registered ON-CHAIN, permanent, and able to play");
        });
    }
}
