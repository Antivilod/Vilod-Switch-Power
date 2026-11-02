using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
namespace QuietPC.App;
public partial class MainWindow {
 void BackgroundOptimizer(){
  var window=Panel("VSP · Фоновые приложения",570,730,out var body);window.MaxHeight=SystemParameters.WorkArea.Height-24;
  Paragraph(body,"Выберите свои фоновые приложения. В профилях Game Boost и Соревновательный VSP сможет временно понижать их приоритет, пока идёт игра. При выключении вернёт прежние приоритеты. Это не закрывает приложения и не приостанавливает загрузки.");
  var items=new List<(System.Windows.Controls.CheckBox Check,string Path)>();
  string windows=Environment.GetFolderPath(Environment.SpecialFolder.Windows)+Path.DirectorySeparatorChar;
  string[] excluded=["steam","riot","vanguard","vgc","vgk","faceit","anticheat","battleye","defender","msmpeng","security","amnezia","nvcontainer","codex","chatgpt"];
  using var self=Process.GetCurrentProcess();var paths=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
  foreach(var process in Process.GetProcesses()){using(process){try{
   if(process.Id==self.Id||process.SessionId!=self.SessionId||process.MainWindowHandle==0)continue;
   var path=GameDetector.Image(process.Id);if(path is null||path.StartsWith(windows,StringComparison.OrdinalIgnoreCase)||excluded.Any(t=>path.Contains(t,StringComparison.OrdinalIgnoreCase))||gamePaths.Contains(path,StringComparer.OrdinalIgnoreCase)||!paths.Add(path))continue;
   var check=new System.Windows.Controls.CheckBox{Content=process.ProcessName+$" · {process.WorkingSet64/1048576.0:F0} МБ",IsChecked=prefs.BackgroundApps.Contains(path,StringComparer.OrdinalIgnoreCase),Foreground=Theme.Brush("TextPrimary"),ToolTip=path,Margin=new Thickness(0,6,0,8)};items.Add((check,path));body.Children.Add(check);
  }catch{}}}
  if(items.Count==0)Paragraph(body,"Подходящие приложения с окнами не найдены. Исполняемый файл можно выбрать вручную в Game Boost.");
  ActionButton(body,"Добавить отмеченные в Game Boost",()=>{foreach(var item in items.Where(i=>i.Check.IsChecked==true))if(!prefs.BackgroundApps.Contains(item.Path,StringComparer.OrdinalIgnoreCase)){if(prefs.BackgroundApps.Count>=100)throw new InvalidOperationException("Достигнут предел фоновых приложений");prefs.BackgroundApps.Add(item.Path);}Save();scanned=DateTimeOffset.MinValue;window.Close();});
  ActionButton(body,"Открыть настройки Game Boost",()=>{window.Close();GameBoostPanel();});
  Paragraph(body,"Системные службы, защита, античиты и сетевые компоненты автоматически не меняются. Приоритет может не измениться, если приложение защищено Windows или самостоятельно управляет им.");window.Show();
 }
}
