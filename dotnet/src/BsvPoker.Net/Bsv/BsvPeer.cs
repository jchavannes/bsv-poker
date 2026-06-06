using System.Net.Sockets;
using System.Security.Cryptography;

namespace BsvPoker.Net.Bsv;

/// <summary>
/// A connection to one BSV network peer: performs the version/verack handshake over the real wire
/// protocol, answers ping with pong, and exposes a stream of received messages. This is the unit that
/// makes the client a genuine participant on the BSV network (used by both outbound dials and inbound
/// accepts). Frame parsing is strict (see <see cref="BsvMessage"/>); a malformed peer is dropped.
/// </summary>
public sealed class BsvPeer : IDisposable
{
    private readonly NetworkParams _net;
    private readonly TcpClient _sock;
    private readonly NetworkStream _stream;
    private readonly ulong _nonce = (ulong)Random.Shared.NextInt64() ^ (ulong)Environment.TickCount64;
    private readonly object _writeLock = new();
    private volatile bool _closed;

    public bool Handshaked { get; private set; }
    public BsvVersion.Info? RemoteVersion { get; private set; }
    public event Action<BsvMessage>? OnMessage;

    public BsvPeer(NetworkParams net, TcpClient sock)
    {
        _net = net; _sock = sock; _sock.NoDelay = true; _stream = sock.GetStream();
    }

    public void Send(string command, byte[] payload)
    {
        var bytes = new BsvMessage(command, payload).Encode(_net.Magic);
        lock (_writeLock) { _stream.Write(bytes, 0, bytes.Length); }
    }

    /// <summary>Run the handshake and then the receive loop until closed. Completes when both sides verack.</summary>
    public async Task HandshakeAsync(int startHeight = 0, int timeoutMs = 15000, CancellationToken ct = default)
    {
        Send("version", BsvVersion.Build(startHeight, _nonce));
        bool gotVersion = false, gotVerack = false;
        var acc = new List<byte>();
        var buf = new byte[16384];
        var deadline = Environment.TickCount64 + timeoutMs;
        while (!_closed && !ct.IsCancellationRequested)
        {
            using var to = new CancellationTokenSource(Math.Max(1, (int)(deadline - Environment.TickCount64)));
            using var link = CancellationTokenSource.CreateLinkedTokenSource(ct, to.Token);
            int n;
            try { n = await _stream.ReadAsync(buf, link.Token); }
            catch { break; }
            if (n <= 0) break;
            acc.AddRange(buf.AsSpan(0, n).ToArray());
            while (true)
            {
                var status = BsvMessage.TryDecode(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(acc), _net.Magic, out var msg, out int consumed);
                if (status == BsvMessage.DecodeStatus.NeedMore) break;
                if (status != BsvMessage.DecodeStatus.Ok) { acc.Clear(); break; } // bad magic/checksum/oversize → drop buffer
                acc.RemoveRange(0, consumed);
                switch (msg!.Command)
                {
                    case "version":
                        try { RemoteVersion = BsvVersion.Parse(msg.Payload); } catch { }
                        Send("verack", Array.Empty<byte>());
                        gotVersion = true;
                        break;
                    case "verack": gotVerack = true; break;
                    case "ping": Send("pong", msg.Payload); break; // echo the nonce
                    default: break;
                }
                OnMessage?.Invoke(msg);
                if (gotVersion && gotVerack && !Handshaked)
                {
                    Handshaked = true;
                    _ = ReceiveLoopAsync(acc, ct); // continue serving the peer (ping/pong, relay) after handshake
                    return;
                }
            }
            if (Environment.TickCount64 >= deadline) break;
        }
        if (!Handshaked) throw new IOException("handshake did not complete");
    }

    private async Task ReceiveLoopAsync(List<byte> acc, CancellationToken ct)
    {
        var buf = new byte[16384];
        try
        {
            while (!_closed && !ct.IsCancellationRequested)
            {
                while (true)
                {
                    var status = BsvMessage.TryDecode(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(acc), _net.Magic, out var msg, out int consumed);
                    if (status == BsvMessage.DecodeStatus.NeedMore) break;
                    if (status != BsvMessage.DecodeStatus.Ok) { acc.Clear(); break; }
                    acc.RemoveRange(0, consumed);
                    if (msg!.Command == "ping") Send("pong", msg.Payload);
                    OnMessage?.Invoke(msg);
                }
                int n = await _stream.ReadAsync(buf, ct);
                if (n <= 0) break;
                acc.AddRange(buf.AsSpan(0, n).ToArray());
            }
        }
        catch { }
        finally { Dispose(); }
    }

    public void Dispose() { _closed = true; try { _stream.Dispose(); } catch { } try { _sock.Dispose(); } catch { } }
}
