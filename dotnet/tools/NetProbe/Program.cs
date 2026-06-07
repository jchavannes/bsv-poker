using BsvPoker.Net.Bsv;
// HONEST live-network probe: connect to the real BSV network AND download+validate real headers.
foreach (var which in new[] { BsvNetwork.Testnet, BsvNetwork.Mainnet })
{
    var node = new BsvNode(NetworkParams.For(which));
    var seeds = await node.ResolveSeedsAsync();
    Console.WriteLine($"[{which}] DNS seeds -> {seeds.Count} endpoints");
    foreach (var ep in seeds.Take(12)) if (await node.ConnectAsync(ep.Address.ToString(), ep.Port)) break;
    Console.WriteLine($"[{which}] connected peers={node.PeerCount}, advertised height={node.BestHeight}");
    if (node.PeerCount > 0)
    {
        var (valid, recv) = await node.DownloadHeadersFromGenesisAsync(10000);
        Console.WriteLine($"[{which}] headers received={recv}, VALIDATED from genesis (PoW+linkage)={valid}");
    }
    node.Dispose();
}
