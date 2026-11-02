using System.Runtime.InteropServices;
using System.Text;
using QuietPC.Core;
namespace QuietPC.Windows;
// ABI verified against NVIDIA nvapi.h / nvapi_interface.h, retrieved 2026-10-16.
// No NVIDIA DLL or SDK code is redistributed. Installed System32 nvapi64.dll only.
public sealed class NvidiaDrs:INvidiaDrs {
 public const string GlobalMarker="::VSP_GLOBAL::";
 const uint Frl=0x10835002;
 const int SettingSize=12320,ValueOffset=8220,AppSize=12296,ProfileSize=4116;
 nint dll,session;
 [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate nint Query(uint id);
 [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate int Zero();
 [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate int Create(out nint s);
 [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate int One(nint s);
 [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate int Base(nint s,out nint profile);
 [UnmanagedFunctionPointer(CallingConvention.Cdecl,CharSet=CharSet.Unicode)]delegate int Find(nint s,[MarshalAs(UnmanagedType.LPWStr)]string app,out nint profile,nint data);
 [UnmanagedFunctionPointer(CallingConvention.Cdecl,CharSet=CharSet.Unicode)]delegate int FindName(nint s,[MarshalAs(UnmanagedType.LPWStr)]string name,out nint profile);
 [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate int ProfileInfo(nint s,nint profile,nint data);
 [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate int GetSetting(nint s,nint profile,uint id,nint data);
 [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate int SetSetting(nint s,nint profile,nint data);
 [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate int Remove(nint s,nint profile,uint id);
 Query query=null!;
 T Api<T>(uint id)where T:Delegate {var p=query(id);if(p==0)throw new NotSupportedException($"NVAPI interface {id:X}");return Marshal.GetDelegateForFunctionPointer<T>(p);}
 static void Check(int rc){if(rc!=0)throw new IOException($"NVAPI DRS: {rc}");}
 static nint Buffer(int size,int version){var p=Marshal.AllocHGlobal(size);Marshal.Copy(new byte[size],0,p,size);Marshal.WriteInt32(p,size|(version<<16));return p;}
 public NvidiaDrs(){
  try {dll=NativeLibrary.Load(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"nvapi64.dll"));query=Marshal.GetDelegateForFunctionPointer<Query>(NativeLibrary.GetExport(dll,"nvapi_QueryInterface"));Check(Api<Zero>(0x0150e828)());Check(Api<Create>(0x0694d52e)(out session));Reload();}
  catch{Dispose();throw;}
 }
 public void Reload()=>Check(Api<One>(0x375dbd6b)(session));
 public void Save()=>Check(Api<One>(0xfcbc7e14)(session));
 public string ProfileFor(string path){
  if(path==GlobalMarker)return GlobalMarker;
  var info=InspectProfile(path);if(info.Applications!=1)throw new NotSupportedException("DRS-профиль содержит несколько приложений; изменение пропущено");return info.Name;
 }
 public (string Name,int Applications,DrsValue Setting) InspectProfile(string path){
  var app=Buffer(AppSize,1);var info=Buffer(ProfileSize,1);
  try{Check(Api<Find>(0xeee566b2)(session,path,out var profile,app));Check(Api<ProfileInfo>(0x61cd6fd6)(session,profile,info));
   var name=Marshal.PtrToStringUni(info+4,2048)!.TrimEnd('\0');return(name,Marshal.ReadInt32(info,4108),Read(name));
  }finally{Marshal.FreeHGlobal(app);Marshal.FreeHGlobal(info);}
 }
 nint ByName(string name){if(name==GlobalMarker){Check(Api<Base>(0xda8466a0)(session,out var global));return global;}Check(Api<FindName>(0x7e4a9a0b)(session,name,out var profile));return profile;}
 public DrsValue Read(string name){
  var data=Buffer(SettingSize,1);
  try{int rc=Api<GetSetting>(0x73bf8338)(session,ByName(name),Frl,data);if(rc==-160)return new(false,0,0,false);
   Check(rc);if(Marshal.ReadInt32(data,4104)!=0)throw new NotSupportedException("FRL не DWORD");
   return new(true,unchecked((uint)Marshal.ReadInt32(data,ValueOffset)),Marshal.ReadInt32(data,4108),Marshal.ReadInt32(data,4112)!=0);
  }finally{Marshal.FreeHGlobal(data);}
 }
 public void Write(string name,uint fps){
  if(fps is <20 or >500)throw new ArgumentOutOfRangeException(nameof(fps));
  Set(name,fps);
 }
 void Set(string name,uint value){var p=Buffer(SettingSize,1);try{Marshal.WriteInt32(p,4100,(int)Frl);Marshal.WriteInt32(p,ValueOffset,(int)value);Check(Api<SetSetting>(0x577dd202)(session,ByName(name),p));}finally{Marshal.FreeHGlobal(p);}}
 public void Revert(string name,DrsValue before){
  if(before.Exists&&before.Location==0&&!before.Predefined)Set(name,before.Value);
  else if(before.Exists&&before.Location==0&&before.Predefined)Check(Api<Remove>(0x53f0381e)(session,ByName(name),Frl));
  else Check(Api<Remove>(0xe4a26362)(session,ByName(name),Frl));
 }
 public void Dispose(){if(session!=0){try{Api<One>(0xdad9cff8)(session);}catch{}session=0;}if(dll!=0){try{Api<Zero>(0xd22bdd7e)();}catch{}NativeLibrary.Free(dll);dll=0;}}
}
