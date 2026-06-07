using System.Diagnostics;
using System.Text.RegularExpressions;
using BsvPoker.Core;
using BsvPoker.Crypto;
using BsvPoker.Net.Bsv;

// REAL end-to-end on a live BSV regtest node: this proves the no-server money spine against actual consensus.
//   node side (bitcoin-cli): mine coinbase, pay our address, mine the funding tx.
//   client side (our library, over the P2P wire): connect, sync+validate headers, fetch the block via getdata,
//     parse it, build a merkleblock, SPV-verify the funding into a UTXO, build+sign a spend, broadcast it.
//   node side: mine again and confirm the node ACCEPTED and MINED our real signed transaction.
// Nothing here is faked: real addresses, real funding, real SPV proof, a real consensus-valid spend.

const string P2P = "127.0.0.1";
const int P2PPort = 19444;

static string Cli(string args)
{
    var psi = new ProcessStartInfo("wsl.exe",
        $"-d Ubuntu -u root -- docker exec bsvreg bitcoin-cli -regtest -rpcuser=u -rpcpassword=p -rpcport=18443 {args}")
    { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
    var p = Process.Start(psi)!;
    string o = p.StandardOutput.ReadToEnd(), e = p.StandardError.ReadToEnd();
    p.WaitForExit();
    if (p.ExitCode != 0) throw new Exception($"cli '{args}' failed: {e.Trim()}");
    return o.Trim();
}

Console.WriteLine("== BSV REGTEST END-TO-END (real node, real consensus) ==");

// --- node: a funded wallet ---
var nodeAddr = Cli("getnewaddress");
Cli($"generatetoaddress 101 {nodeAddr}");           // mature coinbase → node has spendable coins
Console.WriteLine($"node funded; height = {Cli("getblockcount")}");

// --- our client identity (regtest P2PKH address from our own seed) ---
var seed = WalletKeys.NewSeed();
var me = WalletKeys.Account(seed, 0, 0);
var net = NetworkParams.For(BsvNetwork.Regtest);
var myPayload = new byte[21]; myPayload[0] = net.AddressVersion; Hashes.Hash160(me.Pub).CopyTo(myPayload, 1);
var myAddr = Base58.CheckEncode(myPayload);
Console.WriteLine($"our regtest address: {myAddr}");

// --- node: pay our address 1.0, then mine it into a block ---
var fundTxid = Cli($"sendtoaddress {myAddr} 1.0");
Console.WriteLine($"funding txid: {fundTxid}");
var mined = Cli($"generatetoaddress 1 {nodeAddr}");
var fundBlockHash = Regex.Match(mined, "[0-9a-f]{64}").Value;
Console.WriteLine($"funding block: {fundBlockHash}");

// --- client: connect over the P2P wire and validate headers ourselves ---
Console.WriteLine($"regtest magic = {Convert.ToHexString(net.Magic)}");
// raw diagnostic handshake so we can SEE why a connect fails (TCP vs magic vs timeout)
try
{
    using var diag = new System.Net.Sockets.TcpClient();
    await diag.ConnectAsync(P2P, P2PPort).WaitAsync(TimeSpan.FromSeconds(5));
    Console.WriteLine($"TCP connect OK ({diag.Connected})");
    var dp = new BsvPeer(net, diag);
    await dp.HandshakeAsync(startHeight: 0, timeoutMs: 8000);
    Console.WriteLine($"diagnostic handshake OK: remote version startHeight={dp.RemoteVersion?.StartHeight}");
    dp.Dispose();
}
catch (Exception ex) { Console.WriteLine($"diagnostic handshake threw: {ex.GetType().Name}: {ex.Message}"); }

var node = new BsvNode(net);
if (!await node.ConnectAsync(P2P, P2PPort)) { Console.WriteLine("FAIL: could not connect/handshake the regtest node"); return 1; }
Console.WriteLine($"connected: peers={node.PeerCount}, advertised tip={node.BestHeight}");
var storePath = Path.Combine(Path.GetTempPath(), "bsvpoker-regtest-e2e.dat");
if (File.Exists(storePath)) File.Delete(storePath);
var store = new HeaderStore(storePath);
var (appended, height) = await node.SyncHeadersToStoreAsync(store, maxBatches: 5, waitMs: 6000);
Console.WriteLine($"headers synced & validated: {height}");

// --- client: fetch the funding block, parse it, SPV-verify the funding into a UTXO ---
var raw = await node.GetBlockAsync(fundBlockHash, waitMs: 15000);
if (raw == null) { Console.WriteLine("FAIL: node did not serve the block"); return 1; }
var parsed = BsvBlock.Parse(raw);                    // validates merkle root vs header
int idx = parsed.Txs.FindIndex(t => Chain.Txid(t) == fundTxid);
if (idx < 0) { Console.WriteLine("FAIL: funding tx not in block"); return 1; }
var fundTx = parsed.Txs[idx];
var myLock = Chain.P2pkhLock(myPayload[1..]);
uint vout = (uint)fundTx.Outs.FindIndex(o => o.Script.AsSpan().SequenceEqual(myLock));
Console.WriteLine($"funding tx in block at idx {idx}, paying us at vout {vout} ({fundTx.Outs[(int)vout].Value} sat)");

var (chain, _) = store.BuildChain();
var mb = PartialMerkleTree.BuildMerkleBlock(parsed.Header, parsed.Txids, new HashSet<int> { idx });
var utxo = SpvFunding.VerifyFromMerkleBlock(fundTx, vout, mb, chain, me.Pub, 0, 0);
if (utxo == null) { Console.WriteLine("FAIL: SPV funding did not verify"); return 1; }
Console.WriteLine($"SPV FUNDING VERIFIED against our own headers: {utxo.Value} sat UTXO {utxo.Txid[..16]}…:{utxo.Vout}");

// --- client: build + sign a real spend, broadcast it over the P2P wire ---
var backAddr = Cli("getnewaddress");
var backPayload = Base58.CheckDecode(backAddr);
var wallet = new OnChainWallet(seed); wallet.Add(utxo);
var spend = wallet.BuildAction(Chain.P2pkhLock(backPayload[1..]), outputValue: 50_000_000, fee: 1000);
if (!wallet.VerifySpend(spend)) { Console.WriteLine("FAIL: our spend failed self-verification"); return 1; }
var ourTxid = Chain.Txid(spend.Tx);
node.Broadcast(Chain.Serialize(spend.Tx));
Console.WriteLine($"broadcast our signed spend: {ourTxid}");

// --- node: mine, then confirm the node accepted+mined OUR transaction ---
await Task.Delay(1500);                               // let the tx propagate to the node's mempool
Cli($"generatetoaddress 1 {nodeAddr}");
string confirmsJson;
try { confirmsJson = Cli($"getrawtransaction {ourTxid} true"); }
catch (Exception ex) { Console.WriteLine($"FAIL: node never saw our tx ({ex.Message})"); return 1; }
var conf = Regex.Match(confirmsJson, "\"confirmations\"\\s*:\\s*(\\d+)").Groups[1].Value;
node.Dispose();
if (int.TryParse(conf, out var c) && c >= 1)
{
    Console.WriteLine($"SUCCESS ✓ the regtest node ACCEPTED and MINED our spend ({c} confirmation(s)).");
    Console.WriteLine("END-TO-END PROVEN: real fund → SPV-verify → real signed spend → consensus-accepted.");
    return 0;
}
Console.WriteLine($"FAIL: our tx not confirmed (confirmations='{conf}')");
return 1;
