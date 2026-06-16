using System.Text;
using System.Text.Json;
using Microsoft.JSInterop;
using BsvPoker.Net;

namespace BsvPoker.Web.Transport;

/// <summary>
/// A browser <see cref="IGameTransport"/> over the BroadcastChannel API — local, server-less P2P that connects every
/// open tab on this origin. The SAME game engines (NetGame / NetBlackjack) that run over the desktop TCP P2PNode run
/// over this with no change: publish floods the channel (deduped by frame id, re-flooded for catch-up exactly like
/// the mesh), and subscribers receive the decoded message text. Cross-machine play is a future WebRTC transport.
/// </summary>
public sealed class BroadcastChannelTransport : IGameTransport, IAsyncDisposable
{
    private const string Channel = "bsvpoker-mesh";
    private readonly IJSObjectReference _module;
    private readonly DotNetObjectReference<BroadcastChannelTransport> _self;
    private readonly Dictionary<string, List<Action<string>>> _subs = new();
    private readonly HashSet<string> _seen = new();
    private readonly Queue<string> _seenOrder = new();
    private readonly object _gate = new();

    private BroadcastChannelTransport(IJSObjectReference module)
    {
        _module = module;
        _self = DotNetObjectReference.Create(this);
    }

    public static async Task<BroadcastChannelTransport> CreateAsync(IJSRuntime js)
    {
        var module = await js.InvokeAsync<IJSObjectReference>("import", "./js/bc-transport.js");
        var t = new BroadcastChannelTransport(module);
        await module.InvokeVoidAsync("open", Channel, t._self);
        return t;
    }

    public Action Subscribe(string tableId, Action<string> onEvent)
    {
        lock (_gate) { if (!_subs.TryGetValue(tableId, out var l)) { l = new(); _subs[tableId] = l; } l.Add(onEvent); }
        return () => { lock (_gate) { if (_subs.TryGetValue(tableId, out var l)) l.Remove(onEvent); } };
    }

    public Task<int> PublishAsync(string tableId, byte[] payload, string? id = null)
    {
        var fid = id ?? Guid.NewGuid().ToString("N");
        bool firstHere = MarkSeen(fid);
        var text = Encoding.UTF8.GetString(payload);
        if (firstHere) DeliverLocal(tableId, text);                    // echo to our own subscribers once
        var msg = JsonSerializer.Serialize(new { tableId, id = fid, b64 = Convert.ToBase64String(payload) });
        _ = _module.InvokeVoidAsync("post", Channel, msg);             // to every other tab (they dedup on the id)
        return Task.FromResult(1);
    }

    [JSInvokable]
    public void OnMeshMessage(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var r = doc.RootElement;
            var tableId = r.GetProperty("tableId").GetString()!;
            var fid = r.GetProperty("id").GetString()!;
            if (!MarkSeen(fid)) return;                                // already seen (a re-flood, or our own echo)
            var payload = Convert.FromBase64String(r.GetProperty("b64").GetString()!);
            DeliverLocal(tableId, Encoding.UTF8.GetString(payload));
        }
        catch { }
    }

    // returns true the FIRST time an id is seen (bounded LRU so a long session can't grow without limit)
    private bool MarkSeen(string id)
    {
        lock (_gate)
        {
            if (!_seen.Add(id)) return false;
            _seenOrder.Enqueue(id);
            while (_seenOrder.Count > 50_000 && _seenOrder.TryDequeue(out var old)) _seen.Remove(old);
            return true;
        }
    }

    private void DeliverLocal(string tableId, string text)
    {
        Action<string>[] cbs;
        lock (_gate) { if (!_subs.TryGetValue(tableId, out var l)) return; cbs = l.ToArray(); }
        foreach (var cb in cbs) { try { cb(text); } catch { } }
    }

    public async ValueTask DisposeAsync()
    {
        try { await _module.InvokeVoidAsync("close", Channel); } catch { }
        _self.Dispose();
        try { await _module.DisposeAsync(); } catch { }
    }
}
