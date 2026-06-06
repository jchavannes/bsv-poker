using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using BsvPoker.Crypto;

namespace BsvPoker.Net;

/// <summary>
/// Encrypted messaging over the P2P mesh — direct messages AND group chats (the Telegram/WhatsApp-style
/// functions), with NO server. EVERY message is encrypted with a FRESH ephemeral ECDH key PER RECIPIENT
/// (no key reuse ever): for each member, shared = ECDH(eph_priv, member_pub) → HKDF → AES-256-GCM. A DM
/// is a 2-member conversation; a group is N members. Peers are discovered from mesh presence; you can
/// also add a contact by pubkey. Conversation topics are derived so all members subscribe to the same one.
/// </summary>
public sealed class ChatService
{
    public sealed record ChatMessage(string FromHex, string Text, string TimeUtc);
    public sealed class Conversation
    {
        public string Id = "";
        public string Title = "";
        public bool IsGroup;
        public List<string> MembersHex = new();   // pubkeys (hex) of all members (incl me)
        public string Topic = "";
        public List<ChatMessage> Messages = new();
    }

    private static readonly byte[] Salt = Encoding.ASCII.GetBytes("bsvpoker-chat-salt-v1");
    private static readonly byte[] Info = Encoding.ASCII.GetBytes("bsvpoker-chat-key-v1");

    private readonly P2PNode _node;
    private readonly byte[] _priv;
    private readonly string _myHex;
    private readonly ConcurrentDictionary<string, Conversation> _convs = new();
    private readonly ConcurrentDictionary<string, Action> _unsubs = new();

    public string MyHex => _myHex;
    public event Action<Conversation>? OnUpdate;
    private readonly string? _path;
    private static readonly JsonSerializerOptions JsonOpts = new() { IncludeFields = true, WriteIndented = true };
    private readonly object _saveLock = new();

    public ChatService(P2PNode node, byte[] myPriv, byte[] myPub, string? dataDir = null)
    {
        _node = node; _priv = myPriv; _myHex = Convert.ToHexString(myPub).ToLowerInvariant();
        if (dataDir != null)
        {
            System.IO.Directory.CreateDirectory(dataDir);
            _path = System.IO.Path.Combine(dataDir, "chat.json");
            Load();
        }
    }

    private void Load()
    {
        try
        {
            if (_path != null && System.IO.File.Exists(_path))
            {
                var list = JsonSerializer.Deserialize<List<Conversation>>(System.IO.File.ReadAllText(_path), JsonOpts) ?? new();
                foreach (var c in list) { _convs[c.Id] = c; Subscribe(c); } // re-subscribe persisted conversations
            }
        }
        catch { /* corrupt → start fresh */ }
    }

    private void Save()
    {
        if (_path == null) return;
        lock (_saveLock)
        {
            try { var tmp = _path + ".tmp"; System.IO.File.WriteAllText(tmp, JsonSerializer.Serialize(_convs.Values.ToList(), JsonOpts)); System.IO.File.Move(tmp, _path, true); } catch { }
        }
    }

    public IReadOnlyList<Conversation> Conversations => _convs.Values.OrderBy(c => c.Title).ToList();

    /// <summary>Peers currently visible on the mesh (presence) that you can DM, excluding yourself.</summary>
    public IReadOnlyList<string> OnlinePeers() => _node.ListPresence().Select(p => p.playerId.ToLowerInvariant()).Where(p => p != _myHex && p.Length == 66).Distinct().ToList();

    private static string DmTopic(string a, string b)
    {
        var (x, y) = string.CompareOrdinal(a, b) <= 0 ? (a, b) : (b, a);
        return "chat/dm/" + Convert.ToHexString(Hashes.Sha256(Encoding.ASCII.GetBytes(x + "|" + y)))[..24].ToLowerInvariant();
    }

    public Conversation OpenDm(string peerPubHex, string title)
    {
        peerPubHex = peerPubHex.ToLowerInvariant();
        var topic = DmTopic(_myHex, peerPubHex);
        bool fresh = !_convs.ContainsKey(topic);
        var c = _convs.GetOrAdd(topic, _ => Subscribe(new Conversation { Id = topic, Title = title, IsGroup = false, MembersHex = { _myHex, peerPubHex }, Topic = topic }));
        if (fresh) Save();
        return c;
    }

    public Conversation CreateGroup(string title, IEnumerable<string> memberPubHex)
    {
        var members = new List<string> { _myHex };
        members.AddRange(memberPubHex.Select(m => m.ToLowerInvariant()).Where(m => m != _myHex));
        var topic = "chat/grp/" + Convert.ToHexString(Hashes.Sha256(Encoding.ASCII.GetBytes(string.Join("|", members.OrderBy(x => x)) + title)))[..24].ToLowerInvariant();
        return _convs.GetOrAdd(topic, _ => Subscribe(new Conversation { Id = topic, Title = title, IsGroup = true, MembersHex = members, Topic = topic }));
    }

    private Conversation Subscribe(Conversation c)
    {
        _unsubs[c.Id] = _node.Subscribe(c.Topic, text => OnFrame(c, text));
        return c;
    }

    public void Send(string convId, string text)
    {
        if (!_convs.TryGetValue(convId, out var c)) return;
        var time = DateTime.UtcNow.ToString("u");
        foreach (var member in c.MembersHex.Where(m => m != _myHex))
        {
            var pub = Convert.FromHexString(member);
            var (ephPriv, ephPub) = Secp256k1.GenerateKeyPair();             // FRESH per recipient per message — no key reuse
            var key = Aead.Hkdf(Concat(Secp256k1.Ecdh(ephPriv, pub), ephPub), Salt, Info);
            var ct = Aead.Seal(key, Encoding.UTF8.GetBytes(text), pub);
            var frame = JsonSerializer.Serialize(new { t = "msg", from = _myHex, to = member, eph = Convert.ToHexString(ephPub).ToLowerInvariant(), ct = Convert.ToHexString(ct).ToLowerInvariant(), time });
            _ = _node.PublishAsync(c.Topic, Encoding.UTF8.GetBytes(frame));
        }
        c.Messages.Add(new ChatMessage(_myHex, text, time)); // show my own immediately
        Save();
        OnUpdate?.Invoke(c);
    }

    private void OnFrame(Conversation c, string text)
    {
        try
        {
            using var doc = JsonDocument.Parse(text);
            var r = doc.RootElement;
            if (r.GetProperty("t").GetString() != "msg") return;
            if (r.GetProperty("to").GetString() != _myHex) return;           // only the copy addressed to me
            var from = r.GetProperty("from").GetString()!;
            if (from == _myHex) return;
            var ephPub = Convert.FromHexString(r.GetProperty("eph").GetString()!);
            var ct = Convert.FromHexString(r.GetProperty("ct").GetString()!);
            var myPub = Secp256k1.PublicKeyCompressed(_priv);
            var key = Aead.Hkdf(Concat(Secp256k1.Ecdh(_priv, ephPub), ephPub), Salt, Info);
            var pt = Encoding.UTF8.GetString(Aead.Open(key, ct, myPub));
            c.Messages.Add(new ChatMessage(from, pt, r.GetProperty("time").GetString() ?? ""));
            Save();
            OnUpdate?.Invoke(c);
        }
        catch { /* not for me / undecryptable */ }
    }

    private static byte[] Concat(byte[] a, byte[] b) { var o = new byte[a.Length + b.Length]; a.CopyTo(o, 0); b.CopyTo(o, a.Length); return o; }
}
