using System.Runtime.InteropServices;
namespace QuietPC.Windows;
public sealed class Sensors:IDisposable {
 nint lib; bool initialized; nint gpu;
 [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate int Init();
 [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate int Handle(uint index,out nint handle);
 [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate int Uint(nint device,out uint value);
 T Get<T>(string n)where T:Delegate=>Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(lib,n));
 public string GpuStatus {get;private set;}="NVML недоступна";
 public Sensors(){
  // Only load NVIDIA's installed system DLL, never the working directory.
  try {string dll=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"nvml.dll");if(!File.Exists(dll))return;lib=NativeLibrary.Load(dll);int rc=Get<Init>("nvmlInit_v2")();if(rc!=0){GpuStatus=$"NVML init: {rc}";return;}initialized=true;rc=Get<Handle>("nvmlDeviceGetHandleByIndex_v2")(0,out gpu);GpuStatus=rc==0?"NVML GPU 0, только чтение":$"NVML device: {rc}";if(rc!=0)gpu=0; }catch(Exception e){GpuStatus=e.GetType().Name;}
 }
 public double? GpuWatts(){try{if(gpu!=0 && Get<Uint>("nvmlDeviceGetPowerUsage")(gpu,out var mw)==0 && mw>0)return mw/1000.0;}catch{}return null;}
 [StructLayout(LayoutKind.Sequential)]public struct Memory {public uint Length,Load;public ulong Total,Available,PageTotal,PageAvailable,VirtualTotal,VirtualAvailable,Extended;}
 [DllImport("kernel32.dll",SetLastError=true)]static extern bool GlobalMemoryStatusEx(ref Memory m);
 public static Memory Ram(){var m=new Memory{Length=(uint)Marshal.SizeOf<Memory>()};if(!GlobalMemoryStatusEx(ref m))throw new System.ComponentModel.Win32Exception();return m;}

 public void Dispose(){if(initialized){try{Get<Init>("nvmlShutdown")();}catch{}}if(lib!=0)NativeLibrary.Free(lib);}
}
