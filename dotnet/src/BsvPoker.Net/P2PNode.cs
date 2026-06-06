using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace BsvPoker.Net;

/// <summary>
/// Pure peer-to-peer gossip transport (NO server). Every node is an equal peer (listener + dialer): a
/// published frame is delivered to the node's own subscribers (echo) AND flooded to every connected
/// peer, which re-floods with per-frame dedup, so it reaches the whole mesh over any connected graph.
/// A serverless DIRECTORY (gossiped table announces) powers the lobby; presence powers discovery.
/// Hardened: connection cap, per-peer inbound rate limit, anti-eviction directory. Frames are
/// newline-delimited JSON {t,d,id} (d = base64). Stdlib only.
/// </summary>
public sealed class P2PNode : IDisposable
{
    private const int MaxFrameBytes = 1 << 20;
    private const int MaxSeen = 100_000;
    private const int MaxDirectory = 10_000;
    private const int MaxPeers = 64;
    private const int RateCapacity = 1000;
    private const int RateRefillPerSec = 500;
    private static readonly TimeSpan EntryTtl = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan Reannounce = TimeSpan.FromSeconds(5);
    private const string DirTopic = " bsvp/dir";
    private const string DirQuery = " bsvp/dir?";
    private const string PresenceTopic = " bsvp/presence";

    private readonly int _port;
    private readonly string _bindHost;
    private TcpListener? _listener;
    private volatile bool _closed;
    private readonly ConcurrentDictionary<Guid, (TcpClient Sock, RateState Rate)> _peers = new();
    private readonly ConcurrentDictionary<string, List<Action<string>>> _subs = new();
    private readonly ConcurrentDictionary<string, byte> _seen = new();
    private readonly ConcurrentQueue<string> _seenOrder = new();
    private readonly ConcurrentDictionary<string, bool> _dialing = new();
    private readonly ConcurrentDictionary<string, (string Name, int Members, DateTime Exp)> _directory = new();
    private readonly ConcurrentDictionary<string, (string Addr, DateTime Exp)> _presence = new();
    private readonly ConcurrentDictionary<string, TableAnnounce> _ownTables = new();
    private readonly ConcurrentDictionary<string, PresenceAnnounce> _ownPresence = new();
    private Timer? _reannounceTimer;
    private readonly object _writeLock = new();

    public sealed record TableAnnounce(string id, string name, int members);
    public sealed record PresenceAnnounce(string playerId, string addr);
    public sealed record PeerAddr(string Host, int Port);
    private sealed record Frame(string t, string d, string id);
    private sealed class RateState { public double Tokens; public long Last; }

    public P2PNode(int port, string bindHost = "0.0.0.0") { _port = port; _bindHost = bindHost; }

    public int BoundPort { get; private set; }
    public int PeerCount => _peers.Count;

    public Task StartAsync(IReadOnlyList<PeerAddr>? peers = null)
    {
        var addr = _bindHost == "0.0.0.0" ? IPAddress.Any : IPAddress.Parse(_bindHost);
        _listener = new TcpListener(addr, _port);
        _listener.Start();
        BoundPort = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _ = AcceptLoop();
        Subscribe(DirTopic, OnDirAnnounce);
        Subscribe(PresenceTopic, OnPresenceAnnounce);
        Subscribe(DirQuery, _ => RepublishOwn());
        if (peers != null) foreach (var p in peers) Dial(p);
        _ = PublishAsync(DirQuery, Array.Empty<byte>());
        return Task.CompletedTask;
    }

    private async Task AcceptLoop()
    {
        var l = _listener!;
        while (!_closed)
        {
            TcpClient s;
            try { s = await l.AcceptTcpClientAsync(); } catch { if (_closed) return; else continue; }
            Adopt(s);
        }
    }

    public void Dial(PeerAddr a)
    {
        var key = $"{a.Host}:{a.Port}";
        if (!_dialing.TryAdd(key, true)) return;
        _ = Task.Run(async () =>
        {
            while (!_closed)
            {
                try
                {
                    var s = new TcpClient();
                    await s.ConnectAsync(a.Host, a.Port);
                    _dialing.TryRemove(key, out _);
                    Adopt(s);
                    _ = PublishAsync(DirQuery, Array.Empty<byte>());
                    return;
                }
                catch { await Task.Delay(200); }
            }
        });
    }

    private void Adopt(TcpClient sock)
    {
        if (_peers.Count >= MaxPeers) { try { sock.Dispose(); } catch { } return; }
        var id = Guid.NewGuid();
        var rate = new RateState { Tokens = RateCapacity, Last = Environment.TickCount64 };
        _peers[id] = (sock, rate);
        sock.NoDelay = true;
        _ = Task.Run(async () =>
        {
            var buf = new StringBuilder();
            var bytes = new byte[8192];
            try
            {
                var stream = sock.GetStream();
                while (!_closed)
                {
                    int n = await stream.ReadAsync(bytes);
                    if (n <= 0) break;
                    buf.Append(Encoding.UTF8.GetString(bytes, 0, n));
                    if (buf.Length > MaxFrameBytes) break;
                    int nl;
                    while ((nl = IndexOf(buf, '\n')) >= 0)
                    {
                        var line = buf.ToString(0, nl);
                        buf.Remove(0, nl + 1);
                        if (line.Length > 0) OnFrame(line, id, rate);
                    }
                }
            }
            catch { }
            finally { _peers.TryRemove(id, out _); try { sock.Dispose(); } catch { } }
        });
    }

    private static int IndexOf(StringBuilder sb, char c) { for (int i = 0; i < sb.Length; i++) if (sb[i] == c) return i; return -1; }

    private bool Allow(RateState r)
    {
        long now = Environment.TickCount64;
        r.Tokens = Math.Min(RateCapacity, r.Tokens + (now - r.Last) / 1000.0 * RateRefillPerSec);
        r.Last = now;
        if (r.Tokens < 1) return false;
        r.Tokens -= 1; return true;
    }

    private void OnFrame(string line, Guid from, RateState rate)
    {
        if (!Allow(rate)) return;
        Frame? f;
        try { f = JsonSerializer.Deserialize<Frame>(line); if (f?.t == null || f.d == null || f.id == null) return; } catch { return; }
        if (MarkSeen(f.id)) return;
        DeliverLocal(f);
        Flood(line, from);
    }

    private bool MarkSeen(string id)
    {
        if (!_seen.TryAdd(id, 1)) return true;
        _seenOrder.Enqueue(id);
        while (_seenOrder.Count > MaxSeen && _seenOrder.TryDequeue(out var old)) _seen.TryRemove(old, out _);
        return false;
    }

    private void DeliverLocal(Frame f)
    {
        if (!_subs.TryGetValue(f.t, out var set)) return;
        string text; try { text = Encoding.UTF8.GetString(Convert.FromBase64String(f.d)); } catch { return; }
        Action<string>[] cbs; lock (set) cbs = set.ToArray();
        foreach (var cb in cbs) { try { cb(text); } catch { } }
    }

    private void Flood(string line, Guid? except = null)
    {
        var payload = Encoding.UTF8.GetBytes(line + "\n");
        foreach (var kv in _peers)
        {
            if (except is { } ex && kv.Key == ex) continue;
            try { var s = kv.Value.Sock.GetStream(); lock (_writeLock) s.Write(payload, 0, payload.Length); } catch { }
        }
    }

    private void OnDirAnnounce(string text)
    {
        try
        {
            var a = JsonSerializer.Deserialize<TableAnnounce>(text);
            if (a == null || string.IsNullOrEmpty(a.id) || a.name == null) return;
            if (!_directory.ContainsKey(a.id) && _directory.Count >= MaxDirectory) { var oldest = _directory.Keys.FirstOrDefault(); if (oldest != null) _directory.TryRemove(oldest, out _); }
            _directory[a.id] = (a.name, a.members < 0 ? 0 : a.members, DateTime.UtcNow + EntryTtl);
        }
        catch { }
    }

    private void OnPresenceAnnounce(string text)
    {
        try
        {
            var p = JsonSerializer.Deserialize<PresenceAnnounce>(text);
            if (p == null || string.IsNullOrEmpty(p.playerId) || string.IsNullOrEmpty(p.addr)) return;
            if (!_presence.ContainsKey(p.playerId) && _presence.Count >= MaxDirectory) { var oldest = _presence.Keys.FirstOrDefault(); if (oldest != null) _presence.TryRemove(oldest, out _); }
            _presence[p.playerId] = (p.addr, DateTime.UtcNow + EntryTtl);
        }
        catch { }
    }

    private void RepublishOwn()
    {
        foreach (var a in _ownTables.Values) _ = PublishAsync(DirTopic, Encoding.UTF8.GetBytes(JsonSerializer.Serialize(a)));
        foreach (var p in _ownPresence.Values) _ = PublishAsync(PresenceTopic, Encoding.UTF8.GetBytes(JsonSerializer.Serialize(p)));
    }

    private void EnsureReannounce() { if (_reannounceTimer == null && !_closed) _reannounceTimer = new Timer(_ => RepublishOwn(), null, Reannounce, Reannounce); }

    public Task HeartbeatAsync(string playerId, string addr)
    {
        var a = new PresenceAnnounce(playerId, addr);
        _ownPresence[playerId] = a; OnPresenceAnnounce(JsonSerializer.Serialize(a)); EnsureReannounce();
        return PublishAsync(PresenceTopic, Encoding.UTF8.GetBytes(JsonSerializer.Serialize(a)));
    }

    public IReadOnlyList<PresenceAnnounce> ListPresence()
    {
        var now = DateTime.UtcNow; var outp = new List<PresenceAnnounce>();
        foreach (var kv in _presence.ToArray()) { if (kv.Value.Exp <= now) { _presence.TryRemove(kv.Key, out _); continue; } outp.Add(new PresenceAnnounce(kv.Key, kv.Value.Addr)); }
        return outp;
    }

    public Task<TableAnnounce> CreateTableAsync(string id, string name)
    {
        var a = new TableAnnounce(id, name, 1);
        _ownTables[id] = a; OnDirAnnounce(JsonSerializer.Serialize(a)); EnsureReannounce();
        _ = PublishAsync(DirTopic, Encoding.UTF8.GetBytes(JsonSerializer.Serialize(a)));
        return Task.FromResult(a);
    }

    public IReadOnlyList<TableAnnounce> ListTables()
    {
        var now = DateTime.UtcNow; var outp = new List<TableAnnounce>();
        foreach (var kv in _directory.ToArray()) { if (kv.Value.Exp <= now) { _directory.TryRemove(kv.Key, out _); continue; } outp.Add(new TableAnnounce(kv.Key, kv.Value.Name, kv.Value.Members)); }
        return outp;
    }

    public Action Subscribe(string tableId, Action<string> onEvent)
    {
        var set = _subs.GetOrAdd(tableId, _ => new List<Action<string>>());
        lock (set) set.Add(onEvent);
        return () => { lock (set) set.Remove(onEvent); };
    }

    public Task<int> PublishAsync(string tableId, byte[] payload)
    {
        var f = new Frame(tableId, Convert.ToBase64String(payload), RandomId());
        MarkSeen(f.id); DeliverLocal(f); Flood(JsonSerializer.Serialize(f));
        return Task.FromResult(_peers.Count);
    }

    private static string RandomId() { Span<byte> b = stackalloc byte[12]; RandomNumberGenerator.Fill(b); return Convert.ToHexString(b).ToLowerInvariant(); }

    public void Dispose()
    {
        _closed = true; _reannounceTimer?.Dispose();
        try { _listener?.Stop(); } catch { }
        foreach (var kv in _peers) { try { kv.Value.Sock.Dispose(); } catch { } }
        _peers.Clear();
    }
}
