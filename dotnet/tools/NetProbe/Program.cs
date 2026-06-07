using BsvPoker.Net.Bsv;
// HONEST live-network probe: connect, then PERSISTENT store-backed header sync from the live network,
// proving resume across "restarts" (reopen the same store file and continue).
foreach (var (which, batches) in new[] { (BsvNetwork.Testnet, 12), (BsvNetwork.Mainnet, 8) })
{
    var net = NetworkParams.For(which);
    var path = Path.Combine(Path.GetTempPath(), $"bsvpoker-probe-{which}.dat");
    if (File.Exists(path)) File.Delete(path);

    var node = new BsvNode(net);
    var seeds = await node.ResolveSeedsAsync();
    foreach (var ep in seeds.Take(12)) if (await node.ConnectAsync(ep.Address.ToString(), ep.Port)) break;
    Console.WriteLine($"[{which}] peers={node.PeerCount}, advertised tip={node.BestHeight}");
    if (node.PeerCount > 0)
    {
        // first run: sync into a fresh persistent store
        var store = new HeaderStore(path);
        var (appended1, h1) = await node.SyncHeadersToStoreAsync(store, maxBatches: batches, waitMs: 9000);
        Console.WriteLine($"[{which}] run 1: appended {appended1}, store height {h1}");

        // "restart": reopen the SAME file — it must reload and resume past the persisted tip
        var reopened = new HeaderStore(path);
        var loaded = reopened.Load();
        var validated = HeaderStore.ValidatePrefix(loaded, InternalGenesis(net));
        Console.WriteLine($"[{which}] reopen: loaded {loaded.Count}, re-validated {validated} from genesis (PoW+linkage)");
        var (appended2, h2) = await node.SyncHeadersToStoreAsync(reopened, maxBatches: 3, waitMs: 9000);
        Console.WriteLine($"[{which}] run 2 (resume): appended {appended2} more, store height {h2}");
    }
    node.Dispose();
    if (File.Exists(path)) File.Delete(path);
}

static byte[] InternalGenesis(NetworkParams net)
{
    var b = Convert.FromHexString(net.GenesisHashHex); Array.Reverse(b); return b;
}
