using System.IO;
using System.Text;
namespace QuietPC.App;
sealed record PowerSample(DateTimeOffset Time,double GpuWatts,string Profile,bool Enabled);
sealed class PowerHistory {
 readonly Queue<PowerSample> samples=new();
 public void Add(double? watts,string? profile,bool enabled){if(watts is not double value||value<=0)return;samples.Enqueue(new(DateTimeOffset.Now,value,profile??"off",enabled));while(samples.Count>600)samples.Dequeue();}
 public PowerSample[] Recent()=>[..samples];
 public void Export(string path){var builder=new StringBuilder("Time,GPUWatts,Profile,Enabled\r\n");foreach(var sample in samples)builder.AppendLine($"{sample.Time:O},{sample.GpuWatts.ToString("F3",System.Globalization.CultureInfo.InvariantCulture)},\"{sample.Profile.Replace("\"","\"\"")}\",{sample.Enabled}");File.WriteAllText(path,builder.ToString(),Encoding.UTF8);}
}
