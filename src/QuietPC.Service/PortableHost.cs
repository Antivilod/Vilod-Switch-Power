using System.Diagnostics;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using QuietPC.Core;
namespace QuietPC.Service;
static class PortableHost {
 public static void Run(string owner,string pipe,int parentId,long parentTicks){
  try{
   if(!owner.StartsWith("S-1-5-21-",StringComparison.Ordinal)||!pipe.StartsWith("VSP.portable.",StringComparison.Ordinal)||pipe.Length!=45||!Guid.TryParseExact(pipe[13..],"N",out _))throw new ArgumentException("Неверные аргументы portable");
   using var identity=WindowsIdentity.GetCurrent();if(!new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))throw new UnauthorizedAccessException("Нужны права администратора");
   using var parent=Process.GetProcessById(parentId);if(parent.StartTime.ToUniversalTime().Ticks!=parentTicks)throw new IOException("Процесс владельца изменился");
   using var single=new Mutex(true,"Global\\VSP.Portable.PowerHost",out bool first);if(!first)throw new IOException("Portable-помощник уже работает");
   // Do not overlap another active controller. No changes are made by this probe.
   CheckInstalledInactive();
   var root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),"VSP-Portable");
   SecureDirectory(root,true);var state=Path.Combine(root,owner);SecureDirectory(state);
   using var host=new Host(owner,pipe,state);host.StartPortable();
   try{while(!parent.WaitForExit(1000)){} }finally{host.StopPortable();}
  }catch(Exception ex){System.Diagnostics.Debug.WriteLine(ex);Environment.ExitCode=1;}
 }
 static void CheckInstalledInactive(){
  try{
   using var pipe=new NamedPipeClientStream(".","QuietPC.v1",PipeDirection.InOut);pipe.Connect(500);
   var data=JsonSerializer.SerializeToUtf8Bytes(new Request("heartbeat"));pipe.Write(BitConverter.GetBytes(data.Length));pipe.Write(data);
   // Bound the synchronous read with an async deadline, never wait indefinitely on a stale server.
   using var deadline=new CancellationTokenSource(1500);byte[] len=new byte[4];pipe.ReadExactlyAsync(len,deadline.Token).AsTask().GetAwaiter().GetResult();int n=BitConverter.ToInt32(len);if(n is <1 or >16384)throw new InvalidDataException("Неверный ответ установленной службы");
   byte[] reply=new byte[n];pipe.ReadExactlyAsync(reply,deadline.Token).AsTask().GetAwaiter().GetResult();if(JsonSerializer.Deserialize<Reply>(reply)?.Enabled==true)throw new InvalidOperationException("Сначала выключите управление и закройте установленную VSP");
  }catch(TimeoutException){} // No installed service responded. Other failures stop portable before applying anything.
 }
 static void SecureDirectory(string path,bool parent=false){
  var info=new DirectoryInfo(path);
  if(info.Exists){
   if((info.Attributes&FileAttributes.ReparsePoint)!=0)throw new IOException("Перенаправленный журнал запрещён");
   var owner=info.GetAccessControl().GetOwner(typeof(SecurityIdentifier))?.Value;
   if(owner is not "S-1-5-18" and not "S-1-5-32-544")throw new IOException("Каталог журнала имеет неподтверждённого владельца");
   foreach(var entry in info.EnumerateFileSystemInfos()){if((entry.Attributes&FileAttributes.ReparsePoint)!=0||(!parent&&entry is DirectoryInfo))throw new IOException("Неподдерживаемое содержимое каталога журнала");}
  }
  var acl=new DirectorySecurity();acl.SetAccessRuleProtection(true,false);acl.SetOwner(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid,null));
  foreach(var sid in new[]{WellKnownSidType.LocalSystemSid,WellKnownSidType.BuiltinAdministratorsSid})acl.AddAccessRule(new(new SecurityIdentifier(sid,null),FileSystemRights.FullControl,InheritanceFlags.ContainerInherit|InheritanceFlags.ObjectInherit,PropagationFlags.None,AccessControlType.Allow));
  if(!info.Exists)FileSystemAclExtensions.CreateDirectory(acl,path);else info.SetAccessControl(acl);
 }
}
