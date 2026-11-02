using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;
using QuietPC.Windows;
namespace QuietPC.App;
sealed record HardwareCapabilities(string Fingerprint,string Cpu,string[] Graphics,bool NvidiaPower,double? MinimumGpuWatts,double? MaximumGpuWatts,string CpuPowerStatus){
 public static HardwareCapabilities Read(){
  string cpu=Registry.GetValue(@"HKEY_LOCAL_MACHINE\HARDWARE\DESCRIPTION\System\CentralProcessor\0","ProcessorNameString","CPU")?.ToString()?.Trim()??"CPU";
  var graphics=new List<string>();for(uint i=0;i<32;i++){var device=new DisplayDevice{Size=Marshal.SizeOf<DisplayDevice>(),Name="",Description="",Id="",Key=""};if(!EnumDisplayDevices(null,i,ref device,0))break;if((device.Flags&8)==0&&!device.Description.Contains("Remote",StringComparison.OrdinalIgnoreCase))graphics.Add(device.Description);}
  string machine;try{machine=Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Cryptography","MachineGuid",Environment.MachineName)?.ToString()??Environment.MachineName;}catch{machine=Environment.MachineName;}
  string hash=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(machine+"|"+cpu+"|"+string.Join("|",graphics)))).ToLowerInvariant()[..16];
  bool supported=false;double? min=null,max=null;try{using var api=new NvidiaPower();var devices=api.Devices();if(devices.Length==1){supported=true;min=devices[0].Minimum/1000.0;max=devices[0].Maximum/1000.0;}}catch{}
  return new(hash,cpu,graphics.ToArray(),supported,min,max,"Лимит CPU в ваттах недоступен: нет проверенного совместимого провайдера управления. Используется политика энергосбережения Windows; это не PL1/PL2.");
 }
 [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]struct DisplayDevice{public int Size;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=32)]public string Name;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=128)]public string Description;public uint Flags;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=128)]public string Id;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=128)]public string Key;}
 [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern bool EnumDisplayDevices(string? device,uint number,ref DisplayDevice info,uint flags);
}
public partial class MainWindow {
 void HardwarePanel(){
  var h=HardwareCapabilities.Read();var window=Panel("VSP · Возможности компьютера",560,650,out var body);
  Paragraph(body,h.Cpu+"\n"+string.Join("\n",h.Graphics));
  Paragraph(body,"Настройки этого компьютера: "+h.Fingerprint+"\nВ portable настройки разных устройств хранятся отдельно.");
  Paragraph(body,h.NvidiaPower?$"Управление GPU доступно. Допустимый диапазон: {h.MinimumGpuWatts:F0}–{h.MaximumGpuWatts:F0} Вт. Лимит FPS зависит от профилей NVIDIA.":"Управление мощностью GPU не подтверждено. Автоподбор отключён; мониторинг и политика CPU могут оставаться доступны.");
  Paragraph(body,h.CpuPowerStatus);
  Paragraph(body,"Температура CPU требует отдельного доступного источника датчиков. Общее потребление ПК из розетки не измеряется. Для него нужен внешний ваттметр.");
  ActionButton(body,"Самопроверка применённой политики CPU",PowerCheck);
  ActionButton(body,"Мониторинг",Monitoring);window.Show();
 }
}
