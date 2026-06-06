using System.Text;
using BsvPoker.Crypto;
using BsvPoker.Net;

namespace BsvPoker.Tests;

public static class NetChatTests
{
    private static bool Until(Func<bool> c, int ms = 6000)
    {
        var dl = Environment.TickCount64 + ms;
        while (Environment.TickCount64 < dl) { if (c()) return true; Thread.Sleep(25); }
        return c();
    }

    public static void All()
    {
        Console.WriteLine("encrypted chat over the mesh (DM, no key reuse):");

        T.Run("a direct message is delivered DECRYPTED to the recipient; the wire is ciphertext", () =>
        {
            using var a = new P2PNode(0, "127.0.0.1");
            using var b = new P2PNode(0, "127.0.0.1");
            a.StartAsync().Wait();
            b.StartAsync(new[] { new P2PNode.PeerAddr("127.0.0.1", a.BoundPort) }).Wait();
            T.True(Until(() => a.PeerCount >= 1 && b.PeerCount >= 1), "connected");

            var ka = Secp256k1.GenerateKeyPair();
            var kb = Secp256k1.GenerateKeyPair();
            var aHex = Convert.ToHexString(ka.Pub).ToLowerInvariant();
            var bHex = Convert.ToHexString(kb.Pub).ToLowerInvariant();
            var csA = new ChatService(a, ka.Priv, ka.Pub);
            var csB = new ChatService(b, kb.Priv, kb.Pub);

            // wire-tap the DM topic to prove it's encrypted
            string wire = "";
            var convA = csA.OpenDm(bHex, "Bob");
            var convB = csB.OpenDm(aHex, "Alice");
            b.Subscribe(convB.Topic, t => { if (t.Contains("\"msg\"")) wire = t; });
            Thread.Sleep(150);

            csA.Send(convA.Id, "hello-bob-secret");
            T.True(Until(() => convB.Messages.Any(m => m.Text == "hello-bob-secret" && m.FromHex == aHex)), "Bob received the decrypted DM");
            T.True(wire.Length > 0, "a wire frame was seen");
            T.False(wire.Contains("hello-bob-secret"), "the wire frame is ENCRYPTED (plaintext not present)");
        });

        T.Run("chat history persists across restart (conversations + messages reload from disk)", () =>
        {
            var dir = Path.Combine(Path.GetTempPath(), "bsvpoker-chat-" + Guid.NewGuid().ToString("N"));
            try
            {
                var ka = Secp256k1.GenerateKeyPair();
                var kb = Secp256k1.GenerateKeyPair();
                var bHex = Convert.ToHexString(kb.Pub).ToLowerInvariant();

                // first "session": open a DM and send a message (no peer connected — Send still records my own + Saves)
                using (var n1 = new P2PNode(0, "127.0.0.1"))
                {
                    var cs1 = new ChatService(n1, ka.Priv, ka.Pub, dir);
                    var conv = cs1.OpenDm(bHex, "Bob");
                    cs1.Send(conv.Id, "persist-me-please");
                    T.Eq(0, cs1.Conversations.Count - 1, "one conversation exists after send"); // 1 conv
                }

                // second "session": brand-new service on the SAME dir must reload it
                using (var n2 = new P2PNode(0, "127.0.0.1"))
                {
                    var cs2 = new ChatService(n2, ka.Priv, ka.Pub, dir);
                    T.Eq(1, cs2.Conversations.Count, "conversation reloaded after restart");
                    var conv = cs2.Conversations[0];
                    T.Eq("Bob", conv.Title, "title persisted");
                    T.True(conv.Messages.Any(m => m.Text == "persist-me-please"), "message text persisted across restart");
                }
            }
            finally { try { Directory.Delete(dir, true); } catch { } }
        });
    }
}
