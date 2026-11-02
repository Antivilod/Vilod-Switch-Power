using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Diagnostics;
using QuietPC.Core;
namespace QuietPC.App;
static class Client {
 [DllImport("advapi32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern nint OpenSCManager(string? machine,string? database,uint access);
 [DllImport("advapi32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern nint OpenService(nint manager,string name,uint access);
 [DllImport("advapi32.dll",SetLastError=true)]static extern bool QueryServiceStatusEx(nint service,int level,out ServiceStatus status,uint size,out uint needed);
 [DllImport("advapi32.dll")]static extern bool CloseServiceHandle(nint handle);
 [StructLayout(LayoutKind.Sequential)]struct ServiceStatus {public uint Type,State,Accepted,Win32Exit,SpecificExit,Checkpoint,WaitHint,ProcessId,Flags;}
 static uint ServicePid(){
  var manager=OpenSCManager(null,null,1);if(manager==0)throw new IOException("SCM недоступен");
  try{var service=OpenService(manager,"QuietPC",4);if(service==0)throw new IOException("Служба не установлена");
   try{if(!QueryServiceStatusEx(service,0,out var status,(uint)Marshal.SizeOf<ServiceStatus>(),out _)||status.State!=4||status.ProcessId==0)throw new IOException("Служба не запущена");return status.ProcessId;}finally{CloseServiceHandle(service);}
  }finally{CloseServiceHandle(manager);}
 }
 [DllImport("kernel32.dll",SetLastError=true)]static extern bool GetNamedPipeServerProcessId(nint pipe,out uint pid);
 public static async Task<Reply> Send(Request request){
  if(PortableMode.Enabled&&!PortableMode.UsingInstalledService&&!PortableMode.HostRunning){if(!PortableMode.HostWasStarted&&(request.Command is "heartbeat" or "off"))return new(true,false,"Выключено","Portable: управление не запущено");throw new IOException("Portable-помощник завершился. Восстановление не подтверждено. Нажмите выключение, чтобы запустить восстановление журнала.");}
  return await Exchange(request,PortableMode.PipeName,()=>PortableMode.Enabled&&!PortableMode.UsingInstalledService?PortableMode.HostPid:ServicePid(),25);
 }
 public static async Task<Reply?> ProbeInstalled(){
  uint pid;try{pid=ServicePid();}catch(IOException){return null;}
  return await Exchange(new("heartbeat"),"QuietPC.v1",()=>pid,3);
 }
 static async Task<Reply> Exchange(Request request,string pipeName,Func<uint> expectedPid,int seconds){
  using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(seconds));
  using var p=new NamedPipeClientStream(".",pipeName,PipeDirection.InOut,PipeOptions.Asynchronous,System.Security.Principal.TokenImpersonationLevel.Impersonation);
  await p.ConnectAsync(timeout.Token);
  if(!GetNamedPipeServerProcessId(p.SafePipeHandle.DangerousGetHandle(),out uint id))throw new IOException("Не удалось проверить службу");
  // SCM provides a trusted service PID without requiring access to a SYSTEM process.
  if(id!=expectedPid())throw new IOException("Неизвестный сервер IPC");
  var data=JsonSerializer.SerializeToUtf8Bytes(request);if(data.Length>65536)throw new IOException("Слишком большой список игр");await p.WriteAsync(BitConverter.GetBytes(data.Length),timeout.Token);await p.WriteAsync(data,timeout.Token);
  byte[] len=new byte[4];await p.ReadExactlyAsync(len,timeout.Token);int n=BitConverter.ToInt32(len);if(n is <1 or >16384)throw new IOException("Неверный ответ");byte[] output=new byte[n];await p.ReadExactlyAsync(output,timeout.Token);return JsonSerializer.Deserialize<Reply>(output)??throw new IOException("Пустой ответ");
 }
}
