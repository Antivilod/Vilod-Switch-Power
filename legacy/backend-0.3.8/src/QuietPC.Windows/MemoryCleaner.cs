using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
namespace QuietPC.Windows;
public static class MemoryCleaner {
 public static string TrimFileCache(){
  using var process=Process.GetCurrentProcess();
  if(!OpenProcessToken(process.Handle,0x28,out nint token))throw new Win32Exception(Marshal.GetLastWin32Error());
  TokenPrivileges previous=default;bool adjusted=false;
  try{
   if(!LookupPrivilegeValue(null,"SeIncreaseQuotaPrivilege",out var luid))throw new Win32Exception(Marshal.GetLastWin32Error());
   var enabled=new TokenPrivileges{Count=1,Luid=luid,Attributes=2};
   if(!AdjustTokenPrivileges(token,false,ref enabled,(uint)Marshal.SizeOf<TokenPrivileges>(),out previous,out _)||Marshal.GetLastWin32Error()!=0)throw new Win32Exception(Marshal.GetLastWin32Error());
   adjusted=true;var before=Sensors.Ram();
   if(!SetSystemFileCacheSize(nuint.MaxValue,nuint.MaxValue,0))throw new Win32Exception(Marshal.GetLastWin32Error());
   var after=Sensors.Ram();double delta=((double)after.Available-before.Available)/1048576;
   return $"Файловый кэш обработан Windows. Изменение доступной RAM: {delta:+0;-0;0} МБ. Память программ не выгружалась.";
  }finally{if(adjusted)AdjustTokenPrivileges(token,false,ref previous,0,out _,out _);CloseHandle(token);}
 }
 [StructLayout(LayoutKind.Sequential)]struct Luid{public uint Low;public int High;}
 [StructLayout(LayoutKind.Sequential)]struct TokenPrivileges{public uint Count;public Luid Luid;public uint Attributes;}
 [DllImport("advapi32.dll",SetLastError=true)]static extern bool OpenProcessToken(nint process,uint access,out nint token);
 [DllImport("advapi32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern bool LookupPrivilegeValue(string? system,string name,out Luid luid);
 [DllImport("advapi32.dll",SetLastError=true)]static extern bool AdjustTokenPrivileges(nint token,bool disable,ref TokenPrivileges state,uint length,out TokenPrivileges previous,out uint required);
 [DllImport("kernel32.dll",SetLastError=true)]static extern bool SetSystemFileCacheSize(nuint minimum,nuint maximum,uint flags);
 [DllImport("kernel32.dll")]static extern bool CloseHandle(nint handle);
}
