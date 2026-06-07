using BsvPoker.Core;
using BsvPoker.Crypto;
using BsvPoker.Net.Bsv;

namespace BsvPoker.Tests;

/// <summary>
/// The ONLY player-to-player transport carries nothing but Bitcoin transactions. This proves a player can
/// push a Bitcoin tx IP-to-IP straight to another player (the Bitcoin-wire tx message), and the peer parses
/// it — exactly the mandated model (the same tx is also broadcast to miners for on-chain storage).
/// </summary>
public static class TxLinkTests
{
    public static void All()
    {
        Console.WriteLine("IP-to-IP transport (every packet is a Bitcoin transaction):");

        T.Run("a transaction is delivered IP-to-IP as a Bitcoin tx message and parsed by the peer", () =>
        {
            var net = NetworkParams.For(BsvNetwork.Regtest);
            using var link = new TxLink(net, 0);
            Chain.Tx? received = null;
            using var ev = new ManualResetEventSlim();
            link.OnTransaction += tx => { received = tx; ev.Set(); };
            link.Start();

            var k = Secp256k1.GenerateKeyPair();
            var tx = new Chain.Tx(2,
                new() { new("ab".PadRight(64, '1'), 0, Array.Empty<byte>(), 0xffffffff) },
                new() { new(1000, Chain.P2pkhLockForPub(k.Pub)) }, 0);
            var raw = Chain.Serialize(tx);

            var ok = TxLink.SendTxAsync(net, "127.0.0.1", link.Port, raw).GetAwaiter().GetResult();
            T.True(ok, "send completed over the Bitcoin wire");
            T.True(ev.Wait(TimeSpan.FromSeconds(5)), "the peer received the pushed transaction");
            T.Eq(Chain.Txid(received!), Chain.Txid(tx), "the received tx is byte-identical (txid matches)");
        });

        T.Run("a chat message delivered IP-to-IP decrypts for the recipient", () =>
        {
            var net = NetworkParams.For(BsvNetwork.Regtest);
            using var link = new TxLink(net, 0);
            var bob = Secp256k1.GenerateKeyPair();
            var alice = Secp256k1.GenerateKeyPair();
            OnChainChat.Incoming? got = null;
            using var ev = new ManualResetEventSlim();
            link.OnTransaction += tx => { got = OnChainChat.TryReadTx(tx, bob.Priv, bob.Pub); if (got != null) ev.Set(); };
            link.Start();

            // Alice funds a chat tx and pushes it straight to Bob's IP (and would also send it to miners)
            var w = new OnChainWallet(WalletKeys.NewSeed());
            w.Add(new OnChainWallet.Utxo("cd".PadRight(64, '2'), 0, 1_000_000, 0, 0));
            var spend = w.SpendAction(OnChainChat.BuildScript(bob.Pub, alice.Pub, "nice hand"), 1000, 500);
            TxLink.SendTxAsync(net, "127.0.0.1", link.Port, Chain.Serialize(spend.Tx)).GetAwaiter().GetResult();

            T.True(ev.Wait(TimeSpan.FromSeconds(5)), "Bob received the chat tx IP-to-IP");
            T.Eq(got!.Text, "nice hand", "and decrypted it");
        });
    }
}
