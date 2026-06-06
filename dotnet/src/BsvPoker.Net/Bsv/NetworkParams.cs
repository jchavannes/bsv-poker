namespace BsvPoker.Net.Bsv;

/// <summary>The BSV network to operate on. One code path; only this parameter set and the peer set differ.</summary>
public enum BsvNetwork { Mainnet, Testnet, STN, Regtest }

/// <summary>
/// Real Bitcoin SV network parameters (from bitcoin-sv/src/chainparams.cpp): the P2P message-start magic,
/// default port, Base58 version bytes, and DNS seeds. These let the client be a genuine peer on the chosen
/// BSV network — the SAME logic for regtest, testnet, and mainnet.
/// </summary>
public sealed record NetworkParams(
    BsvNetwork Network,
    byte[] Magic,            // pchMessageStart, 4 bytes
    int DefaultPort,
    byte AddressVersion,     // Base58 P2PKH version
    byte ScriptVersion,      // Base58 P2SH version
    byte WifVersion,         // Base58 WIF version
    string[] DnsSeeds)
{
    public static NetworkParams For(BsvNetwork n) => n switch
    {
        BsvNetwork.Mainnet => new(n, new byte[] { 0xe3, 0xe1, 0xf3, 0xe8 }, 8333, 0x00, 0x05, 0x80,
            new[] { "seed.bitcoinsv.io", "seed.satoshisvision.network", "seed.bitcoinseed.directory" }),
        BsvNetwork.Testnet => new(n, new byte[] { 0xf4, 0xe5, 0xf3, 0xf4 }, 18333, 0x6f, 0xc4, 0xef,
            new[] { "testnet-seed.bitcoinsv.io", "testnet-seed.bitcoincloud.net" }),
        BsvNetwork.STN => new(n, new byte[] { 0xfb, 0xce, 0xc4, 0xf9 }, 9333, 0x6f, 0xc4, 0xef,
            new[] { "stn-seed.bitcoinsv.io" }),
        BsvNetwork.Regtest => new(n, new byte[] { 0xda, 0xb5, 0xbf, 0xfa }, 18444, 0x6f, 0xc4, 0xef,
            Array.Empty<string>()),
        _ => throw new ArgumentOutOfRangeException(nameof(n)),
    };
}
