using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
namespace QuietPC.App;
public partial class MainWindow {
 async void GamingAudit(){
  var window=Panel("VSP · Проверка для игр",700,740,out var body);
  Paragraph(body,"Проверка всего компьютера. Отчёт помогает найти фоновую нагрузку; изменения служб, защиты и настроек игры автоматически не выполняются.");
  var info=new TextBox{IsReadOnly=true,TextWrapping=TextWrapping.Wrap,AcceptsReturn=true,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Height=420,Text="Измеряю фоновую нагрузку…"};body.Children.Add(info);
  ActionButton(body,"Игровой режим Windows",()=>Process.Start(new ProcessStartInfo("ms-settings:gaming-gamemode"){UseShellExecute=true}));
  ActionButton(body,"Автозагрузка Windows",()=>Process.Start(new ProcessStartInfo("ms-settings:startupapps"){UseShellExecute=true}));
  ActionButton(body,"Настройки Game Boost в VSP",()=>{window.Close();GameBoostPanel();});
  ActionButton(body,"Сохранить отчёт",()=>{var pick=new SaveFileDialog{Filter="Отчёт VSP|*.txt",FileName="VSP-game-audit.txt"};if(pick.ShowDialog()==true)File.WriteAllText(pick.FileName,info.Text);});
  window.Show();try{info.Text=await Task.Run(AuditReport);}catch(Exception ex){info.Text="Проверка не завершена: "+ex.Message;}
 }
 static string AuditReport(){
  var initial=new Dictionary<int,(long Start,double Cpu)>();
  foreach(var process in Process.GetProcesses()){using(process){try{initial[process.Id]=(process.StartTime.ToUniversalTime().Ticks,process.TotalProcessorTime.TotalMilliseconds);}catch{}}}
  var watch=Stopwatch.StartNew();Thread.Sleep(1200);double duration=watch.Elapsed.TotalMilliseconds;
  var rows=new List<(string Name,double Cpu,double Ram)>();
  foreach(var process in Process.GetProcesses()){using(process){try{if(initial.TryGetValue(process.Id,out var old)&&old.Start==process.StartTime.ToUniversalTime().Ticks)rows.Add((process.ProcessName,Math.Max(0,process.TotalProcessorTime.TotalMilliseconds-old.Cpu)/duration/Environment.ProcessorCount*100,process.WorkingSet64/1048576.0));}catch{}}}
  var text=new StringBuilder("VSP · Проверка компьютера для игр\n").AppendLine(DateTimeOffset.Now.ToString("u")).AppendLine("\nСамые активные процессы CPU (короткий замер, доля всего процессора):");
  foreach(var row in rows.OrderByDescending(r=>r.Cpu).Take(12))text.AppendLine($"{row.Name}: CPU {row.Cpu:F1}% · RAM {row.Ram:F0} МБ");
  text.AppendLine("\nСлужбы с автоматическим запуском вне системной папки (наличие не означает, что служба лишняя):");
  using(var services=Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services")){
   if(services is not null)foreach(var name in services.GetSubKeyNames()){
    using var service=services.OpenSubKey(name);if(service?.GetValue("Start") is not int start||start!=2||service.GetValue("Type") is not int type||(type&0x30)==0)continue;
    string path=Environment.ExpandEnvironmentVariables(service.GetValue("ImagePath")?.ToString()??"");if(path.Length==0||path.Contains(Environment.GetFolderPath(Environment.SpecialFolder.Windows),StringComparison.OrdinalIgnoreCase)||path.StartsWith(@"\SystemRoot",StringComparison.OrdinalIgnoreCase))continue;
    text.AppendLine($"{service.GetValue("DisplayName")??name} [{name}]\n  {path}");
   }
  }
  text.AppendLine("\nРекомендации:\n• Включите игровой режим Windows, если он выключен.\n• Проверьте ненужную автозагрузку и фоновые загрузки перед игрой.\n• Выбирайте FPS-профиль до запуска игры.\n• При GPU-нагрузке около 100% в 4K основное влияние имеют графические настройки игры.\n• Game Boost VSP меняет политику питания и приоритеты выбранных процессов с восстановлением; прирост FPS не гарантируется.\n\nСлужбы Steam, античитов, сети, звука, обновлений и защиты не отключаются. Реестр задержек, HPET, BCD и файлы игр не изменяются. Отчёт не является измерением энергопотребления всего ПК.");
  return text.ToString();
 }
}
