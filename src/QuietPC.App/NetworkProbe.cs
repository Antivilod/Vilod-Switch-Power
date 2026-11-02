using System.Net;
using System.Net.NetworkInformation;
using QuietPC.Core;
namespace QuietPC.App;
sealed class NetworkProbe:IDisposable {
 readonly CancellationTokenSource stop=new();readonly PingWindow samples=new();readonly object gate=new();
 public string Target{get;private set;}="";public string Status{get;private set;}="Определение адреса…";
 public PingSummary? Summary{get{lock(gate)return samples.Snapshot();}}
 public NetworkProbe(string host){_=Run(host);}
 async Task Run(string host){
  try{
   Target=string.IsNullOrWhiteSpace(host)?NetworkInterface.GetAllNetworkInterfaces().Where(n=>n.OperationalStatus==OperationalStatus.Up).SelectMany(n=>n.GetIPProperties().GatewayAddresses).Select(g=>g.Address).FirstOrDefault(a=>a.AddressFamily==System.Net.Sockets.AddressFamily.InterNetwork&&!a.Equals(IPAddress.Any))?.ToString()??"":host.Trim();
   if(Target.Length==0){Status="Активный шлюз не найден";return;}
   var addresses=await Dns.GetHostAddressesAsync(Target,stop.Token);var address=addresses.FirstOrDefault(a=>a.AddressFamily==System.Net.Sockets.AddressFamily.InterNetwork)??addresses.First();
   using var ping=new Ping();
   while(!stop.IsCancellationRequested){
    double? ms=null;try{var result=await ping.SendPingAsync(address,TimeSpan.FromSeconds(1),new byte[32],null,stop.Token);if(result.Status==IPStatus.Success)ms=result.RoundtripTime;}catch(PingException){}
    lock(gate)samples.Add(ms);Status="ICMP → "+Target+" · это не задержка игрового сервера";
    await Task.Delay(2000,stop.Token);
   }
  }catch(OperationCanceledException){}catch(Exception ex){Status="Сетевой замер недоступен: "+ex.Message;}
 }
 public void Dispose(){stop.Cancel();}
}
