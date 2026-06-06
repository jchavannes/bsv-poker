using System.IO;

namespace BsvPoker.App;

/// <summary>
/// Serverless LOCAL peer discovery for instances on the same machine. Each running app writes its own
/// loopback listen port (with a timestamp) into a shared registry file and reads the others, so two copies
/// started on one machine connect to each other automatically — no server, no manual IP:port. (Cross-
/// machine play still uses the lobby's explicit Connect.) Stale entries are pruned.
/// </summary>
public static class LocalDiscovery
{
    private static readonly string Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "bsvpoker-localpeers.tsv");
    private const int StaleSeconds = 20;

    /// <summary>Record (refresh) this instance's port and prune stale entries.</summary>
    public static void Register(int myPort)
    {
        for (int attempt = 0; attempt < 6; attempt++)
        {
            try
            {
                long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                var map = Read();
                map[myPort] = now;
                var live = map.Where(kv => now - kv.Value <= StaleSeconds).Select(kv => $"{kv.Key}\t{kv.Value}");
                var tmp = Path + "." + myPort + ".tmp";
                File.WriteAllLines(tmp, live);
                File.Move(tmp, Path, true);
                return;
            }
            catch { Thread.Sleep(25); }
        }
    }

    /// <summary>The loopback ports of other live instances on this machine.</summary>
    public static IReadOnlyList<int> Peers(int myPort)
    {
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        return Read().Where(kv => kv.Key != myPort && now - kv.Value <= StaleSeconds).Select(kv => kv.Key).ToList();
    }

    private static Dictionary<int, long> Read()
    {
        var d = new Dictionary<int, long>();
        try
        {
            if (File.Exists(Path))
                foreach (var line in File.ReadAllLines(Path))
                {
                    var p = line.Split('\t');
                    if (p.Length == 2 && int.TryParse(p[0], out var port) && long.TryParse(p[1], out var t)) d[port] = t;
                }
        }
        catch { }
        return d;
    }
}
