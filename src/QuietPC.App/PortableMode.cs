using System.Diagnostics;
using System.IO;
using System.Security.Principal;
namespace QuietPC.App;
static class PortableMode {
 public static bool Enabled=>File.Exists(Path.Combine(AppContext.BaseDirectory,"portable.flag"));
 static readonly Lazy<string> dataDirectory=new(ResolveDataDirectory);
 public static string DataDirectory=>dataDirectory.Value;
 static string ResolveDataDirectory(){
  if(!Enabled)return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Vilod","VSP");
  string preferred=Path.Combine(AppContext.BaseDirectory,"Data");
  try{Directory.CreateDirectory(preferred);using var probe=new FileStream(Path.Combine(preferred,".write-check-"+Guid.NewGuid().ToString("N")),FileMode.CreateNew,FileAccess.Write,FileShare.None,1,FileOptions.DeleteOnClose);return Path.Combine(preferred,"Machines",HardwareCapabilities.Read().Fingerprint);}
  catch(Exception ex)when(ex is IOException or UnauthorizedAccessException){return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"VSP-Portable","Data","Machines",HardwareCapabilities.Read().Fingerprint);}
 }
 public static bool UsingInstalledService{get;private set;}
 public static string BackendLabel=>"Portable · служба питания на этом ПК";
 public static string PipeName=>"QuietPC.v1";
 public static uint HostPid=>0;
 public static bool HostRunning=>false;
 public static bool HostWasStarted=>UsingInstalledService;
 public static async Task EnsureHost(){
  if(!Enabled||UsingInstalledService)return;
  var installed=await Client.ProbeInstalled();
  if(installed is null)throw new IOException("Для управления питанием установите VSP с помощью установщика из официального выпуска. Мониторинг доступен без службы.");
  if(!installed.Ok||installed.Enabled)throw new IOException("Сначала выключите управление другой копии VSP и завершите восстановление настроек.");
  UsingInstalledService=true;
 }
}
