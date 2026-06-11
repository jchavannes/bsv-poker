using System.Text;
using BsvPoker.Core;

namespace BsvPoker.Tests;

/// <summary>
/// Key-graph broadcast encryption (GB 2623780 B): the message is encrypted ONCE under the root key; every
/// eligible user decrypts up its leaf→root path; an outsider cannot; and after a REVOKE the departed user
/// cannot read new messages while the remaining members still can.
/// </summary>
public static class BroadcastEncryptionTests
{
    public static void All()
    {
        Console.WriteLine("broadcast encryption (GB 2623780 B — key-graph, encrypt-once, revocable):");

        T.Run("build over a power-of-two group; message encrypted once; every eligible user decrypts it", () =>
        {
            var users = new ulong[] { 10, 20, 30, 40 };
            var g = BroadcastEncryption.Build(users);
            T.Eq(g.UserCount, 4, "four users");
            var items = g.EncryptedDataItems();
            var msg = Encoding.UTF8.GetBytes("only the group reads this");
            var sealed1 = g.EncryptMessage(msg);          // ONE ciphertext for the whole group
            foreach (var u in users)
            {
                var leafKey = g.UserLeafKey(u)!;
                T.Eq(Encoding.UTF8.GetString(g.UserDecrypt(u, leafKey, items, sealed1)), "only the group reads this", $"user {u} decrypts");
            }
        });

        T.Run("a user NOT in the group cannot decrypt", () =>
        {
            var g = BroadcastEncryption.Build(new ulong[] { 1, 2, 3, 4 });
            var items = g.EncryptedDataItems();
            var sealed1 = g.EncryptMessage(Encoding.UTF8.GetBytes("secret"));
            var stolenLeaf = g.UserLeafKey(1)!;
            bool blocked = false;
            try { g.UserDecrypt(999, stolenLeaf, items, sealed1); } catch { blocked = true; }
            T.True(blocked, "an outsider (not in the graph) is rejected");
        });

        T.Run("REVOKE: the departed member cannot read NEW messages; remaining members still can", () =>
        {
            var users = new ulong[] { 1, 2, 3, 4 };
            var g = BroadcastEncryption.Build(users);
            var revokedLeaf = g.UserLeafKey(3)!;          // capture user 3's key BEFORE revoke

            g.Revoke(3);                                  // user 3 leaves → path keys rotated
            var items = g.EncryptedDataItems();           // re-published after rekey
            var sealed2 = g.EncryptMessage(Encoding.UTF8.GetBytes("after revoke"));

            // remaining members still decrypt the new message
            foreach (var u in new ulong[] { 1, 2, 4 })
                T.Eq(Encoding.UTF8.GetString(g.UserDecrypt(u, g.UserLeafKey(u)!, items, sealed2)), "after revoke", $"member {u} still reads");

            // the revoked member cannot (not in the graph, and its old path keys were rotated)
            bool blocked = false;
            try { g.UserDecrypt(3, revokedLeaf, items, sealed2); } catch { blocked = true; }
            T.True(blocked, "the revoked member is locked out of new messages");
            T.Eq(g.UserCount, 3, "membership dropped to 3 after revoke");
        });
    }
}
