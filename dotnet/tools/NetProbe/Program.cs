using BsvPoker.Net.Bsv; using BsvPoker.Core; using BsvPoker.Crypto;
var addr = "1BH5Uf3tbfNSBjCmVJYWB5nTCRXVVBQXuR";
var payload = Base58.CheckDecode(addr);
Console.WriteLine($"addr version=0x{payload[0]:x2} h160={Convert.ToHexString(payload[1..]).ToLowerInvariant()}");
var script = Chain.P2pkhLock(payload[1..]);
var sh = ElectrumXClient.ScriptHashOf(script);
Console.WriteLine("scripthash="+sh);
using var ex = new ElectrumXClient();
bool c = await ex.ConnectAnyAsync(ElectrumXClient.ServersFor(BsvNetwork.Mainnet), 8000, m=>Console.WriteLine("[log] "+m));
if(!c){Console.WriteLine("no server");return;}
var us = await ex.ListUnspentAsync(sh);
Console.WriteLine($"listunspent -> {us.Count} utxo(s)");
foreach(var u in us){ Console.WriteLine($"  {u.TxHashDisplay}:{u.Vout} value={u.Value} height={u.Height}");
  try{ bool v = await ex.VerifyUtxoAsync(u.TxHashDisplay,u.Height); Console.WriteLine($"    SPV verify={v}"); }catch(Exception e){Console.WriteLine("    verify err: "+e.Message);} }
