using System.Globalization;
namespace QuietPC.Core;

public sealed record FrameObservation(int ProcessId,string Chain,double Seconds,double FrameMs,double? PresentMs,double? RenderReadyMs,double? DisplayMs,bool? Dropped);
public sealed class FrameCsvDecoder {
 readonly string[] header;readonly int pid,chain,time,ms,present,render,display,dropped;readonly int target;
 public FrameCsvDecoder(string line,int target){
  header=FrameAnalysis.Csv(line);this.target=target;
  int Index(string name)=>Array.FindIndex(header,s=>s.Trim('\uFEFF').Equals(name,StringComparison.OrdinalIgnoreCase));
  pid=Index("ProcessID");chain=Index("SwapChainAddress");time=Index("TimeInSeconds");ms=Index("msBetweenPresents");present=Index("msInPresentAPI");render=Index("msUntilRenderComplete");display=Index("msUntilDisplayed");dropped=Index("Dropped");
  if(pid<0||chain<0||time<0||ms<0)throw new FormatException("Неподдерживаемый формат PresentMon v1");
 }
 public FrameObservation? Parse(string line){
  string[] cells;try{cells=FrameAnalysis.Csv(line);}catch(FormatException){return null;}
  if(cells.Length!=header.Length||!int.TryParse(cells[pid],out int id)||id!=target)return null;
  double? Number(int index)=>index>=0&&double.TryParse(cells[index],NumberStyles.Float,CultureInfo.InvariantCulture,out double n)&&double.IsFinite(n)&&n>=0?n:null;
  if(Number(time) is not double t||Number(ms) is not double m||m<=0||m>60000)return null;
  bool? drop=dropped>=0?cells[dropped] switch{"0"=>false,"1"=>true,_=>null}:null;
  double? shown=Number(display);if(drop!=false||shown<=0)shown=null;
  double? ready=Number(render);if(ready<=0)ready=null;
  return new(id,cells[chain],t,m,Number(present),ready,shown,drop);
 }
}
public sealed record LiveFrameStats(int Frames,double Duration,double Fps,double AverageFps,double OnePercentLow,double? PointOnePercentLow,double AverageMs,double P50Ms,double P95Ms,double P99Ms,double MaximumMs,double? PresentMs,double? RenderReadyMs,double? DisplayMs,double? DroppedPercent,double[] RecentFrameTimes);
public sealed record LiveFramePacket(LiveFrameStats? Stats,string Status);
public sealed class LiveFrameWindow {
 readonly Queue<FrameObservation> frames=[];double latest;
 public const int Capacity=32768;
 public int Count=>frames.Count;
 public void Add(FrameObservation value){
  if(!double.IsFinite(value.Seconds)||!double.IsFinite(value.FrameMs)||value.Seconds<0||value.FrameMs<=0||value.FrameMs>60000)return;
  latest=Math.Max(latest,value.Seconds);if(value.Seconds<latest-30)return;frames.Enqueue(value);
  while(frames.Count>Capacity||frames.Count>0&&frames.Peek().Seconds<latest-30)frames.Dequeue();
 }
 public LiveFrameStats? Snapshot(){
  if(frames.Count<30)return null;
  var chain=frames.Where(f=>f.Seconds>=latest-2).GroupBy(f=>f.Chain).OrderByDescending(g=>g.Count()).Select(g=>g.Key).FirstOrDefault();
  if(chain is null)return null;
  var all=frames.Where(f=>f.Chain==chain&&f.Seconds>=latest-30).OrderBy(f=>f.Seconds).ToArray();
  var recent=all.Where(f=>f.Seconds>=latest-2).ToArray();
  double duration=all[^1].Seconds-all[0].Seconds;if(recent.Length<30||duration<.5)return null;
  var times=all.Select(f=>f.FrameMs).OrderBy(n=>n).ToArray();
  double Percentile(double p)=>times[Math.Clamp((int)Math.Ceiling(times.Length*p)-1,0,times.Length-1)];
  double Low(double fraction)=>1000/times.TakeLast(Math.Max(1,(int)Math.Ceiling(times.Length*fraction))).Average();
  double? Average(Func<FrameObservation,double?> pick){var a=all.Select(pick).Where(n=>n.HasValue).Select(n=>n!.Value).ToArray();return a.Length>0?a.Average():null;}
  var drop=all.Where(f=>f.Dropped.HasValue).ToArray();
  return new(all.Length,duration,1000/recent.Average(f=>f.FrameMs),1000/times.Average(),Low(.01),all.Length>=1000?Low(.001):null,times.Average(),Percentile(.5),Percentile(.95),Percentile(.99),times[^1],Average(f=>f.PresentMs),Average(f=>f.RenderReadyMs),Average(f=>f.DisplayMs),drop.Length>0?100.0*drop.Count(f=>f.Dropped==true)/drop.Length:null,recent.TakeLast(600).Select(f=>f.FrameMs).ToArray());
 }
}
public sealed record PingSummary(int Sent,int Received,double? Latest,double? Average,double? Minimum,double? Maximum,double? Jitter,double LossPercent);
public sealed class PingWindow {
 readonly Queue<double?> samples=[];
 public void Add(double? milliseconds){if(milliseconds is double n&&(!double.IsFinite(n)||n<0))throw new ArgumentOutOfRangeException(nameof(milliseconds));samples.Enqueue(milliseconds);while(samples.Count>60)samples.Dequeue();}
 public PingSummary? Snapshot(){
  if(samples.Count==0)return null;var valid=samples.Where(v=>v.HasValue).Select(v=>v!.Value).ToArray();var jitter=new List<double>();double? previous=null;
  foreach(var sample in samples){if(sample.HasValue&&previous.HasValue)jitter.Add(Math.Abs(sample.Value-previous.Value));previous=sample;}
  return new(samples.Count,valid.Length,samples.Last(),valid.Length>0?valid.Average():null,valid.Length>0?valid.Min():null,valid.Length>0?valid.Max():null,jitter.Count>0?jitter.Average():null,100.0*(samples.Count-valid.Length)/samples.Count);
 }
}
public sealed record TracePoint(double Seconds,double? Value);
public sealed class MetricHistory {
 readonly Dictionary<string,Queue<TracePoint>> series=[];
 public void Add(string id,double seconds,double? value){
  if(!double.IsFinite(seconds)||seconds<0||value is double n&&!double.IsFinite(n))throw new ArgumentOutOfRangeException(nameof(value));
  if(!series.TryGetValue(id,out var queue)){if(series.Count>=512)return;series[id]=queue=new();}
  if(queue.Count>0&&seconds<queue.Last().Seconds)return;
  queue.Enqueue(new(seconds,value));while(queue.Count>400||queue.Count>0&&queue.Peek().Seconds<seconds-300)queue.Dequeue();
 }
 public TracePoint[] Read(string id,double now,int seconds)=>series.TryGetValue(id,out var queue)?queue.Where(p=>p.Seconds>=now-seconds).ToArray():[];
 public string[] Ids=>series.Keys.ToArray();
}
