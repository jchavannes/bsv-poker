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
    private const int MaxFrameBytes = 1 << 20;        // hard per-frame BYTE cap (byte-accurate, not chars)
    private const int MaxTopicBytes = 256;            // a topic string may not exceed this many UTF-8 bytes
    private const int MaxPayloadBytes = 1 << 20;      // decoded payload byte cap
    private const int MaxSeen = 100_000;
    private const int MaxDirectory = 10_000;
    private const int MaxPeers = 64;
    private const double RateCapacityBytes = 8 << 20;     // token bucket measured in BYTES (8 MiB burst)
    private const double RateRefillBytesPerSec = 4 << 20; // refilled at 4 MiB/s
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

    // SECURITY: default to LOOPBACK. The node listens only on 127.0.0.1 until the user explicitly opts in
    // to LAN/online play via EnableLan(). Outbound dials work regardless, so a player can still join others.
    public P2PNode(int port, string bindHost = "127.0.0.1") { _port = port; _bindHost = bindHost; }
    public bool LanEnabled { get; private set; }

    public int BoundPort { get; private set; }
    public int PeerCount => _peers.Count;
    public long DroppedFrames { get; private set; }
    public string? LastDrop { get; private set; }
    private void Drop(string why) { DroppedFrames++; LastDrop = why; }

    public Task StartAsync(IReadOnlyList<PeerAddr>? peers = null)
    {
        var addr = _bindHost == "0.0.0.0" ? IPAddress.Any : IPAddress.Parse(_bindHost);
        if (_bindHost == "0.0.0.0") LanEnabled = true;
        _listener = new TcpListener(addr, _port);
        _listener.Start();
        BoundPort = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _ = AcceptLoop(_listener);
        Subscribe(DirTopic, OnDirAnnounce);
        Subscribe(PresenceTopic, OnPresenceAnnounce);
        Subscribe(DirQuery, _ => RepublishOwn());
        if (peers != null) foreach (var p in peers) Dial(p);
        _ = PublishAsync(DirQuery, Array.Empty<byte>());
        return Task.CompletedTask;
    }

    private async Task AcceptLoop(TcpListener l)
    {
        while (!_closed)
        {
            TcpClient s;
            try { s = await l.AcceptTcpClientAsync(); }
            catch { if (_closed || !ReferenceEquals(l, _listener)) return; else { await Task.Delay(50); continue; } }
            Adopt(s);
        }
    }

    /// <summary>
    /// Opt in to LAN/online play: rebind the listener from loopback to all interfaces (same port) so other
    /// machines can connect inbound. No-op if already enabled. Existing peer connections are unaffected.
    /// </summary>
    public void EnableLan()
    {
        if (_closed || LanEnabled) return;
        try
        {
            var old = _listener;
            var lan = new TcpListener(IPAddress.Any, BoundPort);
            lan.Start();
            _listener = lan;                 // the old accept loop exits (its listener != _listener)
            _ = AcceptLoop(lan);
            try { old?.Stop(); } catch { }
            LanEnabled = true;
        }
        catch { /* port unavailable for all-interfaces bind; stay loopback-only */ }
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
        var rate = new RateState { Tokens = RateCapacityBytes, Last = Environment.TickCount64 };
        _peers[id] = (sock, rate);
        sock.NoDelay = true;
        _ = Task.Run(async () =>
        {
            // BYTE-accurate framing: accumulate raw bytes, split on the '\n' byte, and hard-cap the byte
            // length of any single frame. An oversize frame is dropped and we resync to the next newline
            // rather than tearing down the connection.
            var acc = new MemoryStream();
            var bytes = new byte[8192];
            bool skipping = false;
            try
            {
                var stream = sock.GetStream();
                while (!_closed)
                {
                    int n = await stream.ReadAsync(bytes);
                    if (n <= 0) break;
                    for (int i = 0; i < n; i++)
                    {
                        byte ch = bytes[i];
                        if (ch == (byte)'\n')
                        {
                            if (skipping) { skipping = false; acc.SetLength(0); continue; }
                            if (acc.Length > 0) { OnFrame(Encoding.UTF8.GetString(acc.ToArray()), id, rate, (int)acc.Length); acc.SetLength(0); }
                        }
                        else if (skipping) { /* discard until newline */ }
                        else
                        {
                            acc.WriteByte(ch);
                            if (acc.Length > MaxFrameBytes) { Drop("oversize frame"); skipping = true; acc.SetLength(0); }
                        }
                    }
                }
            }
            catch { }
            finally { _peers.TryRemove(id, out _); try { sock.Dispose(); } catch { } }
        });
    }

    // token bucket measured in BYTES, so a few large frames cost as much as many small ones (byte-cost
    // rate limiting, not frame-count) — an attacker cannot bypass the limit with big frames.
    private bool Allow(RateState r, int costBytes)
    {
        long now = Environment.TickCount64;
        r.Tokens = Math.Min(RateCapacityBytes, r.Tokens + (now - r.Last) / 1000.0 * RateRefillBytesPerSec);
        r.Last = now;
        if (r.Tokens < costBytes) return false;
        r.Tokens -= costBytes; return true;
    }

    private void OnFrame(string line, Guid from, RateState rate, int costBytes)
    {
        if (!Allow(rate, costBytes)) { Drop("rate limit"); return; }
        Frame? f;
        try { f = JsonSerializer.Deserialize<Frame>(line); if (f?.t == null || f.d == null || f.id == null) { Drop("malformed frame"); return; } } catch { Drop("parse error"); return; }
        if (Encoding.UTF8.GetByteCount(f.t) > MaxTopicBytes) { Drop("topic too long"); return; }
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
        string text;
        try { var raw = Convert.FromBase64String(f.d); if (raw.Length > MaxPayloadBytes) { Drop("payload too large"); return; } text = Encoding.UTF8.GetString(raw); }
        catch { Drop("bad base64"); return; }
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
