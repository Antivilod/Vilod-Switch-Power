using System.Runtime.InteropServices;
using System.Text;
using QuietPC.Core;
namespace QuietPC.Windows;
public sealed class NvidiaPower:IGpuPower {
 nint dll;bool initialized;
 [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate int Empty();
 [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate int Count(out uint count);
 [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate int Handle(uint index,out nint device);
 [UnmanagedFunctionPointer(CallingConvention.Cdecl,CharSet=CharSet.Ansi)]delegate int ByUuid([MarshalAs(UnmanagedType.LPStr)]string id,out nint device);
 [UnmanagedFunctionPointer(CallingConvention.Cdecl,CharSet=CharSet.Ansi)]delegate int Uuid(nint device,StringBuilder text,uint length);
 [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate int Value(nint device,out uint value);
 [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate int Range(nint device,out uint minimum,out uint maximum);
 [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate int SetValue(nint device,uint value);
 T Api<T>(string name)where T:Delegate=>Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(dll,name));
 static void Check(int rc){if(rc!=0)throw new NotSupportedException($"NVML: {rc}");}
 public NvidiaPower(){try{dll=NativeLibrary.Load(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"nvml.dll"));Check(Api<Empty>("nvmlInit_v2")());initialized=true;}catch{Dispose();throw;}}
 public GpuState[] Devices(){Check(Api<Count>("nvmlDeviceGetCount_v2")(out uint count));var result=new List<GpuState>();for(uint i=0;i<Math.Min(count,16);i++){
  if(Api<Handle>("nvmlDeviceGetHandleByIndex_v2")(i,out var device)!=0)continue;
  var id=new StringBuilder(96);if(Api<Uuid>("nvmlDeviceGetUUID")(device,id,96)!=0)continue;
  if(Api<Range>("nvmlDeviceGetPowerManagementLimitConstraints")(device,out uint min,out uint max)!=0||min==0||max<min)continue;
  if(Api<Value>("nvmlDeviceGetPowerManagementLimit")(device,out uint current)!=0)continue;result.Add(new(id.ToString(),current,min,max));
 }return [..result];}
 nint Device(string id){Check(Api<ByUuid>("nvmlDeviceGetHandleByUUID")(id,out var d));return d;}
 public uint Read(string id){Check(Api<Value>("nvmlDeviceGetPowerManagementLimit")(Device(id),out var value));return value;}
 public void Set(string id,uint value){var d=Device(id);Check(Api<Range>("nvmlDeviceGetPowerManagementLimitConstraints")(d,out uint min,out uint max));if(value<min||value>max)throw new ArgumentOutOfRangeException(nameof(value));Check(Api<SetValue>("nvmlDeviceSetPowerManagementLimit")(d,value));}
 public void Dispose(){if(initialized){try{Api<Empty>("nvmlShutdown")();}catch{}}if(dll!=0)NativeLibrary.Free(dll);dll=0;initialized=false;}
}
