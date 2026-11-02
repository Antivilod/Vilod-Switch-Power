using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using QuietPC.Core;
using QuietPC.Windows;
using Button=System.Windows.Controls.Button;
using CheckBox=System.Windows.Controls.CheckBox;
using TextBox=System.Windows.Controls.TextBox;
namespace QuietPC.App;
sealed record CaptureTarget(int Id,long Started,string Path,string Label){public override string ToString()=>Label;}
sealed record Measurement(DateTimeOffset At,string Machine,string Application,int TargetFps,PowerTrial Trial,string Csv);
public partial class MainWindow {
 Window? tuningWindow;
 void TuningPanel(){
  if(tuningWindow is not null){tuningWindow.Activate();return;}
  var window=Panel("VSP · Замеры и автоподбор",760,820,out var body);tuningWindow=window;window.MaxHeight=SystemParameters.WorkArea.Height-24;window.MaxWidth=SystemParameters.WorkArea.Width-24;
  Paragraph(body,"Замеряет выбранное приложение через PresentMon. Игра не модифицируется. Для сравнения используйте одну сцену, разрешение и настройки. Автоподбор снижает только потолок GPU и затем восстанавливает исходный профиль.");
  var target=new ComboBox{DisplayMemberPath="Label",Margin=new Thickness(0,0,0,8)};body.Children.Add(target);
  void Targets(){var entries=new List<CaptureTarget>();using var self=Process.GetCurrentProcess();foreach(var p in Process.GetProcesses()){using(p){try{if(p.Id==self.Id||p.SessionId!=self.SessionId||p.MainWindowHandle==0)continue;var image=GameDetector.Image(p.Id);if(image is not null)entries.Add(new(p.Id,p.StartTime.ToUniversalTime().Ticks,image,$"{p.ProcessName} · PID {p.Id}"));}catch{}}}target.ItemsSource=entries.OrderBy(p=>p.Label).ToArray();if(target.Items.Count>0)target.SelectedIndex=0;}
  Targets();var refresh=new Button{Content="Обновить запущенные приложения",Margin=new Thickness(0,0,0,8)};refresh.Click+=(_,_)=>Targets();body.Children.Add(refresh);
  using var identity=WindowsIdentity.GetCurrent();bool admin=new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
  var elevation=new CheckBox{Content="Запросить права администратора для сбора FPS",IsChecked=!admin,Foreground=Theme.Brush("TextPrimary"),Margin=new Thickness(0,6,0,12)};body.Children.Add(elevation);
  Paragraph(body,"Автоподбор: сначала включите игровой FPS-профиль VSP, затем запустите игру. Если лимит ещё не действует, перезапустите игру. На время теста сохраняйте одинаковую нагрузку. Профиль Game Boost для подбора экономии не используется.");
  var status=new TextBlock{Text="Готов к измерению",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,6,0,10)};body.Children.Add(status);
  var output=new TextBox{IsReadOnly=true,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Height=210};body.Children.Add(output);
  Measurement? before=null,after=null;var trials=new List<PowerTrial>();TuningDecision? decision=null;Profile? recommendationSource=null;CaptureTarget? recommendationTarget=null;bool closeAfter=false,failedRestore=false;
  var buttons=new List<Button>();
  Button Add(string title,Func<Task> action){var b=new Button{Content=title,Margin=new Thickness(0,7,0,0)};b.Click+=async(_,_)=>{try{await action();}catch(Exception e){status.Text=e.Message;}};body.Children.Add(b);buttons.Add(b);return b;}
  var stopButton=new Button{Content="Остановить и вернуть исходный профиль",IsEnabled=false,Margin=new Thickness(0,8,0,0)};
  stopButton.Click+=(_,_)=>calibrationStop?.Cancel();
  async Task Run(bool tune,bool baseline){
   if(calibrating||busy)throw new InvalidOperationException("Дождитесь текущей операции");
   if(target.SelectedItem is not CaptureTarget selected)throw new InvalidOperationException("Выберите запущенную игру или приложение");
   var profile=Selected;
   if(tune&&(!enabled||applied!=profile.Id||profile.Fps is null||profile.Boost))throw new InvalidOperationException("Включите выбранный обычный FPS-профиль до автоподбора");
   if(tune&&!HardwareCapabilities.Read().NvidiaPower)throw new InvalidOperationException("Для автоподбора нужна одна поддерживаемая NVIDIA GPU с доступным лимитом мощности");
   calibrating=true;calibrationStop=new();var token=calibrationStop.Token;Toggle.IsEnabled=false;Profiles.IsEnabled=false;foreach(var b in buttons)b.IsEnabled=false;refresh.IsEnabled=false;target.IsEnabled=false;elevation.IsEnabled=false;stopButton.IsEnabled=true;failedRestore=false;
   try{
    bool elevate=elevation.IsChecked==true;using var capture=await Task.Run(()=>new FrameCapture(selected.Id,tune?180:35,elevate),token);
    status.Text="Подключаю внешний сбор кадров…";await WaitFrames(capture,selected,token);
    if(!tune){status.Text=baseline?"Измеряю ДО…":"Измеряю ПОСЛЕ…";var result=await Measure(capture,selected,profile.GpuPercent,token);var measurement=new Measurement(DateTimeOffset.Now,HardwareCapabilities.Read().Fingerprint,Path.GetFileName(selected.Path),profile.Fps??0,result,capture.CsvPath);if(baseline)before=measurement;else after=measurement;output.Text=Compare(before,after);SaveMeasurement(measurement);status.Text="Замер завершён. Это данные измеренной сцены.";return;}
    trials.Clear();decision=null;recommendationSource=profile;recommendationTarget=selected;
    uint start=profile.GpuPercent!=0?profile.GpuPercent:profile.Fps<=60?70u:profile.Fps<=120?85u:95u;
    var steps=PowerTuner.Steps(start);double? lastLimit=null;
    foreach(uint percent in steps){
     token.ThrowIfCancellationRequested();CheckTarget(selected);
     status.Text=$"Автоподбор: GPU {percent}% исходного лимита. Сохраняйте ту же сцену…";
     if(!await Call(new("apply",percent==start?profile:profile with{Id="vsp-calibration",Name="Автоподбор",GpuPercent=percent},[..prefs.Games],prefs.GlobalFps,GamePids:gamePids,BackgroundPids:backgroundPids)))throw new IOException("Не удалось применить тестовый предел GPU");
     var trial=await Measure(capture,selected,percent,token);trials.Add(trial);
     output.Text=string.Join("\n",trials.Select(TrialText));
     if(trials.Count==1&&(!PowerTuner.Stable(trial,trial,profile.Fps!.Value)||trial.Frames.AverageFps>profile.Fps.Value*1.1))break;
     if(trials.Count>1&&(!PowerTuner.Stable(trials[0],trial,profile.Fps!.Value)||lastLimit is double previous&&trial.LimitWatts is double current&&Math.Abs(current-previous)<1))break;
     lastLimit=trial.LimitWatts;
    }
    decision=PowerTuner.Choose(trials,profile.Fps!.Value);status.Text=decision.Reason;output.Text+="\n\n"+decision.Reason;
    string reports=Path.Combine(PortableMode.DataDirectory,"Measurements");Directory.CreateDirectory(reports);AtomicJson.Write(Path.Combine(reports,"tuning-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+".json"),new{Hardware=HardwareCapabilities.Read(),Application=Path.GetFileName(selected.Path),Profile=profile,Trials=trials,Decision=decision,Scope="GPU only; fixed scene; CPU package power unavailable"});
   }
   catch(OperationCanceledException){status.Text="Замер остановлен. Возвращаю исходный профиль…";}
   catch(Exception e){status.Text="Замер не завершён: "+e.Message;}
   finally{
    if(tune){try{if(!await Call(new("apply",profile,[..prefs.Games],prefs.GlobalFps,GamePids:gamePids,BackgroundPids:backgroundPids))){failedRestore=true;status.Text="Восстановление не подтверждено. Не закрывайте VSP; используйте выключение управления.";}else{profileReason="Исходный профиль восстановлен после замера";}}catch(Exception ex){failedRestore=true;status.Text="Восстановление не подтверждено: "+ex.Message;}}
    calibrating=false;calibrationStop?.Dispose();calibrationStop=null;Toggle.IsEnabled=true;Profiles.IsEnabled=true;foreach(var b in buttons)b.IsEnabled=true;refresh.IsEnabled=true;target.IsEnabled=true;elevation.IsEnabled=true;stopButton.IsEnabled=false;
    if(closeAfter&&!failedRestore)window.Close();
   }
  }
  Add("1 · Замер ДО (около 10 секунд)",()=>Run(false,true));
  Add("2 · Замер ПОСЛЕ и сравнение",()=>Run(false,false));
  Add("Подобрать экономичный предел GPU (до 2 минут)",()=>Run(true,false));body.Children.Add(stopButton);
  Add("Сохранить проверенную рекомендацию отдельным профилем",()=>{
   if(decision?.RecommendedPercent is not uint percent||recommendationSource is null||recommendationTarget is null)throw new InvalidOperationException("Проверенной рекомендации пока нет");
   if(prefs.Profiles.Count>=50)throw new InvalidOperationException("Достигнут предел числа профилей");
   var saved=recommendationSource with{Id=Guid.NewGuid().ToString("N"),Name=$"{recommendationSource.Fps} FPS · ЭКО · {Path.GetFileNameWithoutExtension(recommendationTarget.Path)}",GpuPercent=percent};if(saved.Name.Length>80)saved=saved with{Name=saved.Name[..80]};
   prefs.Profiles.Add(saved);foreach(var game in steamGames.Where(g=>SteamLibrary.Contains(g,recommendationTarget.Path))){prefs.SteamPresets.RemoveAll(p=>p.AppId==game.AppId);prefs.SteamPresets.Add(new(game.AppId,saved.Id));}
   ready=false;Profiles.ItemsSource=DisplayProfiles();Profiles.SelectedValue=recommendationSource.Id;ready=true;Save();status.Text="Профиль сохранён. Для игры Steam добавлена привязка, если игра найдена. Выберите профиль для применения.";return Task.CompletedTask;
  });
  Add("Сохранить сравнение в JSON",()=>{var pick=new Microsoft.Win32.SaveFileDialog{Filter="Отчёт VSP|*.json",FileName="VSP-before-after.json"};if(pick.ShowDialog()==true)AtomicJson.Write(pick.FileName,new{Before=before,After=after,Comparison=Compare(before,after)});return Task.CompletedTask;});
  window.Closing+=(_,e)=>{if(calibrating){e.Cancel=true;closeAfter=true;calibrationStop?.Cancel();}};window.Closed+=(_,_)=>tuningWindow=null;window.Show();
 }
 static void CheckTarget(CaptureTarget target){try{using var process=Process.GetProcessById(target.Id);if(process.HasExited||process.StartTime.ToUniversalTime().Ticks!=target.Started)throw new IOException("Измеряемое приложение завершилось. Запустите его и обновите список.");}catch(ArgumentException ex){throw new IOException("Измеряемое приложение завершилось. Запустите его и обновите список.",ex);}}
 static async Task WaitFrames(FrameCapture capture,CaptureTarget target,CancellationToken token){for(int i=0;i<40;i++){token.ThrowIfCancellationRequested();CheckTarget(target);if((await Task.Run(()=>capture.Read(target.Id),token)).Length>=30)return;await Task.Delay(250,token);}throw new IOException("Нет достоверного потока кадров. Проверьте права PresentMon и выбранное приложение.");}
 async Task<PowerTrial> Measure(FrameCapture capture,CaptureTarget target,uint percent,CancellationToken token){
  await Task.Delay(2000,token);var initial=await Task.Run(()=>capture.Read(target.Id),token);if(initial.Length==0)throw new IOException("Нет кадров после прогрева");double from=initial[^1].Seconds;
  var watts=new List<double>();var clock=Stopwatch.StartNew();while(clock.Elapsed.TotalSeconds<8){token.ThrowIfCancellationRequested();CheckTarget(target);if(sensors.GpuWatts() is double w)watts.Add(w);await Task.Delay(500,token);}
  var samples=await Task.Run(()=>capture.Read(target.Id),token);var frames=FrameAnalysis.Summarize(samples.Where(s=>s.Seconds>from));double? limit=null;try{using var gpu=new NvidiaPower();var devices=gpu.Devices();if(devices.Length==1)limit=devices[0].Current/1000.0;}catch{}
  return new(percent,limit,frames,watts.Count>=4?watts.Average():null);
 }
 static string TrialText(PowerTrial t)=>$"GPU: {t.LimitWatts?.ToString("F0")??"—"} Вт потолок · {t.AverageGpuWatts?.ToString("F1")??"—"} Вт сейчас (среднее)\nFPS {t.Frames.AverageFps:F1} · 1% low {t.Frames.OnePercentLow:F1} · P99 кадра {t.Frames.P99Milliseconds:F2} мс\n";
 static string Compare(Measurement? before,Measurement? after){
  string text=(before is null?"ДО: ещё нет замера":"ДО · "+before.Application+"\n"+TrialText(before.Trial))+"\n"+(after is null?"ПОСЛЕ: ещё нет замера":"ПОСЛЕ · "+after.Application+"\n"+TrialText(after.Trial));
  if(before is not null&&after is not null){if(before.Machine!=after.Machine||before.Application!=after.Application)return text+"\nЗамеры относятся к разным устройствам или приложениям. Сравнение не рассчитано.";text+=$"\nИзменение FPS: {after.Trial.Frames.AverageFps-before.Trial.Frames.AverageFps:+0.0;-0.0;0.0}";if(before.Trial.AverageGpuWatts is double b&&after.Trial.AverageGpuWatts is double a)text+=$"\nИзменение расхода GPU: {a-b:+0.0;-0.0;0.0} Вт";}
  return text+"\nОбщий расход ПК и мощность CPU в ваттах не измерены. Разные сцены нельзя считать равными условиями.";
 }
 static void SaveMeasurement(Measurement measurement){string dir=Path.Combine(PortableMode.DataDirectory,"Measurements");Directory.CreateDirectory(dir);AtomicJson.Write(Path.Combine(dir,"measurement-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+".json"),measurement);}
}
