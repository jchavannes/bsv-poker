using System.Net;
using System.Net.Sockets;
using BsvPoker.Core;
using BsvPoker.Crypto;
using BsvPoker.Net.Bsv;

namespace BsvPoker.Tests;

/// <summary>
/// REGRESSION: a one-to-one chat message must actually be DELIVERED. The crypto/format round-trips
/// (OnChainChatTests), but messages never arrived because the receiver's <see cref="TxLink"/> bound
/// loopback-only while presence/chat advertise this machine's LAN IP — so a connection to the advertised
/// address was refused (even on the same machine). Here the receiver binds ALL interfaces and a real chat
/// transaction is pushed to its ADVERTISED address and decrypts. Loopback-only would have failed this.
/// </summary>
public static class ChatDeliveryTests
{
    private static string LocalIpv4()
    {
        try
        {
            foreach (var ip in Dns.GetHostAddresses(Dns.GetHostName()))
                if (ip.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(ip)) return ip.ToString();
        }
        catch { }
        return "127.0.0.1";
    }

    public static void All()
    {
        Console.WriteLine("chat delivery (a message is actually pushed IP-to-IP and arrives):");

        T.Run("a chat tx pushed to the receiver's ADVERTISED (LAN) address arrives and decrypts", () =>
        {
            var net = NetworkParams.For(BsvNetwork.Mainnet);
            var alice = Secp256k1.GenerateKeyPair();
            var bob = Secp256k1.GenerateKeyPair();

            Chain.Tx? got = null;
            using var arrived = new System.Threading.ManualResetEventSlim(false);
            using var bobLink = new TxLink(net, 0, IPAddress.Any);   // the fix: reachable on LAN + loopback
            bobLink.OnTransaction += tx => { got = tx; arrived.Set(); };
            bobLink.Start();

            // Alice builds a REAL funded chat transaction to Bob (same path the app uses)
            var w = new OnChainWallet(WalletKeys.NewSeed());
            w.Add(new OnChainWallet.Utxo("aa".PadRight(64, '1'), 0, 1_000_000, 0, 0));
            var script = OnChainChat.BuildScript(bob.Pub, alice.Priv, alice.Pub, 0, "hi bob — did this arrive?");
            var spend = w.SpendAction(script, 1000, 500);
            var raw = Chain.Serialize(spend.Tx);

            // push it to the address presence ADVERTISES (this machine's LAN IPv4, NOT loopback)
            var target = LocalIpv4();
            bool sent = TxLink.SendTxAsync(net, target, bobLink.Port, raw).GetAwaiter().GetResult();
            T.True(sent, $"the chat tx was pushed to the advertised address {target}:{bobLink.Port}");
            T.True(arrived.Wait(8000), "Bob RECEIVED the tx at his advertised address (a loopback-only bind would refuse it)");

            var msg = OnChainChat.TryReadTx(got!, bob.Priv, bob.Pub);
            T.True(msg != null && msg.Text == "hi bob — did this arrive?", "Bob decrypts the delivered message");
            T.Eq(T.Hex(msg!.SenderPub), T.Hex(alice.Pub), "the sender is identified");
        });

        T.Run("loopback delivery also works (same-machine two instances)", () =>
        {
            var net = NetworkParams.For(BsvNetwork.Mainnet);
            var alice = Secp256k1.GenerateKeyPair();
            var bob = Secp256k1.GenerateKeyPair();
            Chain.Tx? got = null;
            using var arrived = new System.Threading.ManualResetEventSlim(false);
            using var bobLink = new TxLink(net, 0, IPAddress.Any);
            bobLink.OnTransaction += tx => { got = tx; arrived.Set(); };
            bobLink.Start();
            var w = new OnChainWallet(WalletKeys.NewSeed());
            w.Add(new OnChainWallet.Utxo("bb".PadRight(64, '2'), 0, 1_000_000, 0, 0));
            var raw = Chain.Serialize(w.SpendAction(OnChainChat.BuildScript(bob.Pub, alice.Priv, alice.Pub, 1, "loopback hi"), 1000, 500).Tx);
            T.True(TxLink.SendTxAsync(net, "127.0.0.1", bobLink.Port, raw).GetAwaiter().GetResult(), "pushed over loopback");
            T.True(arrived.Wait(8000), "received over loopback");
            var msg = OnChainChat.TryReadTx(got!, bob.Priv, bob.Pub);
            T.True(msg != null && msg.Text == "loopback hi", "decrypts over loopback");
        });
    }
}
