using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using QuietPC.Core;
namespace QuietPC.Windows;

public sealed class OwnedProcessPriority(string owner):IProcessPriority {
 public ProcessState? Read(int id){
  try{using var p=Process.GetProcessById(id);if(!Allowed(p))return null;return new(id,p.StartTime.ToUniversalTime().Ticks,(int)p.PriorityClass);}
  catch(ArgumentException){return null;}catch(InvalidOperationException){return null;}
 }
 bool Allowed(Process p){
  if(p.Id==Environment.ProcessId||p.SessionId==0)return false;
  if(!OpenProcessToken(p.Handle,8,out var token))throw new Win32Exception(Marshal.GetLastWin32Error());
  try{using var identity=new WindowsIdentity(token);if(identity.User?.Value!=owner)return false;}
  finally{CloseHandle(token);}
  var path=p.MainModule?.FileName;if(path is null)return false;
  var windows=Environment.GetFolderPath(Environment.SpecialFolder.Windows)+Path.DirectorySeparatorChar;
  if(path.StartsWith(windows,StringComparison.OrdinalIgnoreCase))return false;
  return !new[]{"msmpeng","mpdefendercoreservice","securityhealthservice","quietpc","quietpc.service","steamservice"}.Contains(p.ProcessName.ToLowerInvariant());
 }
 public void Set(ProcessState expected,int priority){
  if(priority is not (32 or 16384 or 32768))throw new ArgumentException("Недопустимый приоритет");
  using var p=Process.GetProcessById(expected.Id);
  if(!Allowed(p)||p.StartTime.ToUniversalTime().Ticks!=expected.Started)throw new IOException("Идентичность процесса изменилась");
  if((int)p.PriorityClass!=expected.Priority)throw new IOException("Приоритет изменён извне");
  p.PriorityClass=(ProcessPriorityClass)priority;
 }
 [DllImport("advapi32.dll",SetLastError=true)]static extern bool OpenProcessToken(nint process,uint access,out nint token);
 [DllImport("kernel32.dll")]static extern bool CloseHandle(nint handle);
}
