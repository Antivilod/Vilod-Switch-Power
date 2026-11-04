using System.Text.Json;
namespace QuietPC.Core;
public sealed record Profile(string Id, string Name, int? Fps, uint Epp, string Strategy = "Авто",uint GpuPercent=0,bool Boost=false) {
 public override string ToString()=>Name;
 public void Validate() { if (string.IsNullOrWhiteSpace(Id) || Id.Length>80 || string.IsNullOrWhiteSpace(Name) || Name.Length>80 || Fps is <20 or >500 || Epp>100 || (GpuPercent!=0&&GpuPercent is <30 or >100) || !new[]{"Авто","Больше экономии","Больше плавности"}.Contains(Strategy)) throw new ArgumentException("Недопустимый профиль"); }
 public static Profile[] Defaults => [new("60","60 FPS",60,70),new("120","120 FPS",120,50),new("200","200 FPS",200,30),new("custom","Пользовательский",300,50),new("work","Работа",null,80),new("boost","Game Boost",200,0,"Больше плавности",100,true),new("400","400 FPS",400,30,"Авто",95),new("competitive","Соревновательный · 400 FPS",400,0,"Больше плавности",100,true)];
}
public sealed class Preferences {
 public int Schema {get;set;}=1;
 public string Selected {get;set;}="work";
 public bool DiscoverSteamGames {get;set;}=true;
 public List<SteamPreset> SteamPresets {get;set;}=[];
 public string DefaultGameProfile {get;set;}="120";
 public string GamingDefault()=>Profiles.FirstOrDefault(p=>p.Id==DefaultGameProfile&&p.Fps!=null)?.Id??Profiles.FirstOrDefault(p=>p.Id=="120"&&p.Fps!=null)?.Id??Profiles.FirstOrDefault(p=>p.Fps!=null)?.Id??"";
 public string? ResolveGamingProfile(IEnumerable<string> running,IReadOnlyList<SteamGame> games){
  if(Selected=="competitive"&&Profiles.Any(p=>p.Id==Selected&&p.Fps is not null))return Selected;
  return SteamLibrary.Resolve(running,games,SteamPresets,GamingDefault());
 }
 public void ValidateSteam(){if(SteamPresets.Count>5000||SteamPresets.Select(p=>p.AppId).Distinct().Count()!=SteamPresets.Count)throw new ArgumentException("Повреждён список Steam");foreach(var p in SteamPresets)if(!uint.TryParse(p.AppId,out uint id)||id==0||!Profiles.Any(q=>q.Id==p.ProfileId&&q.Fps!=null))throw new ArgumentException("Неверный игровой профиль Steam");}
 public List<Profile> Profiles {get;set;}=[..Profile.Defaults];
 public int EnsureBuiltIns(){int count=0;foreach(var id in new[]{"boost","400","competitive"})if(Profiles.Count<50&&!Profiles.Any(p=>p.Id==id)){Profiles.Add(Profile.Defaults.Single(p=>p.Id==id));count++;}return count;}
 public List<string> Games {get;set;}=[];
 public bool Automatic {get;set;}
 public bool LightTheme {get;set;}
 public bool GlobalFps {get;set;}=true;
 public bool ResumeOnLaunch {get;set;}
 public bool WasEnabled {get;set;}
 public bool AutoClean {get;set;}
 public string MemoryTrigger {get;set;}="Either";
 public bool PauseCleanInGames {get;set;}=true;
 public int? MemoryWarningThreshold {get;set;}
 public int WarningThreshold()=>MemoryWarningThreshold??MemoryThreshold-10;
 public int MemoryThreshold {get;set;}=90;
 public void SetMemoryLimits(int warning,int critical,int minutes){MemoryLevels.Validate(warning,critical,minutes);MemoryWarningThreshold=warning;MemoryThreshold=critical;CleanIntervalMinutes=minutes;}
 public int CleanIntervalMinutes {get;set;}=20;
 public List<string> BackgroundApps {get;set;}=[];
 public int DelaySeconds {get;set;}=30;
 public void Validate() { ValidateSteam();MemoryLevels.Validate(WarningThreshold(),MemoryThreshold,CleanIntervalMinutes);if(Schema!=1 || !new[]{"Threshold","Timer","Either"}.Contains(MemoryTrigger) || MemoryThreshold is <70 or >95 || CleanIntervalMinutes is <5 or >180 || Profiles.Count is <1 or >50 || Games.Count>200 || BackgroundApps.Count>100 || DelaySeconds is <5 or >600) throw new ArgumentException("Неподдерживаемая схема/пределы"); foreach(var p in Profiles)p.Validate(); if(Profiles.Select(p=>p.Id).Distinct().Count()!=Profiles.Count || !Profiles.Any(p=>p.Id==Selected))throw new ArgumentException("Профили повреждены"); foreach(var p in Games.Concat(BackgroundApps)) if(!Path.IsPathFullyQualified(p)||!p.EndsWith(".exe",StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Нужен полный путь .exe"); }
}
public static class AtomicJson {
 public static readonly JsonSerializerOptions Options=new(){WriteIndented=true,PropertyNameCaseInsensitive=false};
 public static T? Read<T>(string path)=>File.Exists(path)?JsonSerializer.Deserialize<T>(File.ReadAllText(path),Options):default;
 public static void Write<T>(string path,T value) {
  Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
  string temp=path+".new";
  using(var f=new FileStream(temp,FileMode.Create,FileAccess.Write,FileShare.None,4096,FileOptions.WriteThrough)) { JsonSerializer.Serialize(f,value,Options); f.Flush(true); }
  File.Move(temp,path,true);
 }
}
public sealed record Scheme(Guid Id,uint Ac,uint Dc,string Fingerprint="");
public sealed class Journal {
 public int Schema {get;set;}=1;
 public Guid Original {get;set;}
 public List<Scheme> Owned {get;set;}=[];
 public Guid? Last {get;set;}
 public Guid? Pending {get;set;}
 public Guid? Deleting {get;set;}
}
public interface IPower {
 Guid Active(); Scheme Clone(Guid source,uint epp); void Activate(Guid id); Scheme Read(Guid id); void Delete(Guid id);
}
public sealed record Outcome(bool Enabled,string State,string Detail,string? AppliedProfile=null);
public sealed class RecoveryEngine(IPower power,string journalPath) {
 public bool Enabled {get;private set;}
 public Journal? Current {get;private set;}=AtomicJson.Read<Journal>(journalPath);
 void Save()=>AtomicJson.Write(journalPath,Current);
 public Outcome Apply(Profile profile) {
  profile.Validate();
  if(Current is null) {Current=new(){Original=power.Active()};Save();}
  if(Current.Schema!=1)throw new InvalidOperationException("Неизвестный журнал; требуется диагностика");
  var active=power.Active();
  if(active!=Current.Original && active!=Current.Last && active!=Current.Pending)throw new InvalidOperationException("Конфликт: активная схема изменена извне");
  if(Current.Last is Guid last && active==last && !Matches(last))throw new InvalidOperationException("Конфликт: настройки нашей схемы изменены извне");
  try {
   var clone=Current.Owned.FirstOrDefault(s=>s.Ac==profile.Epp&&s.Dc==profile.Epp&&power.Read(s.Id)==s)??power.Clone(Current.Original,profile.Epp);
   if(!Current.Owned.Contains(clone))Current.Owned.Add(clone); Current.Pending=clone.Id; Save(); // before activation
   power.Activate(clone.Id);
   if(power.Active()!=clone.Id || power.Read(clone.Id)!=clone)throw new IOException("Проверка записи питания не пройдена");
   Current.Last=clone.Id; Current.Pending=null;Save();Enabled=true;
   return new(true,profile.Fps is null?"Применено":"Частично применено",profile.Fps is null?"Политика CPU применена. Экономия не измерена.":"CPU применён. FPS-лимит недоступен в этой сборке.");
  } catch { var rollback=Restore();if(rollback.State=="Конфликт")throw new IOException(rollback.Detail);throw; }
 }
 bool Matches(Guid id)=>Current!.Owned.Any(s=>s.Id==id && power.Read(id)==s);
 public Outcome Restore() {
  Enabled=false;if(Current is null)return new(false,"Выключено","Собственных изменений нет");
  if(Current.Schema!=1)return new(false,"Конфликт","Неизвестная версия журнала");
  var active=power.Active();
  if(active==Current.Last || active==Current.Pending) {
   if(!Matches(active))return new(false,"Конфликт","Схема изменена другой программой. Автоматический откат остановлен.");
   power.Activate(Current.Original);
   if(power.Active()!=Current.Original)throw new IOException("Исходная схема не восстановлена");
  } else if(active!=Current.Original) return new(false,"Конфликт","Активная схема изменена извне; журнал сохранён. Верните исходную схему и повторите восстановление.");
  foreach(var s in Current.Owned.ToArray()) {
   if(Current.Deleting==s.Id && power.Read(s.Id).Fingerprint=="missing"){Current.Owned.Remove(s);Current.Deleting=null;Save();continue;}
   if(power.Read(s.Id)!=s)return new(false,"Конфликт","Производная схема изменена извне; она не удалена");
   Current.Deleting=s.Id;Save();power.Delete(s.Id);Current.Owned.Remove(s);Current.Deleting=null;Save();
  }
  File.Delete(journalPath); Current=null;
  return new(false,"Выключено","Исходная схема восстановлена");
 }
}
public sealed class AutoMode {
 DateTimeOffset? emptySince;
 public bool Suppressed {get;private set;}
 public string? LastGaming {get;private set;}
 public void Manual(Profile p) { Suppressed=p.Fps is null;if(p.Fps is not null)LastGaming=p.Id;emptySince=null; }
 public void Select(Profile selected,bool automatic,Profile? fallback){Manual(selected.Fps is null&&automatic&&fallback is not null?fallback:selected);}
 public string? Tick(bool enabled,bool automatic,bool gameRunning,string? applied,int delay,DateTimeOffset now) {
  if(!enabled||!automatic||Suppressed||LastGaming is null){emptySince=null;return null;}
  if(gameRunning){emptySince=null;return applied!=LastGaming?LastGaming:null;}
  emptySince??=now; return now-emptySince>=TimeSpan.FromSeconds(delay)&&applied!="work"?"work":null;
 }
}
public sealed record Request(string Command,Profile? Profile=null,string[]? Games=null,bool GlobalFps=false,int? IdleGamingFps=null,int[]? GamePids=null,int[]? BackgroundPids=null);
public sealed record Reply(bool Ok,bool Enabled,string State,string Detail,string? AppliedProfile=null);
