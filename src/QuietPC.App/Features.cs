using System.IO;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using QuietPC.Core;
using QuietPC.Windows;
using Button=System.Windows.Controls.Button;
using CheckBox=System.Windows.Controls.CheckBox;
using TextBox=System.Windows.Controls.TextBox;
namespace QuietPC.App;
public partial class MainWindow {
 static readonly string DataDirectory=PortableMode.DataDirectory;
 Window Panel(string title,int width,int height,out StackPanel body){
  var w=new Window{Owner=this,Title=title,Width=width,Height=height,WindowStartupLocation=WindowStartupLocation.CenterOwner,Background=Theme.Brush("AppBg")};body=new(){Margin=new Thickness(24)};w.Content=new ScrollViewer{Content=body,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};Theme.ConfigureWindow(w);return w;
 }
 static void ActionButton(Panel panel,string title,Action action){var b=new Button{Content=title,Margin=new Thickness(0,8,0,0)};b.Click+=(_,_)=>{try{action();}catch(Exception e){System.Windows.MessageBox.Show(e.Message,"VSP");}};panel.Children.Add(b);}
 static void Paragraph(Panel panel,string text){panel.Children.Add(new TextBlock{Text=text,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,12)});}
 string MemoryScheduleText(){if(!prefs.AutoClean)return "\n\nАвтоочистка отключена";if(!enabled)return "\n\nВключите VSP для автоочистки";if(prefs.PauseCleanInGames&&knownGame)return "\n\nАвтоочистка отложена: идёт игра";var when=memorySchedule.NextAt(prefs.CleanIntervalMinutes);return "\n\n"+(prefs.MemoryTrigger=="Threshold"?$"Порог автоочистки: {prefs.MemoryThreshold}%":when is {} t?$"Следующая очистка: {t.LocalDateTime:HH:mm}":$"Очистка каждые {prefs.CleanIntervalMinutes} мин");}
 void Memory(){
  var window=Panel("VSP · Память",470,720,out var body);var info=new TextBlock{TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,12)};body.Children.Add(info);
  void Refresh(){var m=Sensors.Ram();var level=MemoryLevels.Classify(m.Load,prefs.WarningThreshold(),prefs.MemoryThreshold);info.Foreground=Theme.Brush(level==MemoryLevel.Critical?"MemoryCritical":level==MemoryLevel.Warning?"MemoryWarning":"TextPrimary");info.Text=(level==MemoryLevel.Critical?"Критический уровень памяти\n":level==MemoryLevel.Warning?"Опасный уровень памяти\n":"Память в норме\n")+$"Занято RAM: {(m.Total-m.Available)/1073741824.0:F1} ГБ\nДоступно: {m.Available/1073741824.0:F1} ГБ\n\n"+(level==MemoryLevel.Normal?"Очистка не требуется.":level==MemoryLevel.Warning?"Проверьте фоновую нагрузку.":"Достигнут критический порог. Очистка - по выбранному условию.")+"\nПамять активных игр и браузера не очищается."+MemoryScheduleText();}
  Refresh();var lastResult=new TextBlock{Text=memoryStatus,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,8)};body.Children.Add(lastResult);
  var limits=new Grid{Margin=new Thickness(0,12,0,8)};for(int i=0;i<3;i++)limits.ColumnDefinitions.Add(new ColumnDefinition());body.Children.Add(limits);
  TextBox Field(string label,string value,int column){var group=new StackPanel{Margin=new Thickness(0,0,column==2?0:10,0)};Grid.SetColumn(group,column);group.Children.Add(new TextBlock{Text=label,FontSize=12,Margin=new Thickness(0,0,0,6)});var box=new TextBox{Text=value};group.Children.Add(box);limits.Children.Add(group);return box;}
  var minutes=Field("Интервал, мин",prefs.CleanIntervalMinutes.ToString(),0);var warning=Field("Опасный, %",prefs.WarningThreshold().ToString(),1);var critical=Field("Критический, %",prefs.MemoryThreshold.ToString(),2);
  var autoClean=new CheckBox{Content="Автоматическая очистка",IsChecked=prefs.AutoClean,Foreground=Theme.Brush("TextPrimary"),Margin=new Thickness(0,8,0,4)};body.Children.Add(autoClean);
  ActionButton(body,"Сохранить автоочистку",()=>{if(!int.TryParse(minutes.Text,out int interval)||!int.TryParse(warning.Text,out int warn)||!int.TryParse(critical.Text,out int crit))throw new ArgumentException("Введите целые числа");prefs.SetMemoryLimits(warn,crit,interval);prefs.AutoClean=autoClean.IsChecked==true;Save();Refresh();});
  Paragraph(body,"Опасный - предупреждение. Критический - автоочистка после минуты выше порога. Интервал: 5-180 мин; опасный: от 50% и ниже критического; критический: 70-95%. Способ срабатывания и пауза в играх - в Настройках.");
  var refreshTimer=new System.Windows.Threading.DispatcherTimer{Interval=TimeSpan.FromSeconds(2)};refreshTimer.Tick+=(_,_)=>{Refresh();lastResult.Text=memoryStatus;};window.Closed+=(_,_)=>refreshTimer.Stop();refreshTimer.Start();
  ActionButton(body,"Проверить ещё раз",Refresh);
  Paragraph(body,"Очистка файлового кэша может временно увеличить обращения к диску. Windows обычно освобождает кэш сама. Во время игры очистка заблокирована.");
  ActionButton(body,"Очистить файловый кэш Windows",async()=>{if(prefs.PauseCleanInGames&&await Task.Run(()=>GameDetector.Detect(prefs.Games,prefs.DiscoverSteamGames?steamGames:[]).Length>0)){lastResult.Text="Идёт игра. Очистка доступна после выхода.";return;}try{var reply=await Client.Send(new("memorytrim"));Refresh();memoryStatus=reply.Detail;lastResult.Text=memoryStatus;}catch(Exception e){memoryStatus=e.Message;lastResult.Text=memoryStatus;}});
  ActionButton(body,"Освободить память самой VSP",()=>{GC.Collect();GC.WaitForPendingFinalizers();if(!EmptyWorkingSet(Process.GetCurrentProcess().Handle))throw new IOException("Windows не освободила рабочий набор VSP");Refresh();});
  var cache=new TextBlock{TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,20,0,8)};body.Children.Add(cache);long total=CacheCandidates().Sum(f=>f.Length);cache.Text=$"Старые временные файлы VSP: {total/1048576.0:F1} МБ.\nДругие программы и личные файлы не затрагиваются.";
  ActionButton(body,"Удалить старые временные файлы VSP",()=>{int count=0;foreach(var f in CacheCandidates()){try{f.Delete();count++;}catch(IOException){}catch(UnauthorizedAccessException){}}cache.Text=$"Удалено временных файлов VSP: {count}.";});window.ShowDialog();
 }
 static FileInfo[] CacheCandidates(){var root=Path.Combine(DataDirectory,"cache");if(!Directory.Exists(root))return [];if((File.GetAttributes(root)&FileAttributes.ReparsePoint)!=0)throw new IOException("Перенаправленный каталог кэша пропущен");var options=new EnumerationOptions{RecurseSubdirectories=true,AttributesToSkip=FileAttributes.ReparsePoint,IgnoreInaccessible=true};return Directory.EnumerateFiles(root,"*",options).Select(p=>new FileInfo(p)).Where(f=>f.LastWriteTimeUtc<DateTime.UtcNow.AddDays(-7)&&new[]{".tmp",".log",".cache"}.Contains(f.Extension,StringComparer.OrdinalIgnoreCase)).ToArray();}
 [DllImport("psapi.dll",SetLastError=true)]static extern bool EmptyWorkingSet(nint process);
 static bool AutoStart(){using var key=Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");return key?.GetValue("VSP") is string;}
 static void AutoStart(bool on){using var key=Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");if(on)key.SetValue("VSP","\""+Environment.ProcessPath+"\"");else key.DeleteValue("VSP",false);}
 void Settings(){
  var window=Panel("VSP · Настройки",470,720,out var body);
  CheckBox Option(string text,bool value){var b=new CheckBox{Content=text,IsChecked=value,Foreground=Theme.Brush("TextPrimary"),Margin=new Thickness(0,0,0,14)};body.Children.Add(b);return b;}
  var automatic=Option("Автоматически включать Работу вне игр",prefs.Automatic);
  var global=Option("Общий FPS-лимит NVIDIA для 3D-приложений",prefs.GlobalFps);
  Paragraph(body,"Лимит игры или её собственный профиль драйвера может иметь приоритет. Изменение FPS иногда требует перезапуска игры.");
  var startup=Option(PortableMode.Enabled?"Автозапуск недоступен для portable":"Запускать VSP при входе в Windows",!PortableMode.Enabled&&AutoStart());startup.IsEnabled=!PortableMode.Enabled;
  var resume=Option("Восстанавливать активный режим при запуске",prefs.ResumeOnLaunch);
  var clean=Option("Автоматическая очистка памяти",prefs.AutoClean);
  Paragraph(body,"Когда очищать память");var trigger=new ComboBox{ItemsSource=new[]{"По заполнению RAM","По таймеру","По заполнению или таймеру"},SelectedIndex=Array.IndexOf(new[]{"Threshold","Timer","Either"},prefs.MemoryTrigger)};body.Children.Add(trigger);
  var pauseClean=Option("Откладывать очистку во время игр",prefs.PauseCleanInGames);
  Paragraph(body,"Опасный уровень RAM, % (от 50 и ниже критического)");var warningThreshold=new TextBox{Text=prefs.WarningThreshold().ToString()};body.Children.Add(warningThreshold);
  Paragraph(body,"Критический уровень RAM, % (70-95)");var threshold=new TextBox{Text=prefs.MemoryThreshold.ToString()};body.Children.Add(threshold);
  Paragraph(body,"Интервал очистки, минут (5-180)");var cleanInterval=new TextBox{Text=prefs.CleanIntervalMinutes.ToString()};body.Children.Add(cleanInterval);
  Paragraph(body,"Таймер работает, пока VSP включена. По заполнению: после минуты выше порога. Повторная очистка - не чаще выбранного интервала.");
  Paragraph(body,"Задержка перехода в Работу, секунд");var delay=new TextBox{Text=prefs.DelaySeconds.ToString()};body.Children.Add(delay);
  Paragraph(body,"\nСписок Steam обновляется автоматически в меню Игры Steam. Для игр из других магазинов можно добавить исполняемый файл.");var localGames=prefs.Games.ToList();var games=new ListBox{ItemsSource=localGames.ToArray(),Height=90};body.Children.Add(games);
  ActionButton(body,"Добавить игру",()=>{var pick=new Microsoft.Win32.OpenFileDialog{Filter="Приложение|*.exe"};if(pick.ShowDialog()==true&&!localGames.Contains(pick.FileName,StringComparer.OrdinalIgnoreCase)){localGames.Add(pick.FileName);games.ItemsSource=localGames.ToArray();}});
  ActionButton(body,"Убрать выбранную игру",()=>{if(games.SelectedItem is string item){localGames.Remove(item);games.ItemsSource=localGames.ToArray();}});
  ActionButton(body,"Экспорт профилей",()=>{var pick=new Microsoft.Win32.SaveFileDialog{Filter="Профили VSP|*.json",FileName="VSP-profiles.json"};if(pick.ShowDialog()==true){prefs.Validate();AtomicJson.Write(pick.FileName,prefs);}});
  ActionButton(body,"Импорт профилей",()=>{if(enabled)throw new InvalidOperationException("Сначала выключите управление");var pick=new Microsoft.Win32.OpenFileDialog{Filter="Профили VSP|*.json"};if(pick.ShowDialog()!=true)return;if(new FileInfo(pick.FileName).Length>1_000_000)throw new IOException("Файл профилей слишком большой");var imported=AtomicJson.Read<Preferences>(pick.FileName)??throw new IOException("Файл пуст");imported.Validate();imported.EnsureBuiltIns();if(!imported.Profiles.Any(p=>p.Id=="work"&&p.Fps==null))throw new IOException("В файле отсутствует режим Работа");prefs=imported;ready=false;Profiles.ItemsSource=DisplayProfiles();Profiles.SelectedValue=prefs.Selected;ready=true;Theme.Apply(prefs.LightTheme);Save();window.Close();});
  ActionButton(body,"Сохранить",()=>{if(!int.TryParse(delay.Text,out int n)||n is <5 or >600)throw new ArgumentException("Задержка: 5-600 секунд");prefs.Games=localGames;prefs.Automatic=automatic.IsChecked==true;prefs.GlobalFps=global.IsChecked==true;prefs.ResumeOnLaunch=resume.IsChecked==true;prefs.AutoClean=clean.IsChecked==true;prefs.MemoryTrigger=new[]{"Threshold","Timer","Either"}[trigger.SelectedIndex];prefs.PauseCleanInGames=pauseClean.IsChecked==true;if(!int.TryParse(threshold.Text,out var limit)||limit is <70 or >95||!int.TryParse(cleanInterval.Text,out var interval)||interval is <5 or >180)throw new ArgumentException("Проверьте порог RAM и интервал очистки");if(!int.TryParse(warningThreshold.Text,out int warning))throw new ArgumentException("Введите опасный порог RAM");prefs.SetMemoryLimits(warning,limit,interval);prefs.DelaySeconds=n;if(!PortableMode.Enabled)AutoStart(startup.IsChecked==true);Save();window.Close();if(enabled)_=Apply();});window.ShowDialog();
 }
 object DiagnosticData(){
  object gpu;try{using var a=new NvidiaPower();gpu=a.Devices();}catch(Exception e){gpu=e.Message;}
  var memory=Sensors.Ram();var processes=Process.GetProcesses().Select(p=>{using(p){try{return new{Name=p.ProcessName,RamMiB=Math.Round(p.WorkingSet64/1048576.0,1)};}catch{return null;}}}).Where(p=>p!=null).OrderByDescending(p=>p!.RamMiB).Take(10).ToArray();
  return new{Product="VSP",Version=typeof(MainWindow).Assembly.GetName().Version?.ToString(),OS=Environment.OSVersion.ToString(),Cpu=Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER"),GPU=gpu,GpuWatts=sensors.GpuWatts(),AvailableRamGiB=memory.Available/1073741824.0,GlobalFps=prefs.GlobalFps,Applied=applied,Enabled=enabled,TopMemoryProcesses=processes,Measurement="GPU only; whole-PC energy unmeasured",GameBoost=prefs.Profiles.FirstOrDefault(p=>p.Id==applied)?.Boost==true,GameProcesses=gamePids.Length,AdaptiveGpuTuning="Not enabled without reliable FPS telemetry"};
 }
 void Diagnostics(){var window=Panel("VSP · Диагностика",560,650,out var body);var text=JsonSerializer.Serialize(new{State=DiagnosticData(),GpuHistory=history.Recent()},new JsonSerializerOptions{WriteIndented=true});var box=new TextBox{Text=text,IsReadOnly=true,TextWrapping=TextWrapping.Wrap,Height=420,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};body.Children.Add(box);ActionButton(body,"Сохранить отчёт",()=>{var pick=new Microsoft.Win32.SaveFileDialog{Filter="Диагностика JSON|*.json",FileName="VSP-diagnostics.json"};if(pick.ShowDialog()==true)File.WriteAllText(pick.FileName,text);});ActionButton(body,"Экспорт истории мощности GPU",()=>{var pick=new Microsoft.Win32.SaveFileDialog{Filter="История CSV|*.csv",FileName="VSP-GPU-power.csv"};if(pick.ShowDialog()==true)history.Export(pick.FileName);});window.ShowDialog();}
 [StructLayout(LayoutKind.Sequential)]struct LastInput {public uint Size,Tick;}
 [DllImport("user32.dll")]static extern bool GetLastInputInfo(ref LastInput info);
 readonly MemorySchedule memorySchedule=new();
 string memoryStatus="Очистка ещё не запускалась";
 async Task CheckMemoryPressure(){
  var memory=Sensors.Ram();var now=DateTimeOffset.UtcNow;
  if(!memorySchedule.Due(enabled,prefs.AutoClean,prefs.MemoryTrigger,prefs.MemoryThreshold,prefs.CleanIntervalMinutes,prefs.PauseCleanInGames,knownGame,memory.Load,now))return;
  if(prefs.PauseCleanInGames&&await Task.Run(()=>GameDetector.Detect(prefs.Games,prefs.DiscoverSteamGames?steamGames:[]).Length>0))return;
  memorySchedule.Attempted(now);
  try{GC.Collect();EmptyWorkingSet(Process.GetCurrentProcess().Handle);var result=await Client.Send(new("memorytrim"));memoryStatus=$"{DateTime.Now:HH:mm} · {result.Detail}";}
  catch(Exception e){memoryStatus=e.Message;}
 }
}
