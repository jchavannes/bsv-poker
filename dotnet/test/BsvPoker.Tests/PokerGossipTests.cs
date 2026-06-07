using BsvPoker.Net;

namespace BsvPoker.Tests;

/// <summary>
/// The poker gossip + query overlay: announcements PROPAGATE across multiple hops (a node learns of peers it
/// was never directly told about, via forwarding), QUERY pulls a peer's known set, and de-duplication stops
/// flood loops. This is how poker nodes find each other with no central directory.
/// </summary>
public static class PokerGossipTests
{
    public static void All()
    {
        Console.WriteLine("poker gossip overlay (nodes find each other across hops):");

        T.Run("an announcement propagates across hops; all nodes discover all others", () =>
        {
            var registry = new Dictionary<string, PokerGossip>();
            // send(peerPub, peerEndpoint, msg) → deliver to the node listening at peerEndpoint
            void Wire(PokerGossip g) { }
            PokerGossip Make(string pub) => new(pub, pub /*endpoint == pub for the test*/,
                (peerPub, endpoint, msg) => { if (registry.TryGetValue(endpoint, out var t)) t.Receive(msg); });

            var a = Make("A"); var b = Make("B"); var c = Make("C"); var d = Make("D");
            registry["A"] = a; registry["B"] = b; registry["C"] = c; registry["D"] = d;

            // a line topology: A↔B, B↔C, C↔D (no node initially knows the far ends)
            a.AddSeed("B", "B"); b.AddSeed("A", "A");
            b.AddSeed("C", "C"); c.AddSeed("B", "B");
            c.AddSeed("D", "D"); d.AddSeed("C", "C");

            // everyone announces; announcements flood across hops (dedup prevents loops)
            a.Announce(); b.Announce(); c.Announce(); d.Announce();
            // a pull round to fill any gaps
            a.Query(); b.Query(); c.Query(); d.Query();

            foreach (var (name, node) in new[] { ("A", a), ("B", b), ("C", c), ("D", d) })
            {
                var known = node.Peers.Select(p => p.PubHex).OrderBy(x => x).ToArray();
                var expected = new[] { "A", "B", "C", "D" }.Where(x => x != name).ToArray();
                T.Eq(string.Join(",", known), string.Join(",", expected), $"{name} discovered every other node");
            }
        });

        T.Run("a node two hops away is discovered purely by forwarding (no direct link, no query)", () =>
        {
            var registry = new Dictionary<string, PokerGossip>();
            PokerGossip Make(string pub) => new(pub, pub,
                (peerPub, endpoint, msg) => { if (registry.TryGetValue(endpoint, out var t)) t.Receive(msg); });
            var a = Make("A"); var b = Make("B"); var c = Make("C");
            registry["A"] = a; registry["B"] = b; registry["C"] = c;
            a.AddSeed("B", "B"); b.AddSeed("A", "A"); b.AddSeed("C", "C"); c.AddSeed("B", "B");

            a.Announce();   // A → B, B forwards → C
            T.True(c.Peers.Any(p => p.PubHex == "A"), "C learned of A two hops away via forwarding alone");
        });
    }
}
