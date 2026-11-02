using System.Text.Json;
using LibreHardwareMonitor.Hardware;
using Microsoft.Win32;
record Metric(string Id,string Group,string Label,string? Value,double? Number=null,string? Unit=null);
static class Program {
 static int Main(string[] args){
  bool advanced=args.Contains("--advanced"),once=args.Contains("--once");
  int refresh=2000,index=Array.IndexOf(args,"--interval");if(index>=0&&index+1<args.Length&&int.TryParse(args[index+1],out int seconds)&&new[]{1,2,5,10}.Contains(seconds))refresh=seconds*1000;
  // The basic mode never opens CPU MSR / motherboard / SMBus / controller access.
  // Additional sensors are opt-in and only attempted when PawnIO is already installed.
  bool pawn=Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\PawnIO") is {} key&&CloseKey(key);
  var computer=new Computer{IsGpuEnabled=true,IsMemoryEnabled=false,IsStorageEnabled=true,IsNetworkEnabled=false,IsCpuEnabled=advanced&&pawn,IsMotherboardEnabled=false,IsControllerEnabled=false};
  try{
   computer.Open();
   do{
    var values=new List<Metric>();foreach(var hardware in computer.Hardware)Read(hardware,values);
    Console.WriteLine(JsonSerializer.Serialize(new{Metrics=values,Status=advanced&&!pawn?"Температура CPU недоступна: дополнительный драйвер PawnIO не установлен. Драйвер автоматически не устанавливается.":advanced?"Дополнительные датчики включены; неподдерживаемые значения скрыты":"Базовый мониторинг. Доступ к CPU MSR и датчикам платы отключён."}));
    if(once)break;Thread.Sleep(refresh);
    int parentIndex=Array.IndexOf(args,"--parent");if(parentIndex>=0&&parentIndex+1<args.Length){try{using var parent=System.Diagnostics.Process.GetProcessById(int.Parse(args[parentIndex+1]));if(parent.HasExited)break;}catch{break;}}
   }while(true);
   return 0;
  }catch(Exception ex){Console.WriteLine(JsonSerializer.Serialize(new{Metrics=Array.Empty<Metric>(),Status="Датчики недоступны: "+ex.Message}));return 1;}
  finally{computer.Close();}
 }
 static bool CloseKey(RegistryKey key){key.Dispose();return true;}
 static void Read(IHardware hardware,List<Metric> result){
  hardware.Update();
  string group=hardware.HardwareType switch{HardwareType.Cpu=>"Процессор",HardwareType.Memory=>"Память",HardwareType.Storage=>"Диски",_=>hardware.Name};
  foreach(var sensor in hardware.Sensors){if(sensor.Value is not float n||!float.IsFinite(n))continue;
   string? unit=sensor.SensorType switch{SensorType.Temperature=>"°C",SensorType.Load=>"%",SensorType.Power=>"Вт",SensorType.Clock=>"МГц",SensorType.Fan=>"об/мин",SensorType.Voltage=>"В",SensorType.Data=>"ГБ",SensorType.SmallData=>"МБ",SensorType.Throughput=>"Б/с",SensorType.Control=>"%",SensorType.Level=>"%",_=>null};
   if(unit is null||sensor.Name.StartsWith("D3D ",StringComparison.Ordinal)&&sensor.SensorType==SensorType.Load&&n==0)continue;string label=sensor.Name switch{"GPU Core"=>"Ядро GPU","GPU Hot Spot"=>"Горячая точка GPU","GPU Memory"=>"Видеопамять","GPU Package"=>"Текущий расход GPU","GPU Memory Total"=>"Объём видеопамяти","GPU Memory Free"=>"Свободная видеопамять","GPU Memory Used"=>"Занятая видеопамять","GPU Core Voltage"=>"Напряжение ядра GPU",_=>sensor.Name};
   label+=" · "+(sensor.SensorType switch{SensorType.Temperature=>"температура",SensorType.Load=>"загрузка",SensorType.Clock=>"частота",SensorType.Power=>"мощность",_=>unit});
   string category=sensor.SensorType switch{SensorType.Temperature or SensorType.Fan or SensorType.Control=>"Температуры и охлаждение",SensorType.Clock or SensorType.Power or SensorType.Voltage=>"Частоты и питание",SensorType.Data or SensorType.SmallData=>"Видеопамять",_=>"Загрузка"};
   string section=hardware.HardwareType is HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel?group+" · "+category:group;
   result.Add(new(sensor.Identifier+"/"+sensor.Name,section,label,$"{n:F1} {unit}",n,unit));
  }
  foreach(var sub in hardware.SubHardware)Read(sub,result);
 }
}
