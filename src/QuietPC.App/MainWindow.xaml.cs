using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Diagnostics;
using QuietPC.Core;
using QuietPC.Windows;
using MessageBox=System.Windows.MessageBox;
using ContextMenu=System.Windows.Controls.ContextMenu;
using MenuItem=System.Windows.Controls.MenuItem;
using Forms=System.Windows.Forms;
namespace QuietPC.App;
public partial class MainWindow:Window {
 [System.Runtime.InteropServices.DllImport("dwmapi.dll")]static extern int DwmSetWindowAttribute(nint window,int attribute,ref int value,int size);
 readonly string prefsPath=Path.Combine(PortableMode.DataDirectory,"profiles.json");
 Preferences prefs;readonly Sensors sensors=new();readonly AutoMode auto=new();
 readonly PowerHistory history=new();readonly CpuClockSampler cpuClock=new();
 readonly Forms.NotifyIcon tray;readonly DispatcherTimer timer=new();
 DateTimeOffset scanned=DateTimeOffset.MinValue;bool knownGame;
 int[] gamePids=[],backgroundPids=[];
 string[] gamePaths=[];string activeGameKey="";
 int generation;bool polling;
 bool calibrating;CancellationTokenSource? calibrationStop;bool ready,busy,enabled,exiting;string? applied;string profileReason="Выбран вручную";
 public MainWindow(){
  InitializeComponent();
  Theme.ConfigureWindow(this);
  VersionLabel.Text="Vilod Switch Power · "+typeof(MainWindow).Assembly.GetName().Version?.ToString(3);
  try{Icon=System.Windows.Media.Imaging.BitmapFrame.Create(new Uri(Path.Combine(AppContext.BaseDirectory,"Assets","vsp.png")));}catch{}
  SourceInitialized+=(_,_)=>{try{int round=2;DwmSetWindowAttribute(new System.Windows.Interop.WindowInteropHelper(this).Handle,33,ref round,4);}catch{}};
  try{prefs=AtomicJson.Read<Preferences>(prefsPath)??new();prefs.Validate();}catch(Exception e){prefs=new();MessageBox.Show("Профили не загружены: "+e.Message+". Исходный файл сохранён.");}
  prefs.EnsureBuiltIns();
  Theme.Apply(prefs.LightTheme);Profiles.ItemsSource=DisplayProfiles();Profiles.SelectedValue=prefs.Selected;ready=true;
  var menu=new Forms.ContextMenuStrip();menu.Items.Add("Открыть",null,(_,_)=>{Show();Activate();});
  foreach(var profile in DisplayProfiles()){var p=profile;menu.Items.Add(p.Name,null,async(_,_)=>{Profiles.SelectedValue=p.Id;await Task.CompletedTask;});}
  menu.Items.Add("Выключить управление",null,async(_,_)=>await TurnOff());menu.Items.Add("Выйти",null,async(_,_)=>await Exit());
  System.Drawing.Icon appIcon;try{appIcon=new System.Drawing.Icon(Path.Combine(AppContext.BaseDirectory,"Assets","vsp.ico"));}catch{appIcon=(System.Drawing.Icon)System.Drawing.SystemIcons.Application.Clone();}
  tray=new(){Icon=appIcon,Text="VSP - выключено",Visible=true,ContextMenuStrip=menu};tray.DoubleClick+=(_,_)=>{Show();Activate();};
  timer.Interval=TimeSpan.FromSeconds(5);timer.Tick+=async(_,_)=>await Tick();timer.Start();
  Loaded+=async(_,_)=>{await RefreshSteam();if(prefs.ResumeOnLaunch&&prefs.WasEnabled)await Apply();};
  Closing+=async(_,e)=>{if(exiting)return;e.Cancel=true;await Exit();};
 }
 IEnumerable<Profile> DisplayProfiles()=>prefs.Profiles.OrderBy(p=>p.Id switch{"work"=>0,"competitive"=>1,"120"=>2,"60"=>3,"200"=>4,"400"=>5,"custom"=>6,"boost"=>7,_=>7});
 Profile? DefaultGaming=>prefs.Profiles.FirstOrDefault(p=>p.Id==prefs.GamingDefault());
 Profile Selected=>(Profile)Profiles.SelectedItem;
 void DragWindow(object sender,System.Windows.Input.MouseButtonEventArgs e){if(e.ChangedButton==System.Windows.Input.MouseButton.Left)DragMove();}
 void MinimizeClick(object sender,RoutedEventArgs e)=>Hide();
 void CloseClick(object sender,RoutedEventArgs e)=>Close();
 void Save(){prefs.Selected=Selected.Id;prefs.Validate();AtomicJson.Write(prefsPath,prefs);}
 string AppliedLabel(){var p=prefs.Profiles.FirstOrDefault(p=>p.Id==applied);return p?.Id=="competitive"&&p.Fps is int cap?$"{cap} FPS":p?.Name??"Активно";}
 void Render(Reply r){
  enabled=r.Enabled;applied=r.AppliedProfile;var profile=prefs.Profiles.FirstOrDefault(p=>p.Id==applied);
  Toggle.Tag=enabled?"on":"off";Toggle.Content=enabled?AppliedLabel():"Выключено";
  State.Text=!r.Ok?r.State:enabled?(applied=="work"?"Экономный режим":profile?.Boost==true?"Game Boost включён":"Режим включён"):"Выключено";
  string description=!r.Ok?r.Detail:!enabled?"Исходные настройки восстановлены":applied=="work"?"Для браузера, документов и повседневной работы.":r.State=="Частично применено"?r.Detail:"Профиль питания включён. Для FPS-лимита может потребоваться перезапуск игры.";
  if(enabled&&prefs.AutoClean)description+="\nПамять: "+(prefs.MemoryTrigger=="Threshold"?$"при {prefs.MemoryThreshold}%":prefs.MemoryTrigger=="Timer"?$"каждые {prefs.CleanIntervalMinutes} мин":$"{prefs.CleanIntervalMinutes} мин или {prefs.MemoryThreshold}%");
  if(enabled&&applied=="competitive"&&r.Ok&&r.State!="Частично применено"){State.Text="Соревновательный режим";description=$"Потолок {profile?.Fps} FPS · приоритет игровой производительности. Штатный Turbo и полный бюджет GPU для стандартного профиля.";if(prefs.AutoClean)description+="\nПамять: по вашему расписанию";}if(enabled)description+="\n"+profileReason;if(PortableMode.Enabled&&PortableMode.UsingInstalledService)description+="\n"+PortableMode.BackendLabel;Detail.Text=description;Detail.ToolTip=r.Detail;
  tray.Text=("VSP - "+State.Text)[..Math.Min(63,("VSP - "+State.Text).Length)];
 }

 async Task<bool> Call(Request request){if(busy)return false;generation++;busy=true;Toggle.IsEnabled=false;Profiles.IsEnabled=false;State.Text="Применяю настройки";Toggle.Tag="pending";Toggle.Content="Применяю…";try{if(PortableMode.Enabled&&(request.Command=="apply"||request.Command=="off"&&PortableMode.HostWasStarted))await PortableMode.EnsureHost();var reply=await Client.Send(request);Render(reply);prefs.WasEnabled=reply.Enabled;try{Save();}catch(Exception e){Detail.ToolTip="Настройки не сохранены: "+e.Message;}return reply.Ok;}catch(Exception e){State.Text="Служба недоступна";Detail.Text=e.Message+". Состояние системы не подтверждено.";return false;}finally{Toggle.Tag=enabled?"on":"off";Toggle.Content=enabled?AppliedLabel():"Выключено";busy=false;Toggle.IsEnabled=!calibrating;Profiles.IsEnabled=!calibrating;}}
 async Task Apply(bool automatic=false){if(!automatic)profileReason="Выбран вручную";int? standby=Selected.Fps is null&&prefs.Automatic?DefaultGaming?.Fps:null;if(await Call(new("apply",Selected,[..prefs.Games],(prefs.GlobalFps||Selected.Id=="competitive"),standby,GamePids:gamePids,BackgroundPids:backgroundPids))&&!automatic){auto.Select(Selected,prefs.Automatic,DefaultGaming);activeGameKey=string.Join("|",gamePaths.OrderBy(p=>p,StringComparer.OrdinalIgnoreCase));}}
 async Task TurnOff(){await Call(new("off"));if(!enabled)applied=null;}
 async void ToggleClick(object sender,RoutedEventArgs e){if(enabled)await TurnOff();else await Apply();}
 async void ProfileChanged(object sender,SelectionChangedEventArgs e){if(!ready||busy)return;Save();if(enabled)await Apply();}
 async Task Exit(){if(calibrating){calibrationStop?.Cancel();MessageBox.Show("Останавливаю замер и восстанавливаю профиль. После завершения нажмите выход ещё раз.");return;}if(busy)return;await TurnOff();if(enabled||State.Text is "Служба недоступна" or "Конфликт" or "Ошибка"){MessageBox.Show("Выход остановлен: восстановление не подтверждено. "+Detail.Text);return;}exiting=true;timer.Stop();tray.Dispose();sensors.Dispose();cpuClock.Dispose();System.Windows.Application.Current.Shutdown();}
 async Task Tick(){
  timer.Interval=TimeSpan.FromSeconds(5);if(busy||polling)return;polling=true;try{
  _ = RefreshSteam();
  if(DateTimeOffset.UtcNow-scanned>=TimeSpan.FromSeconds(5)){var configured=prefs.Games.ToArray();var background=prefs.BackgroundApps.ToArray();var steam=prefs.DiscoverSteamGames?steamGames:[];var scan=await Task.Run(()=>(GameDetector.Detect(configured,steam),GameDetector.BackgroundIds(background)));gamePids=scan.Item1.Select(p=>p.Id).ToArray();gamePaths=scan.Item1.Select(p=>p.Path).ToArray();backgroundPids=scan.Item2;knownGame=gamePids.Length>0;scanned=DateTimeOffset.UtcNow;}
  int ticket=generation;
  try{var r=await Client.Send(new("heartbeat",GamePids:gamePids,BackgroundPids:backgroundPids));if(ticket!=generation||busy)return;Render(r);}catch{State.Text="Служба недоступна";Detail.Text="Управление не подтверждено. Проверьте установку службы.";return;}
  var watts=sensors.GpuWatts();var memory=Sensors.Ram();double? cpuWatts=monitoring?.CpuPackageWatts;double? cpuGhz=cpuClock.Read(SystemMonitor.NominalCpuGhz());Metrics.Text=(cpuGhz is double ghz?$"CPU ≈{ghz:F2} ГГц · ":"")+$"GPU {(watts is double w?$"{w:F0} Вт":"-")} · RAM {memory.Load}%";
  Metrics.ToolTip=(cpuGhz.HasValue?"CPU: оценка частоты по счётчику производительности Windows с учётом Turbo; не эффективная частота отдельного ядра и не мощность CPU.\n":"")+(cpuWatts.HasValue?"Мощность CPU: свежий замер пакета из мониторинга.\n":"")+"GPU: расход видеокарты, RAM: занятая память. Это не мощность всего ПК.";
  ComponentsPower.Visibility=cpuWatts.HasValue&&watts.HasValue?Visibility.Visible:Visibility.Collapsed;if(cpuWatts.HasValue&&watts.HasValue)ComponentsPower.Text=$"CPU {cpuWatts.Value:F0} Вт · CPU + GPU ≈ {cpuWatts.Value+watts.Value:F0} Вт (компоненты)";
  var level=MemoryLevels.Classify(memory.Load,prefs.WarningThreshold(),prefs.MemoryThreshold);Metrics.Foreground=Theme.Brush(level==MemoryLevel.Critical?"MemoryCritical":level==MemoryLevel.Warning?"MemoryWarning":"TextSecondary");if(level!=MemoryLevel.Normal)Metrics.Text+=" · "+(level==MemoryLevel.Critical?"Критический уровень":"Опасный уровень");
  history.Add(watts,applied,enabled);
  gpuEnergy.Add(energyClock.Elapsed.TotalSeconds,watts);
  await CheckMemoryPressure();
  if(!enabled||calibrating)return;
  string gameKey=string.Join("|",gamePaths.OrderBy(p=>p,StringComparer.OrdinalIgnoreCase));
  if(prefs.Automatic&&gameKey!=activeGameKey){activeGameKey=gameKey;string? id=prefs.ResolveGamingProfile(gamePaths,prefs.DiscoverSteamGames?steamGames:[]);if(id is not null&&prefs.Profiles.FirstOrDefault(p=>p.Id==id) is {} preset)auto.Manual(preset);}
  var next=auto.Tick(enabled,prefs.Automatic,knownGame,applied,prefs.DelaySeconds,DateTimeOffset.UtcNow);
  if(next is not null){profileReason=next=="work"?"Игра закрыта → Работа после выбранной задержки":"Обнаружена игра → игровой профиль";var p=prefs.Profiles.FirstOrDefault(p=>p.Id==next);if(p!=null){int? standbyCap=p.Fps is null?prefs.Profiles.FirstOrDefault(q=>q.Id==auto.LastGaming)?.Fps:null;await Call(new("apply",p,[..prefs.Games],(prefs.GlobalFps||Selected.Id=="competitive"),standbyCap,gamePids,backgroundPids));}}
  }finally{polling=false;}
 }
 void MenuClick(object sender,RoutedEventArgs e){var m=new ContextMenu{PlacementTarget=(System.Windows.Controls.Button)sender,Placement=System.Windows.Controls.Primitives.PlacementMode.Bottom,HorizontalOffset=-140};Add(prefs.LightTheme?"Тёмная тема":"Светлая тема",()=>{prefs.LightTheme=!prefs.LightTheme;Theme.Apply(prefs.LightTheme);Save();});Add("Игры Steam",SteamPanel);Add("Профили",EditProfiles);Add("Соревновательный режим",CompetitivePanel);Add("Game Boost",GameBoostPanel);Add("Мониторинг",Monitoring);Add("Энергия GPU",EnergyPanel);Add("Замеры и автоподбор",TuningPanel);Add("Возможности компьютера",HardwarePanel);Add("Фоновые приложения",BackgroundOptimizer);Add("Проверка для игр",GamingAudit);Add("Самопроверка CPU",PowerCheck);Add("Память",Memory);Add("Настройки",Settings);Add("Режимы и выход",Help);Add("Диагностика",Diagnostics);Add("Выйти",async()=>await Exit());m.IsOpen=true;void Add(string text,Action action){var item=new MenuItem{Header=text};item.Click+=(_,_)=>action();m.Items.Add(item);}}
 void Help()=>TextDialog("VSP · Режимы и выход", "Соревновательный: 400 FPS, EPP 0, штатный Turbo, GPU 100% исходного + 5 Вт в диапазоне драйвера, приоритет распознанной игры. Пока выбран этот профиль, обычные привязки Steam не заменяют его профилем 120 FPS.\n\n60 FPS: CPU EPP 70, GPU 70% исходного + 5 Вт.\n120 FPS: EPP 50, GPU 85% + 5 Вт.\n200 / 400 FPS: EPP 30, GPU 95% + 5 Вт.\nПользовательский: FPS и стратегия из профиля; GPU по диапазону FPS или заданному проценту + 5 Вт.\nРабота: EPP 80, GPU 60%, без добавки.\nGame Boost: EPP 0, GPU 100% + 5 Вт, приоритет игры.\n\nЛюбой лимит ограничен допустимым диапазоном видеокарты. Значения - потолок мощности, не постоянный расход. Управление GPU и FPS через NVIDIA доступно только на поддерживаемых устройствах; недоступные функции отображаются в статусе.\n\nСвернуть (─): скрыть в трей, управление продолжается.\nКрестик (×), Alt+F4 и Выйти: сначала восстановить исходные настройки, затем завершить VSP. При ошибке восстановления окно остаётся открытым.\nОсновная кнопка: выключить ограничения без выхода.\nАварийное завершение интерфейса: служба восстанавливает настройки после 15 секунд без связи.\nОчистка выключена по умолчанию. После включения: 20 минут или RAM 90%, с паузой в играх.\nДля CS2 профиль выбирайте до запуска игры. Лимит - верхняя граница; 200 FPS не гарантируются.");
 void TextDialog(string title,string text,Action<string>? save=null){
  var win=new Window{Owner=this,Title=title,Width=500,Height=550,WindowStartupLocation=WindowStartupLocation.CenterOwner,Background=Theme.Brush("AppBg")};
  var panel=new DockPanel{Margin=new Thickness(18)};var box=new System.Windows.Controls.TextBox{Text=text,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,IsReadOnly=save is null};
  var button=new System.Windows.Controls.Button{Content=save is null?"Закрыть":"Сохранить и применить",Margin=new Thickness(0,12,0,0)};DockPanel.SetDock(button,Dock.Bottom);panel.Children.Add(button);panel.Children.Add(box);win.Content=panel;
  button.Click+=(_,_)=>{try{save?.Invoke(box.Text);win.Close();}catch(Exception err){MessageBox.Show(err.Message);}};win.ShowDialog();
 }
 async void EditProfiles(){
  var editor=new ProfileEditor(this,prefs);if(editor.ShowDialog()!=true||editor.Result is null)return;
  bool wasEnabled=enabled;bool removedActive=applied!=null&&!editor.Result.Any(p=>p.Id==applied);
  if(removedActive){await TurnOff();if(enabled||State.Text!="Выключено")return;}
  prefs.Profiles=editor.Result;prefs.Selected=editor.SelectedId;
  prefs.SteamPresets.RemoveAll(s=>!prefs.Profiles.Any(p=>p.Id==s.ProfileId&&p.Fps!=null));
  ready=false;Profiles.ItemsSource=DisplayProfiles();Profiles.SelectedValue=prefs.Selected;ready=true;Save();
  if(wasEnabled&&!removedActive)await Apply();
 }
}
