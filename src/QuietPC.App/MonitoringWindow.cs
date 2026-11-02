using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using QuietPC.Core;
using QuietPC.Windows;
using Button=System.Windows.Controls.Button;
namespace QuietPC.App;
sealed partial class MonitoringWindow:Window {
 readonly MonitorPreferences preferences;readonly Sensors sensors;readonly Action save;readonly Func<string?> games;
 readonly SystemMonitor monitor=new();SensorProcess helper;readonly DispatcherTimer timer=new();
 readonly TextBlock status=new(){FontSize=11,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,0)};
 readonly Grid layout=new();readonly StackPanel left=new(),right=new();
 readonly Dictionary<string,(MonitorMetric Definition,Grid Row,TextBlock Value)> rows=[];
 readonly Dictionary<string,(Border Frame,StackPanel Body)> groups=[];
 readonly List<string> order=[];bool paused;double lastWidth;string layoutSignature="";
 public MonitoringWindow(Window owner,MonitorPreferences preferences,Sensors sensors,Action save,Func<string?> games){
  helper=new SensorProcess(preferences.AdvancedSensors,preferences.MonitorIntervalSeconds);this.preferences=preferences;this.sensors=sensors;this.save=save;this.games=games;
  Owner=owner;Title="VSP · Мониторинг";Width=Math.Min(940,SystemParameters.WorkArea.Width-48);Height=Math.Min(760,SystemParameters.WorkArea.Height-64);MinWidth=340;MinHeight=380;
  WindowStartupLocation=WindowStartupLocation.CenterOwner;SetResourceReference(BackgroundProperty,"AppBg");Theme.ConfigureWindow(this);
  var root=new DockPanel{Margin=new Thickness(18)};Content=root;
  var header=new WrapPanel{Margin=new Thickness(0,0,0,12)};DockPanel.SetDock(header,Dock.Top);root.Children.Add(header);
  var settings=new Button{Content="Настройки мониторинга",Padding=new Thickness(12,8,12,8)};DockPanel.SetDock(settings,Dock.Right);header.Children.Add(settings);settings.Click+=(_,_)=>Settings();
  var pause=new Button{Content="Пауза",Padding=new Thickness(12,8,12,8),Margin=new Thickness(0,0,8,0)};DockPanel.SetDock(pause,Dock.Right);header.Children.Add(pause);pause.Click+=(_,_)=>{paused=!paused;pause.Content=paused?"Продолжить":"Пауза";if(paused){timer.Stop();helper.Dispose();StopLive();status.Text="Мониторинг на паузе";}else{helper=new SensorProcess(preferences.AdvancedSensors,preferences.MonitorIntervalSeconds);Refresh();timer.Start();}};
  header.Children.Insert(0,new TextBlock{Text="Мониторинг",FontSize=22,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,0,20,8)});
  DockPanel.SetDock(status,Dock.Bottom);root.Children.Add(status);
  layout.ColumnDefinitions.Add(new());layout.ColumnDefinitions.Add(new());layout.Children.Add(left);layout.Children.Add(right);Grid.SetColumn(right,1);
  var content=new StackPanel();BuildLive(content);content.Children.Add(layout);var scroll=new ScrollViewer{Content=content,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};root.Children.Add(scroll);
  scroll.SizeChanged+=(_,e)=>{lastWidth=Math.Max(0,e.NewSize.Width-20);Reflow();ReflowCharts();};
  timer.Interval=TimeSpan.FromSeconds(1);timer.Tick+=(_,_)=>Refresh();
  Closed+=(_,_)=>{timer.Stop();helper.Dispose();StopLive();};Loaded+=(_,_)=>{ClampToScreen();Refresh();timer.Start();};DpiChanged+=(_,_)=>Dispatcher.BeginInvoke(ClampToScreen);
 }
 public double? CpuPackageWatts {
  get {
   if(paused||DateTimeOffset.UtcNow-helper.ReceivedAt>TimeSpan.FromSeconds(preferences.MonitorIntervalSeconds*3+2))return null;
   var candidates=helper.Metrics.Where(m=>m.Group=="Процессор"&&m.Unit=="Вт"&&m.Number is double n&&double.IsFinite(n)&&n>=0&&(m.Id.EndsWith("/CPU Package",StringComparison.OrdinalIgnoreCase)||m.Id.EndsWith("/Package",StringComparison.OrdinalIgnoreCase)||m.Id.EndsWith("/CPU Package Power",StringComparison.OrdinalIgnoreCase))).ToArray();
   return candidates.Length==1?candidates[0].Number:null;
  }
 }
 void ClampToScreen(){
  var screen=System.Windows.Forms.Screen.FromHandle(new System.Windows.Interop.WindowInteropHelper(this).Handle);
  var dpi=System.Windows.Media.VisualTreeHelper.GetDpi(this);Width=Math.Min(Width,(screen.WorkingArea.Width-32)/dpi.DpiScaleX);Height=Math.Min(Height,(screen.WorkingArea.Height-32)/dpi.DpiScaleY);
 }
 void Refresh(){
  try{
   if(System.Diagnostics.Stopwatch.GetElapsedTime(lastHardware).TotalSeconds>=preferences.MonitorIntervalSeconds){hardware=monitor.Read(sensors,!preferences.MonitoringHidden.Contains("process.memory"));hardware.AddRange(helper.Metrics);lastHardware=System.Diagnostics.Stopwatch.GetTimestamp();}var values=new List<MonitorMetric>(hardware);values.AddRange(ReadLive());int display=0;
   foreach(var screen in System.Windows.Forms.Screen.AllScreens)values.Add(new("display."+screen.DeviceName,"Мониторы","Монитор "+(++display)+(screen.Primary?" · основной":""),SystemMonitor.Display(screen.DeviceName)));
   values.Add(new("game.name","Игра","Запущено",games()));
   var live=values.Where(v=>v.Value is not null&&!preferences.MonitoringHidden.Contains(v.Id)).Select(v=>v.Id).ToHashSet();
   foreach(var value in values){
    if(!rows.TryGetValue(value.Id,out var row)){
     if(!groups.TryGetValue(value.Group,out var group)){
      var body=new StackPanel();body.Children.Add(new TextBlock{Text=value.Group,FontSize=15,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,8)});
      var frame=new Border{Child=body,CornerRadius=new CornerRadius(10),Padding=new Thickness(14),Margin=new Thickness(4),Background=Theme.Brush("PanelBg")};
      frame.SetResourceReference(Border.BackgroundProperty,"PanelBg");groups[value.Group]=(frame,body);order.Add(value.Group);group=(frame,body);
     }
     var grid=new Grid{Margin=new Thickness(0,3,0,3)};grid.ColumnDefinitions.Add(new(){Width=new GridLength(138)});grid.ColumnDefinitions.Add(new());
     grid.Children.Add(new TextBlock{Text=value.Label,TextWrapping=TextWrapping.Wrap,FontSize=12,Foreground=Theme.Brush("TextSecondary"),Margin=new Thickness(0,0,12,0)});
     var text=new TextBlock{TextWrapping=TextWrapping.Wrap,FontSize=12};Grid.SetColumn(text,1);grid.Children.Add(text);group.Body.Children.Add(grid);row=(value,grid,text);rows[value.Id]=row;
    }
    rows[value.Id]=(value,row.Row,row.Value);
    row.Row.Visibility=live.Contains(value.Id)?Visibility.Visible:Visibility.Collapsed;
    if(row.Value.Text!=value.Value)row.Value.Text=value.Value??"";
   }
   foreach(var item in rows)if(!live.Contains(item.Key))item.Value.Row.Visibility=Visibility.Collapsed;
   foreach(var group in groups)group.Value.Frame.Visibility=rows.Values.Any(r=>r.Definition.Group==group.Key&&r.Row.Visibility==Visibility.Visible)?Visibility.Visible:Visibility.Collapsed;
   RecordCharts(values);Reflow();status.Text=$"Обновлено {DateTime.Now:HH:mm:ss} · {live.Count} показателей · интервал {preferences.MonitorIntervalSeconds} с · "+helper.Status;
  }catch(Exception e){status.Text="Не удалось обновить: "+e.Message;}
 }
 void Reflow(){
  bool two=lastWidth>=780;string signature=two+":"+string.Join(";",rows.Where(r=>r.Value.Row.Visibility==Visibility.Visible).Select(r=>r.Key));if(signature==layoutSignature)return;layoutSignature=signature;left.Children.Clear();right.Children.Clear();
  layout.ColumnDefinitions[1].Width=two?new GridLength(1,GridUnitType.Star):new GridLength(0);right.Visibility=two?Visibility.Visible:Visibility.Collapsed;
  int Priority(string name)=>name switch{"Процессор"=>0,"Память"=>1,"Видеокарта"=>2,_ when name.Contains("Температуры")=>3,_ when name.Contains("Частоты")=>4,_ when name.Contains("Загрузка")=>5,_ when name.Contains("Видеопамять")=>6,"Диски"=>7,"Сеть"=>8,"Мониторы"=>9,"Процессы"=>10,_=>11};
  double a=0,b=0;foreach(string name in order.OrderBy(Priority)){var group=groups[name];if(group.Frame.Visibility!=Visibility.Visible)continue;double height=44+rows.Values.Count(r=>r.Definition.Group==name&&r.Row.Visibility==Visibility.Visible)*31;var target=two&&b<a?right:left;target.Children.Add(group.Frame);if(target==left)a+=height;else b+=height;}
 }
 void Settings(){
  var window=new Window{Owner=this,Title="VSP · Настройки мониторинга",Width=480,Height=Math.Min(700,SystemParameters.WorkArea.Height-64),Background=Theme.Brush("AppBg"),WindowStartupLocation=WindowStartupLocation.CenterOwner};Theme.ConfigureWindow(window);
  var body=new StackPanel{Margin=new Thickness(22)};window.Content=new ScrollViewer{Content=body,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
  body.Children.Add(new TextBlock{Text="Настройки мониторинга",FontSize=22,Margin=new Thickness(0,0,0,12)});
  body.Children.Add(new TextBlock{Text="По умолчанию включено всё. В окне показываются только полученные данные. Неподтверждённые функции скрыты.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,14)});
  body.Children.Add(new TextBlock{Text="Интервал обновления",Margin=new Thickness(0,0,0,6)});int[] seconds=[1,2,5,10];var interval=new ComboBox{ItemsSource=seconds.Select(n=>$"{n} с").ToArray(),SelectedIndex=Array.IndexOf(seconds,preferences.MonitorIntervalSeconds),Margin=new Thickness(0,0,0,12)};body.Children.Add(interval);
  var advanced=new CheckBox{Content="Дополнительные датчики CPU (требуют установленного PawnIO)",IsChecked=preferences.AdvancedSensors,Foreground=Theme.Brush("TextPrimary"),Margin=new Thickness(0,8,0,12)};body.Children.Add(advanced);
  AddLiveSettings(body);
  var checks=new Dictionary<string,CheckBox>();string? heading=null;
  foreach(var row in rows.Values.OrderBy(r=>r.Definition.Group).ThenBy(r=>r.Definition.Label)){
   if(heading!=row.Definition.Group){heading=row.Definition.Group;body.Children.Add(new TextBlock{Text=heading,FontSize=15,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,12,0,8)});}
   var check=new CheckBox{Content=row.Definition.Label,IsChecked=!preferences.MonitoringHidden.Contains(row.Definition.Id),Foreground=Theme.Brush("TextPrimary"),Margin=new Thickness(0,0,0,8),ToolTip=row.Definition.Value is null?"Сейчас источник недоступен. Данные появятся только при подтверждённом состоянии.":null};checks[row.Definition.Id]=check;body.Children.Add(check);
  }
  body.Children.Add(new TextBlock{Text="FPS собирается PresentMon через события Windows, без внедрения в игру. DLSS/FSR и полная задержка ввода не определяются. Температура CPU зависит от доступного драйвера и прав. VSP не устанавливает драйверы мониторинга. После изменения дополнительных датчиков откройте мониторинг заново.",TextWrapping=TextWrapping.Wrap,Foreground=Theme.Brush("TextSecondary"),Margin=new Thickness(0,16,0,12)});
  var apply=new Button{Content="Сохранить",Margin=new Thickness(0,8,0,0)};body.Children.Add(apply);apply.Click+=(_,_)=>{try{foreach(var (id,check) in checks){preferences.MonitoringHidden.Remove(id);if(check.IsChecked!=true)preferences.MonitoringHidden.Add(id);}preferences.MonitorIntervalSeconds=seconds[Math.Max(0,interval.SelectedIndex)];preferences.AdvancedSensors=advanced.IsChecked==true;save();if(!paused){helper.Dispose();helper=new SensorProcess(preferences.AdvancedSensors,preferences.MonitorIntervalSeconds);}timer.Interval=TimeSpan.FromSeconds(1);lastHardware=0;RestartNetwork();Refresh();window.Close();}catch(Exception ex){System.Windows.MessageBox.Show("Не удалось сохранить настройки: "+ex.Message,"VSP");}};
  window.ShowDialog();
 }
}
