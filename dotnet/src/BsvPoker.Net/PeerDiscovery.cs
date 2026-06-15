using System.Net;
using System.Net.Sockets;

namespace BsvPoker.Net;

/// <summary>
/// ZERO-CONFIG automatic peer discovery for <see cref="P2PNode"/> — "open your node and it just connects",
/// the way Bitcoin does, with NO manual host:port entry and NO UDP. Everything is TCP. You never type an IP.
///
/// Three TCP mechanisms feed the node's dialer:
///  1. LOCAL RENDEZVOUS (same machine, instant): every running node keeps its <c>host port</c> in a well-known
///     temp file; every node reads it and DIALS each address. Any number of instances on one machine connect
///     immediately.
///  2. SAME-NETWORK SWEEP (same LAN, within seconds): every node listens on a WELL-KNOWN TCP port and sweeps its
///     local /24 subnet, TCP-probing each host on the well-known port (plus a few fallbacks for multi-instance
///     machines). Any host that accepts is dialled. So two players on the same network find each other in a few
///     seconds with no configuration and no broadcast — pure TCP connect attempts.
///  3. ON-CHAIN SEED REGISTRY (anywhere on earth, within ~15s): live node addresses are published to a well-known
///     BSV address (see <c>NodeSeedRegistry</c>); the app hands this object the seed list it read from the chain
///     and every seed is dialled. Global, serverless, identical everywhere.
///
/// Discovery only ADDS dials; <see cref="P2PNode.Dial"/> de-dups (ignores an address already connected/dialling).
/// </summary>
public sealed class PeerDiscovery : IDisposable
{
    /// <summary>The well-known TCP port the lobby/table node listens on, so same-network peers find it without any
    /// configuration. A node whose well-known port is taken (a 2nd instance on one machine) falls back to an
    /// ephemeral port and is still found via the local rendezvous file and the on-chain registry.</summary>
    public const int WellKnownPort = 47650;

    // The ports the subnet sweep probes on each host: the well-known port and a few above it (so two instances on
    // ONE other machine — which fall back to ephemeral ports — are still reachable when they ALSO publish via the
    // rendezvous/registry; the sweep's job is to find the FIRST node on each host at the well-known port).
    private static readonly int[] SweepPorts = { WellKnownPort, WellKnownPort + 1, WellKnownPort + 2 };
    private const int SweepProbeTimeoutMs = 300;

    private readonly P2PNode _node;
    private readonly string _advertiseHost;     // the address OTHER nodes should dial to reach us (LAN IP or 127.0.0.1)
    private readonly string _rendezvousPath;
    private System.Threading.Timer? _timer;
    private volatile bool _closed;
    private int _sweepRunning;                  // guard so only one subnet sweep runs at a time
    private readonly bool _subnetSweep;         // probe the local /24 (off in isolated/test mode)
    private string _selfTag = "";
    private string _loopbackTag = "";

    // On-chain seed addresses to dial, refreshed by the app from the registry. Guarded by its own lock.
    private readonly object _seedLock = new();
    private List<(string Host, int Port)> _onChainSeeds = new();

    /// <summary>Create peer discovery. <paramref name="rendezvousPath"/> overrides the shared local rendezvous file
    /// (tests pass a unique path so a real running instance can't pollute them); <paramref name="subnetSweep"/>
    /// can disable the LAN /24 probe (tests turn it off for the same isolation reason). Production uses the defaults.</summary>
    public PeerDiscovery(P2PNode node, string advertiseHost, string? rendezvousPath = null, bool subnetSweep = true)
    {
        _node = node; _advertiseHost = advertiseHost; _subnetSweep = subnetSweep;
        _rendezvousPath = rendezvousPath ?? Path.Combine(Path.GetTempPath(), "bsvpoker-peers.txt");
    }

    public void Start()
    {
        _selfTag = $"{_advertiseHost} {_node.BoundPort}";
        // ALSO advertise a loopback line so two instances on THIS machine connect via 127.0.0.1 even if the LAN
        // bind was refused — same-machine discovery must never depend on anything external.
        _loopbackTag = $"127.0.0.1 {_node.BoundPort}";
        WriteRendezvous();
        _timer = new System.Threading.Timer(_ => Tick(), null, 0, 1000);   // re-advertise + dial every second
    }

    /// <summary>Supply the latest LIVE node addresses read from the on-chain seed registry; each is dialled (TCP).
    /// Called by the app after it scans the well-known registry address. Expired records are excluded by the
    /// registry reader, so everything passed here is current.</summary>
    public void SetOnChainSeeds(IEnumerable<(string Host, int Port)> seeds)
    {
        lock (_seedLock) _onChainSeeds = seeds.ToList();
    }

    /// <summary>The host:port THIS node advertises to the world (for publishing into the on-chain registry).</summary>
    public string SelfEndpoint => $"{_advertiseHost}:{_node.BoundPort}";

    private long _lastSweep;
    private void Tick()
    {
        if (_closed) return;
        try { WriteRendezvous(); } catch { }
        try { DialRendezvousPeers(); } catch { }
        try { DialOnChainSeeds(); } catch { }
        // sweep the local subnet every ~4s (a full /24 probe is cheap and parallel); finds same-network players
        // within a few seconds with no configuration. Skipped while a previous sweep is still running.
        long now = Environment.TickCount64;
        if (_subnetSweep && now - _lastSweep > 4000) { _lastSweep = now; _ = SweepSubnetAsync(); }
    }

    // TCP subnet sweep: probe every host on the local /24 at the well-known port(s); dial any that accept. Pure
    // TCP connect attempts — no broadcast, no config. This is what makes "open it and find players in 5s" work.
    private async Task SweepSubnetAsync()
    {
        if (Interlocked.Exchange(ref _sweepRunning, 1) == 1) return;
        try
        {
            foreach (var prefix in LocalSubnetPrefixes())
            {
                var tasks = new List<Task>();
                for (int host = 1; host <= 254; host++)
                {
                    var ip = prefix + host;
                    foreach (var port in SweepPorts)
                    {
                        if (ip == _advertiseHost && port == _node.BoundPort) continue; // that's us
                        tasks.Add(ProbeAndDial(ip, port));
                    }
                }
                await Task.WhenAll(tasks);
            }
        }
        catch { }
        finally { Interlocked.Exchange(ref _sweepRunning, 0); }
    }

    private async Task ProbeAndDial(string host, int port)
    {
        try
        {
            using var c = new TcpClient();
            var connect = c.ConnectAsync(host, port);
            var done = await Task.WhenAny(connect, Task.Delay(SweepProbeTimeoutMs));
            if (done == connect && c.Connected)
            {
                c.Close();                               // we only needed to know it accepts; let the node make the real connection
                _node.Dial(new P2PNode.PeerAddr(host, port));   // de-duped by Dial()
            }
        }
        catch { }
    }

    // The "a.b.c." prefixes of every local IPv4 /24 we are on (so the sweep covers each network the host joins).
    private static IEnumerable<string> LocalSubnetPrefixes()
    {
        var outp = new HashSet<string>();
        try
        {
            foreach (var ip in Dns.GetHostAddresses(Dns.GetHostName()))
            {
                if (ip.AddressFamily != AddressFamily.InterNetwork || IPAddress.IsLoopback(ip)) continue;
                var parts = ip.ToString().Split('.');
                if (parts.Length == 4) outp.Add($"{parts[0]}.{parts[1]}.{parts[2]}.");
            }
        }
        catch { }
        return outp;
    }

    private void DialOnChainSeeds()
    {
        List<(string Host, int Port)> seeds;
        lock (_seedLock) seeds = _onChainSeeds.ToList();
        foreach (var (host, port) in seeds)
        {
            if (port == _node.BoundPort && (host == _advertiseHost || host == "127.0.0.1")) continue; // that's us
            _node.Dial(new P2PNode.PeerAddr(host, port));   // TCP; Dial() de-dups already-connected/already-dialling
        }
    }

    // ---- local rendezvous (same machine), file-based, TCP dials ----
    private static readonly object FileLock = new();

    private void WriteRendezvous()
    {
        lock (FileLock)
        {
            var lines = ReadLines();
            if (!lines.Contains(_selfTag)) lines.Add(_selfTag);
            if (!lines.Contains(_loopbackTag)) lines.Add(_loopbackTag);
            try { File.WriteAllLines(_rendezvousPath, lines.Distinct()); } catch { }
        }
    }

    private List<string> ReadLines()
    {
        try { return File.Exists(_rendezvousPath) ? File.ReadAllLines(_rendezvousPath).Where(l => l.Trim().Length > 0).ToList() : new(); }
        catch { return new(); }
    }

    private void DialRendezvousPeers()
    {
        foreach (var line in ReadLines())
        {
            if (line == _selfTag || line == _loopbackTag) continue;
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2 || !int.TryParse(parts[1], out var port)) continue;
            if (port == _node.BoundPort) continue;             // unique ephemeral port per node ⇒ same port = us
            _node.Dial(new P2PNode.PeerAddr(parts[0], port));
        }
    }

    public void Dispose()
    {
        _closed = true;
        _timer?.Dispose();
        lock (FileLock)
        {
            try { var lines = ReadLines().Where(l => l != _selfTag && l != _loopbackTag).ToList(); File.WriteAllLines(_rendezvousPath, lines); } catch { }
        }
    }
}
