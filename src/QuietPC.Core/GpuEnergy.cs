namespace QuietPC.Core;
public sealed record GpuEnergySnapshot(double WattHours,double ObservedSeconds,double? AverageWatts,double? PeakWatts,int Samples);
// Integrates only adjacent, valid GPU samples. Missing readings and long gaps
// break continuity rather than inventing energy for sleep or suspended polling.
public sealed class GpuEnergy {
 double? previousTime,previousWatts;double joules,observed;double? peak;int samples;
 public void Add(double seconds,double? watts){
  if(!double.IsFinite(seconds)||seconds<0)throw new ArgumentOutOfRangeException(nameof(seconds));
  if(previousTime is double before&&seconds<=before)return;
  bool valid=watts is double w&&double.IsFinite(w)&&w>=0&&w<=10000;
  if(valid){samples++;peak=peak is double maximum?Math.Max(maximum,watts!.Value):watts;
   if(previousTime is double time&&previousWatts is double power){double elapsed=seconds-time;if(elapsed<=15){joules+=(power+watts!.Value)*.5*elapsed;observed+=elapsed;}}
  }
  previousTime=seconds;previousWatts=valid?watts:null;
 }
 public GpuEnergySnapshot Snapshot()=>new(joules/3600,observed,observed>0?joules/observed:null,peak,samples);
}
