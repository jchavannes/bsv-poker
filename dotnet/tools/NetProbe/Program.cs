using System.Net; using System.Net.Sockets; using System.Runtime.InteropServices; using BsvPoker.Net.Bsv;
var net = NetworkParams.For(BsvNetwork.Mainnet);
var ips = (await Dns.GetHostAddressesAsync("seed.bitcoinsv.io")).Concat(await Dns.GetHostAddressesAsync("seed.bitcoinseed.directory")).Where(a=>a.AddressFamily==AddressFamily.InterNetwork).Select(a=>a.ToString()).Distinct().ToList();
Console.WriteLine("public node IPs: "+string.Join(", ",ips));
foreach(var ip in ips){
  Console.Write($"{ip}:8333 -> ");
  try{ using var c=new TcpClient(); await c.ConnectAsync(ip,8333).WaitAsync(TimeSpan.FromSeconds(6)); var s=c.GetStream();
    await s.WriteAsync(new BsvMessage("version",BsvVersion.Build(0,(ulong)Random.Shared.NextInt64())).Encode(net.Magic));
    var acc=new List<byte>(); var buf=new byte[32768]; bool gv=false,gva=false; var dl=DateTime.UtcNow.AddSeconds(10); var got=new List<string>();
    while(DateTime.UtcNow<dl && !(gv&&gva)){ using var cts=new CancellationTokenSource(TimeSpan.FromSeconds(3)); int n; try{n=await s.ReadAsync(buf,cts.Token);}catch{break;} if(n<=0){got.Add("CLOSED");break;} acc.AddRange(buf.AsSpan(0,n).ToArray());
      while(true){ var st=BsvMessage.TryDecode(CollectionsMarshal.AsSpan(acc),net.Magic,out var m,out int cons); if(st==BsvMessage.DecodeStatus.NeedMore)break; if(st!=BsvMessage.DecodeStatus.Ok){got.Add("DECODE:"+st);acc.Clear();break;} acc.RemoveRange(0,cons); got.Add(m!.Command); if(m.Command=="version"){await s.WriteAsync(new BsvMessage("verack",Array.Empty<byte>()).Encode(net.Magic));gv=true;} if(m.Command=="verack")gva=true; if(m.Command=="ping")await s.WriteAsync(new BsvMessage("pong",m.Payload).Encode(net.Magic)); } }
    Console.WriteLine($"handshake={(gv&&gva)} msgs=[{string.Join(",",got)}]");
  }catch(Exception e){Console.WriteLine("ERR "+e.Message);}
}
