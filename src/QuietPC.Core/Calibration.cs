using System.Globalization;
namespace QuietPC.Core;
public sealed record FrameSample(int ProcessId,string SwapChain,double Seconds,double Milliseconds);
public sealed record FrameSummary(int Frames,double AverageFps,double OnePercentLow,double P99Milliseconds,double Seconds);
public sealed record PowerTrial(uint GpuPercent,double? LimitWatts,FrameSummary Frames,double? AverageGpuWatts);
public sealed record TuningDecision(bool Valid,uint? RecommendedPercent,string Reason,bool SavingsMeasured);
public static class FrameAnalysis {
 public static string[] Csv(string line){
  var cells=new List<string>();var value=new System.Text.StringBuilder();bool quote=false;
  for(int i=0;i<line.Length;i++){char c=line[i];if(c=='"'){if(quote&&i+1<line.Length&&line[i+1]=='"'){value.Append('"');i++;}else quote=!quote;}else if(c==','&&!quote){cells.Add(value.ToString());value.Clear();}else value.Append(c);}
  if(quote)throw new FormatException("Незавершённая строка CSV");cells.Add(value.ToString());return cells.ToArray();
 }
 public static FrameSample[] Parse(string text,int processId){
  if(text.Length>100_000_000)throw new IOException("Слишком большой замер");
  using var reader=new StringReader(text);var header=Csv(reader.ReadLine()??throw new FormatException("Пустой CSV"));
  int Index(string name)=>Array.FindIndex(header,c=>c.Trim('\uFEFF').Equals(name,StringComparison.OrdinalIgnoreCase));
  int pid=Index("ProcessID"),chain=Index("SwapChainAddress"),time=Index("TimeInSeconds"),ms=Index("msBetweenPresents");
  if(pid<0||chain<0||time<0||ms<0)throw new FormatException("Нужен CSV PresentMon с метриками v1");
  var samples=new List<FrameSample>();string? line;
  while((line=reader.ReadLine()) is not null){
   string[] cells;try{cells=Csv(line);}catch(FormatException){continue;}if(cells.Length!=header.Length)continue;
   if(!int.TryParse(cells[pid],out int id)||id!=processId||!double.TryParse(cells[time],NumberStyles.Float,CultureInfo.InvariantCulture,out double t)||!double.TryParse(cells[ms],NumberStyles.Float,CultureInfo.InvariantCulture,out double m)||!double.IsFinite(t)||!double.IsFinite(m)||t<0||m<=0||m>60000)continue;
   samples.Add(new(id,cells[chain],t,m));
  }
  return samples.OrderBy(s=>s.Seconds).ToArray();
 }
 public static FrameSummary Summarize(IEnumerable<FrameSample> source){
  var groups=source.GroupBy(s=>s.SwapChain).OrderByDescending(g=>g.Count()).ToArray();if(groups.Length==0)throw new InvalidOperationException("Нет кадров выбранного процесса");
  var frames=groups[0].OrderBy(s=>s.Seconds).ToArray();if(frames.Length<30)throw new InvalidOperationException("Недостаточно кадров для замера");
  double duration=frames[^1].Seconds-frames[0].Seconds;if(duration<1)throw new InvalidOperationException("Замер слишком короткий");
  var ms=frames.Select(s=>s.Milliseconds).OrderBy(x=>x).ToArray();int slow=Math.Max(1,(int)Math.Ceiling(ms.Length*.01));
  return new(frames.Length,1000/ms.Average(),1000/ms.TakeLast(slow).Average(),ms[Math.Clamp((int)Math.Ceiling(ms.Length*.99)-1,0,ms.Length-1)],duration);
 }
}
public static class PowerTuner {
 public static bool Stable(PowerTrial baseline,PowerTrial trial,int target)=>trial.Frames.Frames>=30&&trial.Frames.AverageFps>=target*.95&&trial.Frames.P99Milliseconds<=Math.Max(1000.0/target*1.35,baseline.Frames.P99Milliseconds*1.15);
 public static uint[] Steps(uint start){if(start is <30 or >100)throw new ArgumentOutOfRangeException(nameof(start));var result=new List<uint>{start};for(int i=1;i<=7;i++){int next=(int)start-i*5;if(next<30)break;result.Add((uint)next);}return result.ToArray();}
 public static TuningDecision Choose(IReadOnlyList<PowerTrial> trials,int target){
  if(target is <20 or >500||trials.Count==0)return new(false,null,"Нет исходного замера",false);
  var baseline=trials[0];if(baseline.Frames.AverageFps>target*1.1)return new(false,null,"Выбранный FPS-лимит не подтверждён. Выберите профиль до запуска игры и повторите после перезапуска игры.",false);if(!Stable(baseline,baseline,target))return new(false,null,"Исходная нагрузка уже не удерживает целевой FPS. Снижение мощности не рекомендовано.",false);
  var good=new List<PowerTrial>{baseline};foreach(var trial in trials.Skip(1)){if(!Stable(baseline,trial,target))break;good.Add(trial);}
  // Reserve one already measured step above the lowest stable setting.
  var candidate=good.Count>=3?good[^2]:baseline;
  if(candidate==baseline)return new(true,null,"Недостаточно проверенного запаса для снижения мощности",false);
  if(candidate.AverageGpuWatts is not double watts||baseline.AverageGpuWatts is not double original)return new(true,null,"Нет достоверного замера мощности GPU. Экономия не подтверждена.",false);
  if(original-watts<Math.Max(2,original*.03))return new(true,null,"Заметная экономия GPU на этой сцене не подтверждена. Исходный профиль сохранён.",false);
  return new(true,candidate.GpuPercent,"Найден проверенный режим с запасом на один шаг. Результат относится к измеренной сцене, а не ко всей игре.",true);
 }
}
