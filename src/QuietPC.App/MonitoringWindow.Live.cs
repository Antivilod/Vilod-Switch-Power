using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using QuietPC.Core;
using Button=System.Windows.Controls.Button;
namespace QuietPC.App;
sealed partial class MonitoringWindow {
 long lastHardware;List<MonitorMetric> hardware=[];readonly Stopwatch clock=Stopwatch.StartNew();readonly MetricHistory history=new();
 readonly ComboBox targets=new(){MinWidth=200,MaxWidth=480,Margin=new Thickness(0,4,8,4)};
 readonly TextBlock frameStatus=new(){TextWrapping=TextWrapping.Wrap,FontSize=11,Margin=new Thickness(0,6,0,8)};
 readonly Button captureButton=new(){Content="Включить FPS",Padding=new Thickness(10,6,10,6)};
 readonly UniformGridCompat charts=new();readonly List<(ComboBox Choice,MetricChart Chart)> chartItems=[];
 readonly MetricChart frameChart=new(){Visibility=Visibility.Collapsed};readonly Dictionary<string,MonitorMetric> definitions=[];
 LiveFrameSession? capture;NetworkProbe? probe;bool closed;
 sealed record Choice(string Id,string Label){public override string ToString()=>Label;}
 void BuildLive(StackPanel content){
  var panel=new StackPanel{Margin=new Thickness(4,0,4,12)};content.Children.Add(panel);
  panel.Children.Add(new TextBlock{Text="Кадры и задержки",FontSize=17,Margin=new Thickness(0,4,0,8)});
  var bar=new WrapPanel();panel.Children.Add(bar);bar.Children.Add(targets);
  var refresh=new Button{Content="Обновить список",Padding=new Thickness(10,6,10,6),Margin=new Thickness(0,0,8,0)};bar.Children.Add(refresh);bar.Children.Add(captureButton);refresh.Click+=(_,_)=>RefreshTargets();
  captureButton.Click+=async(_,_)=>{
   if(capture is not null){capture.Dispose();capture=null;captureButton.Content="Включить FPS";return;}
   if(targets.SelectedItem is not CaptureTarget target){frameStatus.Text="Выберите запущенную игру или приложение";return;}
   captureButton.IsEnabled=false;targets.IsEnabled=false;var session=new LiveFrameSession();capture=session;
   try{await session.Start(target);}finally{if(!closed){captureButton.IsEnabled=true;targets.IsEnabled=true;captureButton.Content="Остановить FPS";}}
  };
  panel.Children.Add(frameStatus);panel.Children.Add(new TextBlock{Text="Графики · выберите показатель в каждой карточке",FontSize=14,Margin=new Thickness(0,4,0,8)});
  panel.Children.Add(charts);for(int i=0;i<4;i++){
   int index=i;var stack=new StackPanel();var choice=new ComboBox{Margin=new Thickness(0,0,0,6)};var chart=new MetricChart();stack.Children.Add(choice);stack.Children.Add(chart);
   charts.Children.Add(new Border{Child=stack,Padding=new Thickness(12),Margin=new Thickness(4),CornerRadius=new CornerRadius(10),Background=Theme.Brush("PanelBg")});chartItems.Add((choice,chart));
   choice.SelectionChanged+=(_,_)=>{if(choice.SelectedItem is Choice c){var ids=preferences.Graphs.ToList();while(ids.Count<=index)ids.Add("");ids[index]=c.Id;preferences.Graphs=ids.ToArray();try{save();}catch{}UpdateCharts();}};
  }
  panel.Children.Add(new TextBlock{Text="Последние кадры · интервал Present, мс (не задержка ввода)",FontSize=12,Margin=new Thickness(4,10,0,0)});panel.Children.Add(frameChart);
  RefreshTargets();RestartNetwork();Closed+=(_,_)=>closed=true;
 }
 void RefreshTargets(){
  int? selected=(targets.SelectedItem as CaptureTarget)?.Id;var list=new List<CaptureTarget>();var names=(games()??"").Split(", ");
  foreach(var p in Process.GetProcesses()){using(p){try{if(p.Id==Environment.ProcessId||p.MainWindowHandle==0||p.SessionId!=Process.GetCurrentProcess().SessionId)continue;string? path=GameDetector.Image(p.Id);if(path is null)continue;list.Add(new(p.Id,p.StartTime.ToUniversalTime().Ticks,path,$"{p.ProcessName} · {p.Id}"));}catch{}}}
  targets.ItemsSource=list.OrderByDescending(t=>names.Contains(System.IO.Path.GetFileNameWithoutExtension(t.Path))).ThenBy(t=>t.Label).ToArray();targets.SelectedItem=list.FirstOrDefault(t=>t.Id==selected);if(targets.SelectedIndex<0&&list.Count>0)targets.SelectedIndex=0;
 }
 void RestartNetwork(){probe?.Dispose();probe=!paused&&preferences.NetworkProbeEnabled?new NetworkProbe(preferences.NetworkHost):null;}
 void StopLive(){capture?.Dispose();capture=null;probe?.Dispose();probe=null;captureButton.Content="Включить FPS";frameChart.Points=[];frameChart.InvalidateVisual();}
 List<MonitorMetric> ReadLive(){
  if(!paused&&preferences.NetworkProbeEnabled&&probe is null)RestartNetwork();var result=new List<MonitorMetric>();
  void Add(string id,string group,string label,double? n,string unit){result.Add(new(id,group,label,n is double v?$"{v:F2} {unit}":null,n,unit));}
  frameStatus.Text=capture?.Packet.Status??"Выберите приложение и включите FPS. Windows может запросить разрешение. Сбор без записи кадров на диск.";
  var stats=capture?.Packet.Stats;
  Add("live.fps","Кадры","FPS · последние 2 с",stats?.Fps,"FPS");Add("live.average","Кадры","Средний FPS · до 30 с",stats?.AverageFps,"FPS");Add("live.low1","Кадры","1% low · худшие интервалы",stats?.OnePercentLow,"FPS");Add("live.low01","Кадры","0,1% low · от 1000 кадров",stats?.PointOnePercentLow,"FPS");
  Add("live.ms","Кадры","Среднее время кадра",stats?.AverageMs,"мс");Add("live.p50","Кадры","Время кадра P50",stats?.P50Ms,"мс");Add("live.p95","Кадры","Время кадра P95",stats?.P95Ms,"мс");Add("live.p99","Кадры","Время кадра P99",stats?.P99Ms,"мс");Add("live.max","Кадры","Худший интервал · до 30 с",stats?.MaximumMs,"мс");
  Add("live.present","Кадры","Внутри вызова Present",stats?.PresentMs,"мс");Add("live.render","Кадры","Present → завершение рендера",stats?.RenderReadyMs,"мс");Add("live.display","Кадры","Present → показ (ETW)",stats?.DisplayMs,"мс");Add("live.dropped","Кадры","Не показаны · ETW",stats?.DroppedPercent,"%");
  frameChart.Visibility=stats is not null&&!preferences.MonitoringHidden.Contains("live.ms")?Visibility.Visible:Visibility.Collapsed;
  frameChart.Unit="мс";frameChart.Seconds=600;frameChart.Now=600;frameChart.Points=stats?.RecentFrameTimes.Select((v,i)=>new TracePoint(600-stats.RecentFrameTimes.Length+i+1,v)).ToArray()??[];frameChart.InvalidateVisual();
  var ping=probe?.Summary;if(probe is not null)result.Add(new("ping.target","Сетевой замер","Адрес и тип проверки",probe.Status));
  Add("ping.latest","Сетевой замер","Последний RTT",ping?.Latest,"мс");Add("ping.average","Сетевой замер","Средний RTT · 60 проб",ping?.Average,"мс");Add("ping.min","Сетевой замер","Минимальный RTT",ping?.Minimum,"мс");Add("ping.max","Сетевой замер","Максимальный RTT",ping?.Maximum,"мс");Add("ping.jitter","Сетевой замер","Разброс RTT (jitter)",ping?.Jitter,"мс");Add("ping.loss","Сетевой замер","Нет ответов ICMP · не потери игры",ping?.LossPercent,"%");
  return result;
 }
 void RecordCharts(List<MonitorMetric> values){
  double now=clock.Elapsed.TotalSeconds;foreach(var v in values){definitions[v.Id]=v;if(v.Number.HasValue||v.Id.StartsWith("live.")||v.Id.StartsWith("ping."))history.Add(v.Id,now,v.Number);}
  foreach(var id in history.Ids.Except(values.Select(v=>v.Id)))history.Add(id,now,null);
  foreach(var (choice,_) in chartItems){var available=definitions.Values.Where(v=>v.Unit is not null||v.Id=="live.fps").OrderBy(v=>v.Group).ThenBy(v=>v.Label).Select(v=>new Choice(v.Id,v.Group+" · "+v.Label)).Prepend(new Choice("","Не показывать")).ToArray();
   if(choice.Items.Count!=available.Length){int index=chartItems.FindIndex(c=>c.Choice==choice);string id=preferences.Graphs.ElementAtOrDefault(index)??"";choice.ItemsSource=available;choice.SelectedItem=available.FirstOrDefault(c=>c.Id==id)??available[0];}
  }UpdateCharts();
 }
 void UpdateCharts(){foreach(var (choice,chart) in chartItems){string id=(choice.SelectedItem as Choice)?.Id??"";chart.Points=preferences.MonitoringHidden.Contains(id)?[]:history.Read(id,clock.Elapsed.TotalSeconds,preferences.HistorySeconds);chart.Now=clock.Elapsed.TotalSeconds;chart.Seconds=preferences.HistorySeconds;chart.Unit=definitions.GetValueOrDefault(id)?.Unit??"";chart.InvalidateVisual();}}
 void ReflowCharts(){charts.Columns=lastWidth>=700?2:1;}
 void AddLiveSettings(StackPanel body){
  body.Children.Add(new TextBlock{Text="История графиков",Margin=new Thickness(0,8,0,6)});var duration=new ComboBox{ItemsSource=new[]{30,60,120,300},SelectedItem=preferences.HistorySeconds};body.Children.Add(duration);duration.SelectionChanged+=(_,_)=>{if(duration.SelectedItem is int n)preferences.HistorySeconds=n;};
  var enabled=new CheckBox{Content="Сетевые замеры ICMP каждые 2 с",IsChecked=preferences.NetworkProbeEnabled,Foreground=Theme.Brush("TextPrimary"),Margin=new Thickness(0,12,0,6)};body.Children.Add(enabled);enabled.Checked+=(_,_)=>preferences.NetworkProbeEnabled=true;enabled.Unchecked+=(_,_)=>preferences.NetworkProbeEnabled=false;
  body.Children.Add(new TextBlock{Text="Адрес для ping · пусто = ваш роутер",Margin=new Thickness(0,4,0,6)});var host=new TextBox{Text=preferences.NetworkHost,MaxLength=253};body.Children.Add(host);host.TextChanged+=(_,_)=>{string value=host.Text.Trim();if(value.Length==0||Uri.CheckHostName(value)!=UriHostNameType.Unknown)preferences.NetworkHost=value;};
 }
 sealed class UniformGridCompat:System.Windows.Controls.Primitives.UniformGrid {public UniformGridCompat(){Columns=2;}}
}
