using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.ServiceProcess;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using QuietPC.Core;
using QuietPC.Windows;

namespace QuietPC.Setup;

static class Program {
 const string ServiceName="QuietPC";
 static string Root=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"Vilod","VSP");
 static string Backend=>Path.Combine(Root,"service","QuietPC.Service.exe");
 static int Main(string[] args){
  try{
   using var identity=WindowsIdentity.GetCurrent();
   if(!new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))throw new UnauthorizedAccessException("Administrator rights are required");
   if(args.Length!=1)throw new ArgumentException("Use --preflight, --install, --remove, --verify or --smoke-power");
   switch(args[0]){
    case "--preflight":
     if(Exists())throw new IOException("A QuietPC/VSP service already exists. This installer will not replace it. Remove the previous installation using its own uninstaller first.");
     if(Directory.Exists(Root))throw new IOException("The installation directory already exists. Use its uninstaller or resolve the previous installation first.");
     Console.WriteLine("PASS_PREFLIGHT");break;
    case "--install":Install();Console.WriteLine("PASS_INSTALL");break;
    case "--remove":Remove();Console.WriteLine("PASS_REMOVE");break;
    case "--verify":Verify();Console.WriteLine("PASS_SERVICE");break;
    case "--smoke-power":SmokePower();Console.WriteLine("PASS_POWER_RESTORE");break;
    default:throw new ArgumentException("Unknown setup command");
   }
   return 0;
  }catch(Exception error){Console.Error.WriteLine(error.Message);return 1;}
 }
 static bool Exists(){using var key=Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\"+ServiceName);return key!=null;}
 static void ValidateRoot(){
  var root=Path.GetFullPath(Root);var executable=Path.GetFullPath(Backend);
  if(!File.Exists(executable)||!Directory.Exists(root))throw new IOException("Incomplete installation payload");
  for(DirectoryInfo? directory=new(root);directory!=null;directory=directory.Parent)
   if((directory.Attributes&FileAttributes.ReparsePoint)!=0)throw new IOException("Redirected installation directories are not supported");
  if((File.GetAttributes(executable)&FileAttributes.ReparsePoint)!=0)throw new IOException("Redirected service executable is not supported");
 }
 static string? SessionAccount(){
  static string Read(int session,int info){
   if(!WTSQuerySessionInformation(0,session,info,out var buffer,out _))return "";
   try{return Marshal.PtrToStringUni(buffer)??"";}finally{WTSFreeMemory(buffer);}
  }
  int session=Process.GetCurrentProcess().SessionId;
  string user=Read(session,5),domain=Read(session,7);
  return string.IsNullOrWhiteSpace(user)?null:(string.IsNullOrWhiteSpace(domain)?user:domain+"\\"+user);
 }
 static string OwnerSid(){
  string? account=SessionAccount();
  string sid=account==null?WindowsIdentity.GetCurrent().User!.Value:((SecurityIdentifier)new NTAccount(account).Translate(typeof(SecurityIdentifier))).Value;
  if(!Regex.IsMatch(sid,@"^S-1-5-21-\d+-\d+-\d+-\d+$",RegexOptions.CultureInvariant))throw new IOException("Cannot determine a local interactive owner account");
  return sid;
 }
 static void Sc(params string[] args){
  var start=new ProcessStartInfo(Path.Combine(Environment.SystemDirectory,"sc.exe")){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
  foreach(string arg in args)start.ArgumentList.Add(arg);
  using var process=Process.Start(start)??throw new IOException("Cannot start Service Control Manager tool");
  var output=process.StandardOutput.ReadToEndAsync();var error=process.StandardError.ReadToEndAsync();
  if(!process.WaitForExit(30000)){process.Kill();throw new IOException("Service configuration timed out");}
  Task.WaitAll(output,error);
  if(process.ExitCode!=0)throw new IOException("Service configuration failed ("+process.ExitCode+")");
 }
 static void AssertOwned(){
  ValidateRoot();
  using var key=Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\"+ServiceName);
  string command=key?.GetValue("ImagePath") as string??"";
  if(!command.StartsWith('"'+Backend+"\" ",StringComparison.OrdinalIgnoreCase)||
   !Regex.IsMatch(command[(Backend.Length+3)..],@"^S-1-5-21-\d+-\d+-\d+-\d+$",RegexOptions.CultureInvariant))
   throw new IOException("The existing service does not belong to this installation");
 }
 static void Install(){
  ValidateRoot();if(Exists())throw new IOException("Refusing to replace an existing service");
  string owner=OwnerSid();bool created=false;
  try{
   Sc("create",ServiceName,"binPath=",'"'+Backend+"\" "+owner,"start=","delayed-auto","obj=","LocalSystem","DisplayName=","Vilod Switch Power");created=true;
   Sc("description",ServiceName,"Local VSP power backend. Restores owned changes on stop and heartbeat loss.");
   using var service=new ServiceController(ServiceName);service.Start();service.WaitForStatus(ServiceControllerStatus.Running,TimeSpan.FromSeconds(30));
   Verify();
  }catch{
   if(created){try{using var service=new ServiceController(ServiceName);if(service.Status!=ServiceControllerStatus.Stopped){service.Stop();service.WaitForStatus(ServiceControllerStatus.Stopped,TimeSpan.FromSeconds(30));}Sc("delete",ServiceName);}catch{/* Keep files and recovery state for diagnosis. */}}
   throw;
  }
 }
 static Reply Exchange(Request request){
  using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(25));
  using var pipe=new NamedPipeClientStream(".","QuietPC.v1",PipeDirection.InOut,PipeOptions.Asynchronous,TokenImpersonationLevel.Impersonation);
  pipe.ConnectAsync(deadline.Token).GetAwaiter().GetResult();
  using var key=Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\"+ServiceName);
  // Never send even a restore request to an unrelated local pipe server.
  if(!GetNamedPipeServerProcessId(pipe.SafePipeHandle.DangerousGetHandle(),out uint pid))throw new IOException("Cannot authenticate the local service");
  if(!GetServicePid(out uint expected)||pid!=expected)throw new IOException("Unexpected local pipe server");
  byte[] data=JsonSerializer.SerializeToUtf8Bytes(request);
  pipe.WriteAsync(BitConverter.GetBytes(data.Length),deadline.Token).AsTask().GetAwaiter().GetResult();
  pipe.WriteAsync(data,deadline.Token).AsTask().GetAwaiter().GetResult();
  byte[] length=new byte[4];pipe.ReadExactlyAsync(length,deadline.Token).AsTask().GetAwaiter().GetResult();int count=BitConverter.ToInt32(length);
  if(count is <1 or >16384)throw new IOException("Invalid service frame");
  byte[] reply=new byte[count];pipe.ReadExactlyAsync(reply,deadline.Token).AsTask().GetAwaiter().GetResult();
  return JsonSerializer.Deserialize<Reply>(reply)??throw new IOException("Empty service reply");
 }
 static void Verify(){AssertOwned();var reply=Exchange(new("heartbeat"));if(!reply.Ok)throw new IOException("Service is not healthy: "+reply.State);}
 static void Remove(){
  if(!Exists())return;
  AssertOwned();using var service=new ServiceController(ServiceName);
  if(service.Status!=ServiceControllerStatus.Running){service.Start();service.WaitForStatus(ServiceControllerStatus.Running,TimeSpan.FromSeconds(30));}
  var restored=Exchange(new("prepareuninstall"));
  if(!restored.Ok||restored.Enabled)throw new IOException("Restoration is not confirmed. Files and recovery data are preserved.");
  service.Stop();service.WaitForStatus(ServiceControllerStatus.Stopped,TimeSpan.FromSeconds(30));Sc("delete",ServiceName);
 }
 static void SmokePower(){
  AssertOwned();var power=new WindowsPower();Guid original=power.Active();var baseline=power.Read(original);
  try{
   foreach(var profile in Profile.Defaults.Where(p=>p.Id is "work" or "60" or "120" or "200")){
    var reply=Exchange(new("apply",profile,[],false));
    if(!reply.Ok||!reply.Enabled)throw new IOException("Power apply failed for "+profile.Id+": "+reply.State);
    var restored=Exchange(new("off"));
    if(!restored.Ok||restored.Enabled||power.Active()!=original||power.Read(original)!=baseline)throw new IOException("Power restoration failed");
   }
  }finally{Exchange(new("off"));}
 }
 [StructLayout(LayoutKind.Sequential)]struct Status{public uint Type,State,Accepted,Win32Exit,SpecificExit,Checkpoint,WaitHint,ProcessId,Flags;}
 static bool GetServicePid(out uint pid){pid=0;var manager=OpenSCManager(null,null,1);if(manager==0)return false;try{var service=OpenService(manager,ServiceName,4);if(service==0)return false;try{if(!QueryServiceStatusEx(service,0,out var status,(uint)Marshal.SizeOf<Status>(),out _)||status.State!=4)return false;pid=status.ProcessId;return pid!=0;}finally{CloseServiceHandle(service);}}finally{CloseServiceHandle(manager);}}
 [DllImport("advapi32.dll",CharSet=CharSet.Unicode)]static extern nint OpenSCManager(string? machine,string? database,uint access);
 [DllImport("advapi32.dll",CharSet=CharSet.Unicode)]static extern nint OpenService(nint manager,string name,uint access);
 [DllImport("advapi32.dll")]static extern bool QueryServiceStatusEx(nint service,int level,out Status status,uint size,out uint needed);
 [DllImport("advapi32.dll")]static extern bool CloseServiceHandle(nint service);
 [DllImport("kernel32.dll",SetLastError=true)]static extern bool GetNamedPipeServerProcessId(nint pipe,out uint pid);
 [DllImport("wtsapi32.dll",CharSet=CharSet.Unicode)]static extern bool WTSQuerySessionInformation(nint server,int session,int info,out nint buffer,out int bytes);
 [DllImport("wtsapi32.dll")]static extern void WTSFreeMemory(nint buffer);
}
