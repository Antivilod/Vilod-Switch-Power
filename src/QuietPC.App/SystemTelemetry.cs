using System.Diagnostics;
using System.IO;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using QuietPC.Windows;
namespace QuietPC.App;
public sealed record MonitorMetric(string Id,string Group,string Label,string? Value,double? Number=null,string? Unit=null);
public sealed class SystemMonitor {
 ulong idle,kernel,user;DateTimeOffset last=DateTimeOffset.UtcNow;
 readonly NetworkInterface[] network=NetworkInterface.GetAllNetworkInterfaces().Where(n=>n.NetworkInterfaceType!=NetworkInterfaceType.Loopback).ToArray();
 readonly Dictionary<string,(long Rx,long Tx)> previous=[];
 readonly DriveInfo[] disks=DriveInfo.GetDrives().Where(d=>d.DriveType==DriveType.Fixed).ToArray();
 readonly int session=Process.GetCurrentProcess().SessionId;
 readonly string cpu=Registry.GetValue(@"HKEY_LOCAL_MACHINE\HARDWARE\DESCRIPTION\System\CentralProcessor\0","ProcessorNameString","Процессор")?.ToString()?.Trim()??"Процессор";
 DateTimeOffset processesAt=DateTimeOffset.MinValue;string? processesText;
 DateTimeOffset limitsAt=DateTimeOffset.MinValue;string? limitsText;
 public List<MonitorMetric> Read(Sensors sensors,bool processDetails=true){
  var now=DateTimeOffset.UtcNow;double seconds=Math.Max(.1,(now-last).TotalSeconds);last=now;
  var values=new List<MonitorMetric>();void Add(string id,string group,string name,string? value,double? number=null,string? unit=null)=>values.Add(new(id,group,name,value,number,unit));
  Add("cpu.name","Процессор","Модель",cpu);Add("cpu.threads","Процессор","Логических процессоров",Environment.ProcessorCount.ToString());
  if(GetSystemTimes(out ulong i,out ulong k,out ulong u)){ulong total=(k-kernel)+(u-user);double? load=kernel==0||total==0?null:Math.Clamp(100.0*(1-(double)(i-idle)/total),0,100);idle=i;kernel=k;user=u;Add("cpu.load","Процессор","Загрузка",load is {} v?$"{v:F1}%":null,load,"%");}
  Add("cpu.clock","Процессор","Частота по данным Windows",Clock());
  Add("cpu.temperature","Процессор","Температура",null);Add("cpu.power","Процессор","Мощность",null);
  var ram=Sensors.Ram();Add("ram.load","Память","Занято RAM",$"{ram.Load}% · {(ram.Total-ram.Available)/1073741824.0:F1} из {ram.Total/1073741824.0:F1} ГБ",ram.Load,"%");
  Add("ram.available","Память","Доступно RAM",$"{ram.Available/1073741824.0:F1} ГБ",ram.Available/1073741824.0,"ГБ");
  Add("ram.commit","Память","Лимит фиксации памяти",$"{ram.PageTotal/1073741824.0:F1} ГБ");
  var watts=sensors.GpuWatts();Add("gpu.current","Видеокарта","Текущий расход GPU 0",watts is {} w?$"{w:F1} Вт":null,watts,"Вт");
  if(now-limitsAt>=TimeSpan.FromSeconds(5)){limitsAt=now;try{using var api=new NvidiaPower();limitsText=string.Join(" · ",api.Devices().Select((d,index)=>$"GPU {index}: {d.Current/1000.0:F0} Вт"));}catch{limitsText=null;}}
  Add("gpu.cap","Видеокарта","Установленный потолок",limitsText);
  foreach(var disk in disks){try{if(!disk.IsReady)continue;double used=(double)(disk.TotalSize-disk.AvailableFreeSpace)/disk.TotalSize;Add("disk."+disk.Name,"Диски",disk.Name+" "+disk.VolumeLabel,$"{disk.AvailableFreeSpace/1073741824.0:F1} ГБ свободно из {disk.TotalSize/1073741824.0:F0} · занято {used:P0} · {disk.DriveFormat}",used*100,"%");}catch{}}
  foreach(var nic in network){try{if(nic.OperationalStatus!=OperationalStatus.Up||nic.GetIPProperties().UnicastAddresses.Count==0)continue;var s=nic.GetIPStatistics();var old=previous.GetValueOrDefault(nic.Id,(s.BytesReceived,s.BytesSent));previous[nic.Id]=(s.BytesReceived,s.BytesSent);Add("net."+nic.Id,"Сеть",nic.Name,$"↓ {Rate(Math.Max(0,s.BytesReceived-old.Item1)/seconds)} · ↑ {Rate(Math.Max(0,s.BytesSent-old.Item2)/seconds)} · канал {nic.Speed/1000000.0:F0} Мбит/с");Add("net.rx."+nic.Id,"Сеть",nic.Name+" · приём",$"{Math.Max(0,s.BytesReceived-old.Item1)/seconds/1048576:F3} МБ/с",Math.Max(0,s.BytesReceived-old.Item1)/seconds/1048576,"МБ/с");Add("net.tx."+nic.Id,"Сеть",nic.Name+" · отправка",$"{Math.Max(0,s.BytesSent-old.Item2)/seconds/1048576:F3} МБ/с",Math.Max(0,s.BytesSent-old.Item2)/seconds/1048576,"МБ/с");}catch{}}
  if(processDetails&&now-processesAt>=TimeSpan.FromSeconds(10)){var entries=new List<(string Name,long Ram)>();foreach(var process in Process.GetProcesses()){using(process){try{if(process.SessionId!=session)continue;entries.Add((process.ProcessName,process.WorkingSet64));}catch{}}}processesText=string.Join("\n",entries.OrderByDescending(p=>p.Ram).Take(8).Select(p=>$"{p.Name} · {p.Ram/1048576.0:F0} МБ"));processesAt=now;}
  Add("process.memory","Процессы","Крупнейшие по RAM",processDetails?processesText:null);
  var os=Environment.OSVersion.Version;Add("system.os","Система","Windows",$"{(os.Build>=22000?"Windows 11":"Windows 10")} · сборка {os.Build} · {RuntimeInformation.OSArchitecture}");
  Add("system.uptime","Система","Время работы",TimeSpan.FromMilliseconds(Environment.TickCount64).ToString(@"d\д\ hh\:mm\:ss"));
  Add("game.fps","Игра","Фактический FPS",null);Add("game.dlss","Игра","DLSS",null);Add("game.fsr","Игра","FSR / XeSS",null);
  return values;
 }
 static string Rate(double bytes)=>bytes>=1048576?$"{bytes/1048576:F2} МБ/с":$"{bytes/1024:F1} КБ/с";
 static string? Clock(){try{var data=new CpuPower[Environment.ProcessorCount];if(CallNtPowerInformation(11,0,0,data,(uint)(data.Length*Marshal.SizeOf<CpuPower>()))!=0)return null;uint min=data.Min(c=>c.CurrentMhz),max=data.Max(c=>c.CurrentMhz);return min==max?$"{max} МГц":$"{min}-{max} МГц";}catch{return null;}}
 [StructLayout(LayoutKind.Sequential)]struct CpuPower{public uint Number,MaxMhz,CurrentMhz,LimitMhz,MaxIdle,CurrentIdle;}
 [DllImport("kernel32.dll")]static extern bool GetSystemTimes(out ulong idle,out ulong kernel,out ulong user);
 [DllImport("powrprof.dll")]static extern uint CallNtPowerInformation(int level,nint input,uint size,[Out]CpuPower[] output,uint length);
 public static double? NominalCpuGhz(){try{var data=new CpuPower[Environment.ProcessorCount];if(CallNtPowerInformation(11,0,0,data,(uint)(data.Length*Marshal.SizeOf<CpuPower>()))!=0)return null;var values=data.Select(c=>c.MaxMhz).Where(n=>n>0&&n<=10000).ToArray();return values.Length>0?values.Max()/1000.0:null;}catch{return null;}}
 public static string Display(string name){var mode=new DisplayMode{Size=(ushort)Marshal.SizeOf<DisplayMode>(),DeviceName="",FormName=""};return EnumDisplaySettings(name,-1,ref mode)?$"{mode.Width} × {mode.Height} · {mode.Frequency} Гц · {mode.Bits} бит":"Параметры не получены";}
 [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]struct DisplayMode{
  [MarshalAs(UnmanagedType.ByValTStr,SizeConst=32)]public string DeviceName;
  public ushort Spec,Driver,Size,Extra;public uint Fields;public int X,Y;public uint Orientation,Fixed;
  public short Color,Duplex,YResolution,TTOption,Collate;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=32)]public string FormName;
  public ushort LogPixels;public uint Bits,Width,Height,Flags,Frequency,IcmMethod,IcmIntent,Media,Dither,Reserved1,Reserved2,PanningWidth,PanningHeight;
 }
 [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern bool EnumDisplaySettings(string device,int number,ref DisplayMode mode);
}
