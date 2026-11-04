using System.Runtime.InteropServices;
using System.ComponentModel;
using QuietPC.Core;
namespace QuietPC.Windows;
public sealed class WindowsPower:IPower {
 static Guid Cpu=new("54533251-82be-4824-96c1-47b60b740d00"), Epp=new("36687f9e-e3a5-4dbf-b1dc-15eb381c6863");
 static Guid Minimum=new("893dee8e-2bef-41e0-89c6-b55d0929964c"),Boost=new("be337238-0d82-4146-a960-4f3749d470c7");
 static void Check(uint code){if(code!=0)throw new Win32Exception((int)code);}
 public Guid Active(){Check(PowerGetActiveScheme(0,out var p));try{return Marshal.PtrToStructure<Guid>(p);}finally{LocalFree(p);}}
 public Scheme Clone(Guid source,uint epp){
  Check(PowerDuplicateScheme(0,ref source,out var p));Guid id;
  try{id=Marshal.PtrToStructure<Guid>(p);}finally{LocalFree(p);}
  try {
   Check(PowerWriteACValueIndex(0,ref id,ref Cpu,ref Epp,epp));Check(PowerWriteDCValueIndex(0,ref id,ref Cpu,ref Epp,epp));
   Check(PowerReadACValueIndex(0,ref source,ref Cpu,ref Minimum,out var minimumAc));Check(PowerReadDCValueIndex(0,ref source,ref Cpu,ref Minimum,out var minimumDc));
   Check(PowerWriteACValueIndex(0,ref id,ref Cpu,ref Minimum,Math.Min(5u,minimumAc)));Check(PowerWriteDCValueIndex(0,ref id,ref Cpu,ref Minimum,Math.Min(5u,minimumDc)));
   if(epp==0){Check(PowerWriteACValueIndex(0,ref id,ref Cpu,ref Boost,2));Check(PowerWriteDCValueIndex(0,ref id,ref Cpu,ref Boost,2));}
   if(epp>=80){Check(PowerWriteACValueIndex(0,ref id,ref Cpu,ref Boost,0));Check(PowerWriteDCValueIndex(0,ref id,ref Cpu,ref Boost,0));}
   var label=System.Text.Encoding.Unicode.GetBytes("VSP - "+(epp==0?"Game Boost":epp>=80?"Работа":"Игра")+"\0");Check(PowerWriteFriendlyName(0,ref id,0,0,label,(uint)label.Length));
   var result=Read(id);if(result.Ac!=epp||result.Dc!=epp)throw new IOException("EPP не подтверждён");return result;
  }
  catch{PowerDeleteScheme(0,ref id);throw;}
 }
 public Scheme Read(Guid id){
  var rc=PowerReadACValueIndex(0,ref id,ref Cpu,ref Epp,out var ac);if(rc==2)return new(id,0,0,"missing");Check(rc);Check(PowerReadDCValueIndex(0,ref id,ref Cpu,ref Epp,out var dc));
  var entries=new List<string>();
  foreach(var groupId in Enumerate(id,Guid.Empty,17)){
   var group=groupId;
   foreach(var settingId in Enumerate(id,group,18)){
    var setting=settingId;
    uint a=PowerReadACValueIndex(0,ref id,ref group,ref setting,out uint av),d=PowerReadDCValueIndex(0,ref id,ref group,ref setting,out uint dv);
    entries.Add($"{group}:{setting}:{a}:{av}:{d}:{dv}");
   }
  }
  entries.Sort(StringComparer.Ordinal);
  uint labelSize=0;var labelRc=PowerReadFriendlyName(0,ref id,0,0,null,ref labelSize);if(labelRc is 0 or 234&&labelSize is >0 and <65536){var label=new byte[labelSize];if(PowerReadFriendlyName(0,ref id,0,0,label,ref labelSize)==0)entries.Add(Convert.ToBase64String(label));}
  var hash=System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(string.Join("\n",entries)));
  return new(id,ac,dc,Convert.ToHexString(hash));
 }
 static List<Guid> Enumerate(Guid scheme,Guid group,uint access){
  var result=new List<Guid>();
  for(uint i=0;i<4096;i++){uint size=16;var rc=PowerEnumerate(0,ref scheme,ref group,access,i,out var id,ref size);if(rc==259)return result;Check(rc);if(size!=16)throw new IOException("Неверный GUID схемы");result.Add(id);}
  throw new IOException("Превышен предел перечисления питания");
 }
 [DllImport("powrprof.dll")]static extern uint PowerEnumerate(nint root,ref Guid scheme,ref Guid group,uint access,uint index,out Guid value,ref uint size);
 [DllImport("powrprof.dll")]static extern uint PowerWriteFriendlyName(nint root,ref Guid scheme,nint group,nint setting,byte[] buffer,uint size);
 [DllImport("powrprof.dll")]static extern uint PowerReadFriendlyName(nint root,ref Guid scheme,nint group,nint setting,byte[]? buffer,ref uint size);
 public void Activate(Guid id)=>Check(PowerSetActiveScheme(0,ref id));
 public void Delete(Guid id)=>Check(PowerDeleteScheme(0,ref id));
 [DllImport("powrprof.dll")]static extern uint PowerGetActiveScheme(nint root,out nint guid);
 [DllImport("powrprof.dll")]static extern uint PowerDuplicateScheme(nint root,ref Guid source,out nint destination);
 [DllImport("powrprof.dll")]static extern uint PowerSetActiveScheme(nint root,ref Guid scheme);
 [DllImport("powrprof.dll")]static extern uint PowerDeleteScheme(nint root,ref Guid scheme);
 [DllImport("powrprof.dll")]static extern uint PowerWriteACValueIndex(nint root,ref Guid scheme,ref Guid group,ref Guid setting,uint value);
 [DllImport("powrprof.dll")]static extern uint PowerWriteDCValueIndex(nint root,ref Guid scheme,ref Guid group,ref Guid setting,uint value);
 [DllImport("powrprof.dll")]static extern uint PowerReadACValueIndex(nint root,ref Guid scheme,ref Guid group,ref Guid setting,out uint value);
 [DllImport("powrprof.dll")]static extern uint PowerReadDCValueIndex(nint root,ref Guid scheme,ref Guid group,ref Guid setting,out uint value);
 [DllImport("kernel32.dll")]static extern nint LocalFree(nint p);
}
