using System.Runtime.InteropServices;
namespace QuietPC.App;
// Windows' processor-performance counter includes Turbo above nominal frequency.
// This is an OS estimate, not an effective per-core hardware clock measurement.
sealed class CpuClockSampler:IDisposable {
 nint query,counter;
 public CpuClockSampler(){try{if(PdhOpenQuery(null,0,out query)!=0||PdhAddEnglishCounter(query,@"\Processor Information(_Total)\% Processor Performance",0,out counter)!=0||PdhCollectQueryData(query)!=0)Dispose();}catch{Dispose();}}
 public double? Read(double? nominalGhz){
  try{if(query==0||nominalGhz is not double nominal||PdhCollectQueryData(query)!=0||PdhGetFormattedCounterValue(counter,0x200|0x8000,out _,out var value)!=0||value.Status>1||!double.IsFinite(value.Number)||value.Number<=0)return null;
   double ghz=nominal*value.Number/100;return ghz is >0.05 and <=10?ghz:null;
  }catch{return null;}
 }
 public void Dispose(){var handle=Interlocked.Exchange(ref query,0);if(handle!=0)try{PdhCloseQuery(handle);}catch{}}
 [StructLayout(LayoutKind.Explicit,Size=16)]struct CounterValue{[FieldOffset(0)]public uint Status;[FieldOffset(8)]public double Number;}
 [DefaultDllImportSearchPaths(DllImportSearchPath.System32)][DllImport("pdh.dll",EntryPoint="PdhOpenQueryW",CharSet=CharSet.Unicode)]static extern uint PdhOpenQuery(string? source,nint user,out nint query);
 [DefaultDllImportSearchPaths(DllImportSearchPath.System32)][DllImport("pdh.dll",EntryPoint="PdhAddEnglishCounterW",CharSet=CharSet.Unicode)]static extern uint PdhAddEnglishCounter(nint query,string path,nint user,out nint counter);
 [DefaultDllImportSearchPaths(DllImportSearchPath.System32)][DllImport("pdh.dll")]static extern uint PdhCollectQueryData(nint query);
 [DefaultDllImportSearchPaths(DllImportSearchPath.System32)][DllImport("pdh.dll")]static extern uint PdhGetFormattedCounterValue(nint counter,uint format,out uint type,out CounterValue value);
 [DefaultDllImportSearchPaths(DllImportSearchPath.System32)][DllImport("pdh.dll")]static extern uint PdhCloseQuery(nint query);
}
