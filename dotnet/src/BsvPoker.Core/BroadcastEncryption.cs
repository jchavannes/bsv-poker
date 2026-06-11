using System.Security.Cryptography;
using BsvPoker.Crypto;

namespace BsvPoker.Core;

/// <summary>
/// Key-graph BROADCAST ENCRYPTION (GB 2623780 B) — ported from the reference Rust implementation in
/// overlay-broadcast. A balanced binary key tree over a power-of-two set of users: every node holds a 32-byte
/// symmetric key, the ROOT key is the message-encryption key, and each non-root node's key
/// authenticated-wraps (AES-GCM) its PARENT's key. The message is encrypted ONCE under the message key; the
/// published "encrypted data items" let each eligible user decrypt UP its leaf→root path to the message key. A
/// user not in the graph — or one whose path keys were rotated by a REVOKE (leave) — cannot decrypt. This is
/// "encrypt once to a selectable GROUP, with revocation", NOT a per-recipient re-encrypt loop and NOT a plain
/// broadcast. (Heap node layout: root = 1, children 2i/2i+1, leaves n..2n-1 for n users.)
/// </summary>
public sealed class BroadcastEncryption
{
    private static readonly byte[] WrapAad = System.Text.Encoding.ASCII.GetBytes("broadcast/wrap/v1");
    private static readonly byte[] MsgAad = System.Text.Encoding.ASCII.GetBytes("broadcast/message/v1");

    private readonly Dictionary<int, byte[]> _keys = new();    // node id -> 32-byte key
    private readonly Dictionary<ulong, int> _userLeaf = new(); // user id -> leaf node id

    /// <summary>One published item: the parent-node key wrapped under a child-node key (GB cl.1).</summary>
    public sealed record Item(int Node, int Parent, byte[] WrappedParentKey);

    private BroadcastEncryption() { }
    private static int Parent(int node) => node / 2;
    private static IEnumerable<int> LeafToRoot(int leaf) { for (int i = leaf; i >= 1; i /= 2) yield return i; }

    /// <summary>Build the graph over a power-of-two user set, a fresh random key per node (REQ-BCS-001).</summary>
    public static BroadcastEncryption Build(IReadOnlyList<ulong> userIds)
    {
        int n = userIds.Count;
        if (n == 0 || (n & (n - 1)) != 0) throw new ArgumentException("user count must be a power of two");
        var g = new BroadcastEncryption();
        int total = 2 * n - 1;                                   // nodes 1..2n-1; leaves n..2n-1
        for (int i = 1; i <= total; i++) g._keys[i] = RandomNumberGenerator.GetBytes(32);
        for (int k = 0; k < n; k++) g._userLeaf[userIds[k]] = n + k;
        return g;
    }

    public int UserCount => _userLeaf.Count;

    /// <summary>A copy of a user's leaf key — handed to that user out of band (so only they hold it).</summary>
    public byte[]? UserLeafKey(ulong user) => _userLeaf.TryGetValue(user, out var leaf) && _keys.TryGetValue(leaf, out var k) ? (byte[])k.Clone() : null;

    /// <summary>Every non-root node's key wraps its parent's key (REQ-BCS-003, GB cl.1).</summary>
    public List<Item> EncryptedDataItems()
    {
        var items = new List<Item>();
        foreach (var (node, key) in _keys)
            if (node != 1 && _keys.TryGetValue(Parent(node), out var pk))
                items.Add(new Item(node, Parent(node), Aead.Seal(key, pk, WrapAad)));
        return items;
    }

    /// <summary>Encrypt the message ONCE under the root/message key (REQ-BCS-002).</summary>
    public byte[] EncryptMessage(byte[] plaintext) => Aead.Seal(_keys[1], plaintext, MsgAad);

    /// <summary>Decrypt as <paramref name="user"/> using its leaf key and the published items: unwrap up the
    /// leaf→root path to the message key (REQ-BCS-004). Throws if the user is not eligible / cannot decrypt.</summary>
    public byte[] UserDecrypt(ulong user, byte[] leafKey, IReadOnlyList<Item> items, byte[] sealedMsg)
    {
        if (!_userLeaf.TryGetValue(user, out var leaf)) throw new InvalidOperationException("user not eligible");
        var path = LeafToRoot(leaf).ToList();
        var current = (byte[])leafKey.Clone();
        for (int i = 0; i + 1 < path.Count; i++)
        {
            int child = path[i], parent = path[i + 1];
            var item = items.FirstOrDefault(it => it.Node == child && it.Parent == parent)
                       ?? throw new InvalidOperationException("not eligible (no path item)");
            current = Aead.Open(current, item.WrappedParentKey, WrapAad);   // throws on a wrong/rotated key
        }
        return Aead.Open(current, sealedMsg, MsgAad);
    }

    /// <summary>REVOKE (leave): drop the user's leaf and rotate every key on its old leaf→root path — the keys it
    /// knew — so it can no longer decrypt NEW messages while remaining members still can. Re-publish
    /// EncryptedDataItems() + EncryptMessage() afterwards. (GB §4 leave/rekey.)</summary>
    public void Revoke(ulong user)
    {
        if (!_userLeaf.TryGetValue(user, out var leaf)) return;
        var ancestors = LeafToRoot(leaf).Where(n => n != leaf).ToList();   // parent..root
        _keys.Remove(leaf);                                                // the departed leaf is gone from the graph
        _userLeaf.Remove(user);
        foreach (var node in ancestors) _keys[node] = RandomNumberGenerator.GetBytes(32);   // rotate the path keys
    }
}
