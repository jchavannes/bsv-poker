using System.Security.Cryptography;
using BsvPoker.Core;
using BsvPoker.Crypto;

// Generate a DEDICATED testnet wallet that this agent controls (seed stored in the clear so the comprehensive
// testnet exercise can spend it). Derives the testnet (0x6f) receive address for index 0 the same way the app
// does (WalletKeys.Account(seed,0,0) → HASH160 → Base58Check). Persists everything to a file next to the repo.

const byte TestnetP2pkh = 0x6f;   // NetworkParams.Testnet.AddressVersion
const byte TestnetWif   = 0xef;   // NetworkParams.Testnet WIF/secret version

var outPath = args.Length > 0 ? args[0] : @"D:\claude\Mental Poker\bsv-poker\_testnet_wallet.txt";

// reuse an existing controlled wallet if present (never churn the funded key); else create one
byte[] seed;
if (File.Exists(outPath))
{
    var line = File.ReadAllLines(outPath).FirstOrDefault(l => l.StartsWith("SEEDBACKUP=")) ?? "";
    var bk = line.Length > 11 ? line[11..].Trim() : "";
    seed = bk.Length > 0 ? WalletKeys.BackupToSeed(bk) : RandomNumberGenerator.GetBytes(32);
}
else seed = RandomNumberGenerator.GetBytes(32);

var backup = WalletKeys.SeedToBackup(seed);
var k = WalletKeys.Account(seed, 0, 0);
var payload = new byte[21]; payload[0] = TestnetP2pkh; Hashes.Hash160(k.Pub).CopyTo(payload, 1);
var addr = Base58.CheckEncode(payload);
var wif = WalletExtras.ToWif(k.Priv, true, TestnetWif);

var lines = new[]
{
    "# DEDICATED TESTNET WALLET — agent-controlled, for the comprehensive on-chain testnet exercise.",
    "# NEVER delete this; it holds the funded key. Not for git.",
    "SEEDBACKUP=" + backup,
    "ADDRESS_TESTNET=" + addr,
    "RECVKEY=chain0/index0",
    "PUBHEX=" + Convert.ToHexString(k.Pub).ToLowerInvariant(),
    "WIF_TESTNET=" + wif,
};
File.WriteAllLines(outPath, lines);

Console.WriteLine("TESTNET_ADDRESS=" + addr);
Console.WriteLine("SEED_PERSISTED=" + outPath);
Console.WriteLine("PUBHEX=" + Convert.ToHexString(k.Pub).ToLowerInvariant());
