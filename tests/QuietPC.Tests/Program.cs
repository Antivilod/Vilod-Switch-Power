using QuietPC.Core;
int passed=0,failed=0;
void Test(string name,Action test){try{test();Console.WriteLine("PASS "+name);passed++;}catch(Exception e){Console.WriteLine("FAIL "+name+": "+e);failed++;}}
void Assert(bool ok){if(!ok)throw new Exception("Assertion failed");}
void Throws(Action action){try{action();}catch{return;}throw new Exception("Expected failure");}
(string,Fake,RecoveryEngine) Setup(){var path=Path.Combine(Path.GetTempPath(),"quiet-tests",Guid.NewGuid()+".json");var fake=new Fake();return(path,fake,new(fake,path));}
Test("profile boundaries incl 30 and 300",()=>{new Profile("a","a",30,50).Validate();new Profile("a","a",300,50).Validate();Throws(()=>new Profile("a","a",19,50).Validate());Throws(()=>new Profile("a","a",501,50).Validate());});
Test("switches preserve original snapshot",()=>{var(path,f,e)=Setup();var orig=f.Active();e.Apply(Profile.Defaults[0]);e.Apply(Profile.Defaults[1]);Assert(e.Current!.Original==orig);e.Restore();Assert(f.Active()==orig&&!File.Exists(path));});
Test("profile selection is data only",()=>{var(_,f,e)=Setup();var orig=f.Active();var prefs=new Preferences{Selected="120"};prefs.Validate();Assert(f.Active()==orig&&e.Current is null);});
Test("activation failure rolls back",()=>{var(_,f,e)=Setup();var orig=f.Active();f.FailActivate=true;Throws(()=>e.Apply(Profile.Defaults[0]));Assert(f.Active()==orig&&!e.Enabled&&e.Current is null);});
Test("readback failure rolls back",()=>{var(_,f,e)=Setup();var orig=f.Active();f.IgnoreActivate=true;Throws(()=>e.Apply(Profile.Defaults[0]));Assert(f.Active()==orig&&e.Current is null);});
Test("external active scheme preserved",()=>{var(_,f,e)=Setup();e.Apply(Profile.Defaults[0]);var other=Guid.NewGuid();f.Current=other;Assert(e.Restore().State=="Конфликт"&&f.Active()==other&&e.Current is not null);});
Test("external setting preserved",()=>{var(_,f,e)=Setup();e.Apply(Profile.Defaults[0]);var own=f.Active();f.Schemes[own]=f.Schemes[own] with{Fingerprint="external"};Assert(e.Restore().State=="Конфликт"&&f.Active()==own);});
Test("restart restores interrupted transaction",()=>{var(path,f,e)=Setup();var orig=f.Active();e.Apply(Profile.Defaults[0]);var restarted=new RecoveryEngine(f,path);restarted.Restore();Assert(f.Active()==orig&&restarted.Current is null);});
Test("pending activation crash restores",()=>{var(path,f,e)=Setup();var orig=f.Active();e.Apply(Profile.Defaults[0]);var j=AtomicJson.Read<Journal>(path)!;j.Pending=j.Last;j.Last=null;AtomicJson.Write(path,j);new RecoveryEngine(f,path).Restore();Assert(f.Active()==orig);});
Test("identical profiles reuse clone",()=>{var(_,f,e)=Setup();e.Apply(Profile.Defaults[0]);e.Apply(Profile.Defaults[0]);Assert(e.Current!.Owned.Count==1);e.Restore();});
Test("recovery delete is restartable",()=>{var(path,f,e)=Setup();var orig=f.Active();e.Apply(Profile.Defaults[0]);var j=e.Current!;f.Current=orig;j.Deleting=j.Last;f.Delete(j.Last!.Value);AtomicJson.Write(path,j);Assert(new RecoveryEngine(f,path).Restore().State=="Выключено");});
Test("manual work suppresses automatic return",()=>{var a=new AutoMode();a.Manual(Profile.Defaults[0]);a.Manual(Profile.Defaults[4]);Assert(a.Tick(true,true,true,"work",30,DateTimeOffset.UtcNow)==null);});
Test("delay then game return; minimized still running",()=>{var a=new AutoMode();a.Manual(Profile.Defaults[0]);var t=DateTimeOffset.UtcNow;Assert(a.Tick(true,true,false,"60",30,t)==null);Assert(a.Tick(true,true,false,"60",30,t.AddSeconds(29))==null);Assert(a.Tick(true,true,false,"60",30,t.AddSeconds(30))=="work");Assert(a.Tick(true,true,true,"work",30,t.AddSeconds(31))=="60");Assert(a.Tick(true,true,true,"60",30,t.AddSeconds(99))==null);});
Test("off blocks automation",()=>{var a=new AutoMode();a.Manual(Profile.Defaults[0]);Assert(a.Tick(false,true,true,"work",30,DateTimeOffset.UtcNow)==null);});
Test("atomic prefs roundtrip",()=>{var(path,_,_)=Setup();var p=new Preferences();AtomicJson.Write(path,p);AtomicJson.Read<Preferences>(path)!.Validate();File.Delete(path);});
Test("delete failure retains journal",()=>{var(path,f,e)=Setup();e.Apply(Profile.Defaults[0]);f.FailDelete=true;Throws(()=>e.Restore());Assert(File.Exists(path));f.FailDelete=false;new RecoveryEngine(f,path).Restore();Assert(!File.Exists(path));});

(string,string,FakeDrs,NvidiaFpsRecovery) FpsSetup(){var dir=Path.Combine(Path.GetTempPath(),"quiet-fps",Guid.NewGuid().ToString());Directory.CreateDirectory(dir);var game=Path.Combine(dir,"game.exe");File.WriteAllText(game,"");var path=Path.Combine(dir,"fps.json");var api=new FakeDrs();return(path,game,api,new(path,()=>api));}
Test("FPS inherited state restored, override removed",()=>{var(path,game,a,e)=FpsSetup();var original=a.Value;Assert(e.Apply([game],60).Contains("DRS FPS записан"));Assert(a.Value.Value==60);e.Restore();Assert(a.Value==original&&!File.Exists(path));});
Test("FPS existing user cap restored exactly",()=>{var(_,game,a,e)=FpsSetup();a.Value=new(true,144,0,false);e.Apply([game],60);e.Apply([game],120);e.Restore();Assert(a.Value==new DrsValue(true,144,0,false));});
Test("FPS external change retained",()=>{var(path,game,a,e)=FpsSetup();e.Apply([game],60);a.Value=new(true,72,0,false);Throws(()=>e.Restore());Assert(a.Value.Value==72&&File.Exists(path));});
Test("FPS crash before second write",()=>{var(path,game,a,e)=FpsSetup();var orig=a.Value;e.Apply([game],60);var j=AtomicJson.Read<FpsJournal>(path)!;j.Changes[0]=j.Changes[0] with{Last=120,Previous=60};AtomicJson.Write(path,j);new NvidiaFpsRecovery(path,()=>a).Restore();Assert(a.Value==orig);});
Test("FPS crash before first write",()=>{var(path,_,a,_e)=FpsSetup();var j=new FpsJournal{Changes=[new("game-profile",a.Value,60)]};AtomicJson.Write(path,j);new NvidiaFpsRecovery(path,()=>a).Restore();Assert(!File.Exists(path));});
Test("FPS failed save journal enables recovery",()=>{var(path,game,a,e)=FpsSetup();var orig=a.Value;a.FailSave=true;Throws(()=>e.Apply([game],60));Assert(File.Exists(path));new NvidiaFpsRecovery(path,()=>a).Restore();Assert(a.Value==orig);});
Test("FPS profile not found never writes",()=>{var(_,game,a,e)=FpsSetup();a.NoProfile=true;var before=a.Value;Assert(e.Apply([game],60).StartsWith("FPS не применён"));Assert(a.Value==before&&!e.Pending);});
Test("global FPS can apply without a games list",()=>{var(path,_,a,e)=FpsSetup();var original=a.Value;Assert(e.Apply([],60,true).Contains("DRS FPS записан"));e.Restore();Assert(a.Value==original&&!File.Exists(path));});
(string,FakeGpu,GpuPowerRecovery) GpuSetup(){var path=Path.Combine(Path.GetTempPath(),"vsp-gpu-"+Guid.NewGuid()+".json");var a=new FakeGpu();return(path,a,new(path,()=>a));}
Test("GPU limit clamps to hardware minimum and restores",()=>{var(path,a,e)=GpuSetup();e.Apply(30);Assert(a.Value==100000);e.Restore();Assert(a.Value==180000&&!File.Exists(path));});
Test("GPU repeated switches retain original",()=>{var(_,a,e)=GpuSetup();e.Apply(70);Assert(a.Value==126000);e.Apply(85);Assert(a.Value==153000);e.Restore();Assert(a.Value==180000);});
Test("GPU external write never overwritten",()=>{var(path,a,e)=GpuSetup();e.Apply(70);a.Value=144000;Throws(()=>e.Restore());Assert(a.Value==144000&&File.Exists(path));});
Test("GPU rejected write can recover journal",()=>{var(path,a,e)=GpuSetup();a.Reject=true;Throws(()=>e.Apply(70));e.Restore();Assert(!File.Exists(path)&&a.Value==180000);});
Test("GPU interrupted write restores original",()=>{var(path,a,e)=GpuSetup();e.Apply(70);new GpuPowerRecovery(path,()=>a).Restore();Assert(a.Value==180000&&!File.Exists(path));});
Test("GPU invalid percentage rejected",()=>{var(_,_,e)=GpuSetup();Throws(()=>e.Apply(101));Throws(()=>e.Apply(0));});
Test("theme and scope preferences persist",()=>{var(path,_,_)=Setup();AtomicJson.Write(path,new Preferences{LightTheme=true,GlobalFps=false,ResumeOnLaunch=true});var p=AtomicJson.Read<Preferences>(path)!;Assert(p.LightTheme&&!p.GlobalFps&&p.ResumeOnLaunch);File.Delete(path);});

(string,FakePriority,GameBoostRecovery) BoostSetup(){var path=Path.Combine(Path.GetTempPath(),"vsp-boost-"+Guid.NewGuid()+".json");var a=new FakePriority();a.States[10]=new(10,100,32);a.States[20]=new(20,200,32);return(path,a,new(a,path));}
Test("Game Boost restores game and background priorities",()=>{var(path,a,e)=BoostSetup();e.Apply([10],[20]);Assert(a.States[10].Priority==32768&&a.States[20].Priority==16384);e.Restore();Assert(a.States[10].Priority==32&&a.States[20].Priority==32&&!File.Exists(path));});
Test("Game Boost stops background throttling when game exits",()=>{var(_,a,e)=BoostSetup();e.Apply([10],[20]);a.States.Remove(10);e.Apply([], [20]);Assert(a.States[20].Priority==32&&!e.Pending);});
Test("Game Boost never restores into a reused process ID",()=>{var(path,a,e)=BoostSetup();e.Apply([10],[]);a.States[10]=new(10,999,16384);e.Restore();Assert(a.States[10].Priority==16384&&!File.Exists(path));});
Test("Game Boost preserves external priority changes",()=>{var(path,a,e)=BoostSetup();e.Apply([10],[]);a.States[10]=a.States[10] with{Priority=128};Throws(e.Restore);Assert(a.States[10].Priority==128&&File.Exists(path));});
Test("Game Boost interrupted write recovers after restart",()=>{var(path,a,e)=BoostSetup();a.Reject=true;Throws(()=>e.Apply([10],[20]));a.Reject=false;new GameBoostRecovery(a,path).Restore();Assert(a.States[10].Priority==32&&!File.Exists(path));});
Test("Game Boost respects manually chosen high priority",()=>{var(_,a,e)=BoostSetup();a.States[10]=a.States[10] with{Priority=128};e.Apply([10],[]);Assert(a.States[10].Priority==128&&!e.Pending);});
Test("Game takes precedence over background list",()=>{var(_,a,e)=BoostSetup();e.Apply([10],[10]);Assert(a.States[10].Priority==32768);e.Restore();});

Test("memory timer fires at 20 minutes even without pressure or idle",()=>{var m=new MemorySchedule();var t=DateTimeOffset.UtcNow;Assert(!m.Due(true,true,"Timer",90,20,true,false,30,t));Assert(!m.Due(true,true,"Timer",90,20,true,false,30,t.AddMinutes(19)));Assert(m.Due(true,true,"Timer",90,20,true,false,30,t.AddMinutes(20)));});
Test("memory timer postpones through game and runs after exit",()=>{var m=new MemorySchedule();var t=DateTimeOffset.UtcNow;m.Due(true,true,"Timer",90,20,true,false,30,t);Assert(!m.Due(true,true,"Timer",90,20,true,true,30,t.AddMinutes(21)));Assert(m.Due(true,true,"Timer",90,20,true,false,30,t.AddMinutes(22)));});
Test("memory pressure must persist and obey cooldown",()=>{var m=new MemorySchedule();var t=DateTimeOffset.UtcNow;Assert(!m.Due(true,true,"Threshold",90,20,true,false,91,t));Assert(m.Due(true,true,"Threshold",90,20,true,false,91,t.AddMinutes(1)));m.Attempted(t.AddMinutes(1));Assert(!m.Due(true,true,"Threshold",90,20,true,false,95,t.AddMinutes(2)));Assert(!m.Due(true,true,"Threshold",90,20,true,false,95,t.AddMinutes(20)));Assert(m.Due(true,true,"Threshold",90,20,true,false,95,t.AddMinutes(21)));});
Test("memory threshold recovery resets pressure debounce",()=>{var m=new MemorySchedule();var t=DateTimeOffset.UtcNow;m.Due(true,true,"Threshold",90,20,true,false,91,t);m.Due(true,true,"Threshold",90,20,true,false,80,t.AddSeconds(50));Assert(!m.Due(true,true,"Threshold",90,20,true,false,91,t.AddSeconds(70)));});
Test("memory disable resets timer and never cleans",()=>{var m=new MemorySchedule();var t=DateTimeOffset.UtcNow;m.Due(true,true,"Either",90,20,true,false,20,t);Assert(!m.Due(false,true,"Either",90,20,true,false,99,t.AddMinutes(30)));Assert(!m.Due(true,true,"Timer",90,20,true,false,30,t.AddMinutes(31)));Assert(m.NextAt(20)==t.AddMinutes(51));});
Test("memory schedule preferences validate",()=>{new Preferences{CleanIntervalMinutes=20,MemoryTrigger="Either",MemoryThreshold=90}.Validate();Throws(()=>new Preferences{MemoryThreshold=60}.Validate());Throws(()=>new Preferences{MemoryTrigger="invalid"}.Validate());});
Test("GPU gaming extra is exact and never accumulates",()=>{var(_,a,e)=GpuSetup();e.Apply(70,5);Assert(a.Value==131000);e.Apply(85,5);Assert(a.Value==158000);e.Apply(95,5);Assert(a.Value==176000);e.Apply(100,5);Assert(a.Value==185000);e.Apply(60,0);Assert(a.Value==108000);e.Restore();Assert(a.Value==180000);});
Test("GPU extra stays within hardware range",()=>{var(_,a,e)=GpuSetup();a.Maximum=182000;e.Apply(100,5);Assert(a.Value==182000);e.Restore();Assert(a.Value==180000);});
Test("GPU extra restores after interrupted update",()=>{var(path,a,e)=GpuSetup();e.Apply(100,5);new GpuPowerRecovery(path,()=>a).Restore();Assert(a.Value==180000&&!File.Exists(path));});
Test("GPU unsupported adapter does not invent a result",()=>{var(_,a,e)=GpuSetup();a.NoDevice=true;Assert(e.Apply(70,5).Contains("недоступно")&&!e.Pending);});
Test("GPU extra validation rejects excess before writing",()=>{var(_,a,e)=GpuSetup();Throws(()=>e.Apply(70,6));Assert(a.Value==180000&&!e.Pending);});
Test("memory warning and critical boundaries",()=>{Assert(MemoryLevels.Classify(79,80,90)==MemoryLevel.Normal);Assert(MemoryLevels.Classify(80,80,90)==MemoryLevel.Warning);Assert(MemoryLevels.Classify(89,80,90)==MemoryLevel.Warning);Assert(MemoryLevels.Classify(90,80,90)==MemoryLevel.Critical);});
Test("memory legacy preference warning adapts to lower critical level",()=>{var prefs=new Preferences{MemoryThreshold=70};prefs.Validate();Assert(prefs.WarningThreshold()==60);});
Test("memory thresholds cannot overlap",()=>{Throws(()=>new Preferences{MemoryWarningThreshold=90,MemoryThreshold=90}.Validate());Throws(()=>new Preferences{MemoryWarningThreshold=91,MemoryThreshold=90}.Validate());});
Test("memory invalid edits leave existing limits intact",()=>{var prefs=new Preferences();Throws(()=>prefs.SetMemoryLimits(91,90,20));Assert(prefs.MemoryThreshold==90&&prefs.WarningThreshold()==80&&prefs.CleanIntervalMinutes==20);});
Test("memory limits persist with interval and warning",()=>{var(path,_,_)=Setup();var prefs=new Preferences();prefs.SetMemoryLimits(75,85,25);AtomicJson.Write(path,prefs);var loaded=AtomicJson.Read<Preferences>(path)!;loaded.Validate();Assert(loaded.WarningThreshold()==75&&loaded.MemoryThreshold==85&&loaded.CleanIntervalMinutes==25);File.Delete(path);});
Test("warning level alone never triggers threshold cleanup",()=>{var m=new MemorySchedule();var t=DateTimeOffset.UtcNow;m.Due(true,true,"Threshold",90,20,true,false,85,t);Assert(!m.Due(true,true,"Threshold",90,20,true,false,85,t.AddMinutes(30)));});

Test("Steam VDF parses escaped paths, braces inside names and comments",()=>{var v=SteamLibrary.Parse("// comment\n\"libraryfolders\" { \"0\" { \"path\" \"D:\\\\Games\\\\Steam\" } \"name\" \"{Game}\" }");var section=(Dictionary<string,object>)v["libraryfolders"];Assert(((Dictionary<string,object>)section["0"])["path"].ToString()==@"D:\Games\Steam"&&section["name"].ToString()=="{Game}");});
Test("Steam VDF rejects partial writes and excess nesting",()=>{Throws(()=>SteamLibrary.Parse("\"x\" { \"a\""));Throws(()=>SteamLibrary.Parse(string.Concat(Enumerable.Repeat("\"x\" { ",40))));});
string SteamFixture(){string root=Path.Combine(Path.GetTempPath(),"vsp-steam-"+Guid.NewGuid());Directory.CreateDirectory(Path.Combine(root,"steamapps","common"));return root;}
void Manifest(string root,string id,string name,string folder,string flags="4"){Directory.CreateDirectory(Path.Combine(root,"steamapps","common",folder));File.WriteAllText(Path.Combine(root,"steamapps",$"appmanifest_{id}.acf"),$"\"AppState\" {{ \"appid\" \"{id}\" \"name\" \"{name}\" \"installdir\" \"{folder}\" \"StateFlags\" \"{flags}\" }}");}
Test("Steam discovers all libraries and removes uninstalled games",()=>{var a=SteamFixture();var b=SteamFixture();try{Manifest(a,"730","CS2","CS2");Manifest(b,"570","Dota 2","Dota");string escaped=b.Replace("\\","\\\\");File.WriteAllText(Path.Combine(a,"steamapps","libraryfolders.vdf"),$"\"libraryfolders\" {{ \"0\" {{ \"path\" \"{escaped}\" }} }}");var scan=SteamLibrary.Scan([a,a]);Assert(scan.Games.Length==2&&scan.Errors.Length==0);File.Delete(Path.Combine(b,"steamapps","appmanifest_570.acf"));Assert(SteamLibrary.Scan([a]).Games.Single().AppId=="730");}finally{Directory.Delete(a,true);Directory.Delete(b,true);}});
Test("Steam excludes tools and incomplete downloads",()=>{var a=SteamFixture();try{Manifest(a,"1905180","OBS Studio","OBS");Manifest(a,"228980","Steamworks Common Redistributables","redist");Manifest(a,"42","Downloading","partial","2");Manifest(a,"730","CS2","CS2");Assert(SteamLibrary.Scan([a]).Games.Single().AppId=="730");}finally{Directory.Delete(a,true);}});
Test("Steam broken manifest keeps other libraries usable and reports error",()=>{var a=SteamFixture();try{Manifest(a,"730","CS2","CS2");File.WriteAllText(Path.Combine(a,"steamapps","appmanifest_42.acf"),"\"AppState\" {");var scan=SteamLibrary.Scan([a]);Assert(scan.Games.Length==1&&scan.Errors.Length==1);}finally{Directory.Delete(a,true);}});
Test("Steam rejects manifest directory traversal",()=>{var a=SteamFixture();try{File.WriteAllText(Path.Combine(a,"steamapps","appmanifest_42.acf"),"\"AppState\" { \"appid\" \"42\" \"name\" \"bad\" \"installdir\" \"../escape\" \"StateFlags\" \"4\" }");Assert(SteamLibrary.Scan([a]).Games.Length==0);}finally{Directory.Delete(a,true);}});
Test("Steam game matching observes directory boundaries and ignores case",()=>{string dir=Path.Combine(Path.GetTempPath(),"SteamGame");var game=new SteamGame("730","CS2",dir);Assert(SteamLibrary.Contains(game,Path.Combine(dir,"bin","CS2.EXE").ToUpperInvariant()));Assert(!SteamLibrary.Contains(game,Path.Combine(dir+"Extra","cs2.exe")));});
Test("Steam presets resolve deterministically with fallback",()=>{string dir=Path.Combine(Path.GetTempPath(),"SteamGames");SteamGame[] games=[new("730","CS2",Path.Combine(dir,"cs")),new("570","Dota",Path.Combine(dir,"dota"))];string cs=Path.Combine(games[0].Directory,"game.exe"),dota=Path.Combine(games[1].Directory,"game.exe");SteamPreset[] presets=[new("730","200")];Assert(SteamLibrary.Resolve([cs],games,presets,"120")=="200");Assert(SteamLibrary.Resolve([dota,cs],games,presets,"120")=="120");Assert(SteamLibrary.Resolve([],games,presets,"120")==null);});
Test("Steam bindings survive settings save and missing installation",()=>{var(path,_,_)=Setup();var prefs=new Preferences{SteamPresets=[new("730","120")]};AtomicJson.Write(path,prefs);var read=AtomicJson.Read<Preferences>(path)!;read.Validate();Assert(read.SteamPresets.Single()==new SteamPreset("730","120"));File.Delete(path);});
Test("Steam rejects duplicate and missing or work profile bindings",()=>{Throws(()=>new Preferences{SteamPresets=[new("730","120"),new("730","200")]}.Validate());Throws(()=>new Preferences{SteamPresets=[new("730","missing")]}.Validate());Throws(()=>new Preferences{SteamPresets=[new("730","work")]}.Validate());});
Test("Automatic Work returns to preferred 120 while manual Work stays",()=>{var a=new AutoMode();var p=new Preferences();a.Select(Profile.Defaults[4],true,Profile.Defaults[1]);Assert(a.Tick(true,true,true,"work",30,DateTimeOffset.UtcNow)=="120");a.Select(Profile.Defaults[4],false,Profile.Defaults[1]);Assert(a.Tick(true,true,true,"work",30,DateTimeOffset.UtcNow)==null);Assert(p.Selected=="120"&&p.GamingDefault()=="120");});
Test("Steam disconnected library retains games while healthy removal prunes",()=>{SteamGame[] old=[new("730","CS2",@"C:\CS2")];Assert(SteamLibrary.Merge(old,new([], ["Disk unavailable"])).Length==1);Assert(SteamLibrary.Merge(old,new([], [])).Length==0);});
Test("Steam new installation receives fallback and existing binding persists",()=>{var p=new Preferences{SteamPresets=[new("730","200")]};SteamGame[] catalog=[new("730","CS2",@"C:\CS2"),new("570","Dota",@"C:\Dota")];Assert(SteamLibrary.Resolve([@"C:\Dota\game.exe"],catalog,p.SteamPresets,p.GamingDefault())=="120");Assert(SteamLibrary.Resolve([@"C:\CS2\game.exe"],catalog,p.SteamPresets,p.GamingDefault())=="200");});
if(args.Length==2&&args[0]=="--steam-scan"){var scan=SteamLibrary.Scan([args[1]]);Console.WriteLine("STEAM_SCAN "+System.Text.Json.JsonSerializer.Serialize(scan));}


Test("legacy upgrade adds 400 once and preserves Work, theme and Steam",()=>{var p=new Preferences{Selected="work",LightTheme=true,Profiles=Profile.Defaults.Where(p=>p.Id!="400").ToList(),SteamPresets=[new("730","120")]};Assert(p.EnsureBuiltIns()==1);p.Validate();Assert(p.Profiles.Single(q=>q.Id=="400").Fps==400&&p.Selected=="work"&&p.LightTheme&&p.SteamPresets.Single().ProfileId=="120");Assert(p.EnsureBuiltIns()==0);var(path,_,_)=Setup();AtomicJson.Write(path,p);var restored=AtomicJson.Read<Preferences>(path)!;restored.Validate();Assert(restored.Profiles.Single(q=>q.Id=="400").Fps==400);File.Delete(path);});
Test("upgrade does not overwrite existing user profile with same ID",()=>{var p=new Preferences();p.Profiles.RemoveAll(p=>p.Id=="400");p.Profiles.Add(new("400","My settings",400,20,"Авто",90));Assert(p.EnsureBuiltIns()==0);Assert(p.Profiles.Single(q=>q.Id=="400").GpuPercent==90);});
Test("upgrade respects maximum profile count",()=>{var p=new Preferences{Profiles=Profile.Defaults.Where(q=>q.Id!="400").ToList()};while(p.Profiles.Count<50)p.Profiles.Add(new(Guid.NewGuid().ToString(),"User",120,50));Assert(p.EnsureBuiltIns()==0);p.Validate();});
Test("400 global FPS readback restores prior 144 cap exactly",()=>{var(path,_,api,engine)=FpsSetup();api.Value=new(true,144,0,false);Assert(engine.Apply([],400,true).Contains("DRS FPS записан"));Assert(api.Value.Value==400);engine.Restore();Assert(api.Value==new DrsValue(true,144,0,false)&&!File.Exists(path));});

Test("CSV quoted fields, BOM and target PID are respected",()=>{string header="\uFEFFApplication,ProcessID,SwapChainAddress,TimeInSeconds,msBetweenPresents\n";var text=header+string.Join("\n",Enumerable.Range(0,130).Select(i=>$"\"game, demo.exe\",17,0x1,{(i/120.0).ToString("R",System.Globalization.CultureInfo.InvariantCulture)},8.333333333"))+"\nother.exe,99,0x1,1,1";var rows=FrameAnalysis.Parse(text,17);Assert(rows.Length==130&&rows.All(r=>r.ProcessId==17));Assert(Math.Abs(FrameAnalysis.Summarize(rows).AverageFps-120)<.01);});
Test("CSV rejects unknown layouts and ignores partial or nonfinite rows",()=>{Throws(()=>FrameAnalysis.Parse("ProcessID,FPS\n1,120",1));var rows=FrameAnalysis.Parse("ProcessID,SwapChainAddress,TimeInSeconds,msBetweenPresents\n1,x,0,NaN\n1,x,1,-1\n1,x,2,8\n1,x,3\n1,x,Infinity,8",1);Assert(rows.Length==1);});
Test("Frame analysis uses dominant swap chain, not a menu overlay",()=>{var main=Enumerable.Range(0,121).Select(i=>new FrameSample(1,"main",i/60.0,1000.0/60));var overlay=Enumerable.Range(0,61).Select(i=>new FrameSample(1,"overlay",i/60.0,1));Assert(Math.Abs(FrameAnalysis.Summarize(main.Concat(overlay)).AverageFps-60)<.01);});
Test("Frame average is time weighted and 1 percent low reflects slow frames",()=>{var rows=Enumerable.Range(0,200).Select(i=>new FrameSample(1,"x",i*.02,i<198?10:100));var stats=FrameAnalysis.Summarize(rows);Assert(stats.OnePercentLow==10&&stats.AverageFps<100&&stats.P99Milliseconds==10);});
Test("Insufficient or zero-duration captures are rejected",()=>{Throws(()=>FrameAnalysis.Summarize([]));Throws(()=>FrameAnalysis.Summarize(Enumerable.Range(0,40).Select(i=>new FrameSample(1,"x",0,8))));});
PowerTrial Trial050(uint percent,double fps,double p99,double? watts)=>new(percent,percent*1.8,new(500,fps,1000/p99,p99,8),watts);
Test("Tuner keeps a measured reserve above the lowest stable step",()=>{var rows=new[]{Trial050(85,120,9,100),Trial050(80,120,9,93),Trial050(75,119,9.5,85),Trial050(70,118,10,78),Trial050(65,90,18,65)};var result=PowerTuner.Choose(rows,120);Assert(result.RecommendedPercent==75&&result.SavingsMeasured);});
Test("Tuner rejects an already struggling baseline",()=>{Assert(!PowerTuner.Choose([Trial050(85,85,25,100)],120).Valid);});
Test("Tuner rejects an unconfirmed FPS limit",()=>{Assert(!PowerTuner.Choose([Trial050(85,500,2,100)],120).Valid);});
Test("Tuner never promises savings with missing or insignificant watts",()=>{var missing=new[]{Trial050(85,120,9,null),Trial050(80,120,9,null),Trial050(75,120,9,null)};Assert(PowerTuner.Choose(missing,120).RecommendedPercent==null);var flat=new[]{Trial050(85,120,9,20),Trial050(80,120,9,19.8),Trial050(75,120,9,19)};Assert(PowerTuner.Choose(flat,120).RecommendedPercent==null);});
Test("Tuner stops considering steps after the first frametime failure",()=>{var rows=new[]{Trial050(85,120,9,100),Trial050(80,120,30,70),Trial050(75,120,9,60),Trial050(70,120,9,50)};Assert(PowerTuner.Choose(rows,120).RecommendedPercent==null);});
Test("Tuning steps never raise the budget or exceed profile limits",()=>{var steps=PowerTuner.Steps(55);Assert(steps[0]==55&&steps[^1]>=30&&steps.SequenceEqual(steps.OrderByDescending(x=>x)));Throws(()=>PowerTuner.Steps(101));});
Test("Live frame CSV optional fields and target filtering",()=>{
 var d=new FrameCsvDecoder("Application,ProcessID,SwapChainAddress,TimeInSeconds,msBetweenPresents,msInPresentAPI,msUntilRenderComplete,msUntilDisplayed,Dropped",42);
 var f=d.Parse("game,42,0x1,1.0,8.333,0.1,3,5,0");Assert(f is not null&&f.DisplayMs==5&&f.FrameMs==8.333);
 Assert(d.Parse("game,41,0x1,1,8,0,0,0,0") is null);Assert(d.Parse("game,42,0x1,1,NaN,0,0,0,0") is null);
 Assert(d.Parse("game,42,0x1,1,8,0,3,5,1")!.DisplayMs is null);
});
Test("Live frame statistics choose dominant chain and bound memory",()=>{
 var w=new LiveFrameWindow();for(int n=0;n<3600;n++){w.Add(new(42,"main",n/120.0,1000.0/120,0.1,2,4,false));if(n%10==0)w.Add(new(42,"other",n/120.0,100,0,0,null,null));}
 var f=w.Snapshot();Assert(f is not null&&Math.Abs(f.Fps-120)<.001&&Math.Abs(f.OnePercentLow-120)<.001);Assert(f!.PointOnePercentLow.HasValue&&f.RecentFrameTimes.Length<=600);
 for(int n=3600;n<50000;n++)w.Add(new(42,"main",n/1000.0,1,null,null,null,null));Assert(w.Count<=LiveFrameWindow.Capacity);
});
Test("Ping reports missing replies without inventing RTT",()=>{
 var w=new PingWindow();w.Add(0);w.Add(10);w.Add(null);var f=w.Snapshot()!;Assert(f.Latest is null&&f.Received==2&&Math.Abs(f.LossPercent-100.0/3)<.001&&f.Jitter==10);
 for(int n=0;n<100;n++)w.Add(20);Assert(w.Snapshot()!.Sent==60&&w.Snapshot()!.LossPercent==0);
});
Test("History preserves gaps and bounds retention",()=>{
 var h=new MetricHistory();for(int n=0;n<1000;n++)h.Add("x",n,n%2==0?n:null);var points=h.Read("x",999,300);Assert(points.Length<=301&&points.Any(p=>p.Value is null));
 Throws(()=>h.Add("bad",1,double.NaN));h.Add("x",1,999);Assert(h.Read("x",999,300).Last().Seconds==999);
});
Test("GPU energy integrates measured power with correct units",()=>{var e=new GpuEnergy();e.Add(0,100);e.Add(10,300);var v=e.Snapshot();Assert(Math.Abs(v.WattHours-2000.0/3600)<1e-10&&v.ObservedSeconds==10&&v.AverageWatts==200&&v.PeakWatts==300&&v.Samples==2);});
Test("GPU energy does not invent initial energy",()=>{var e=new GpuEnergy();e.Add(500,100);var v=e.Snapshot();Assert(v.WattHours==0&&v.ObservedSeconds==0&&v.AverageWatts is null&&v.PeakWatts==100);});
Test("GPU energy excludes long gaps and resumes adjacent samples",()=>{var e=new GpuEnergy();e.Add(0,100);e.Add(60,100);e.Add(65,100);var v=e.Snapshot();Assert(v.ObservedSeconds==5&&Math.Abs(v.WattHours-500.0/3600)<1e-10);});
Test("GPU energy missing sample breaks continuity",()=>{var e=new GpuEnergy();e.Add(0,100);e.Add(5,null);e.Add(10,100);Assert(e.Snapshot().WattHours==0);e.Add(15,100);Assert(e.Snapshot().ObservedSeconds==5);});
Test("GPU energy rejects nonfinite and impossible readings",()=>{var e=new GpuEnergy();e.Add(0,100);e.Add(5,double.NaN);e.Add(10,double.PositiveInfinity);e.Add(15,-1);e.Add(20,10001);e.Add(25,100);Assert(e.Snapshot().ObservedSeconds==0&&e.Snapshot().Samples==2);});
Test("GPU energy accepts genuine zero power",()=>{var e=new GpuEnergy();e.Add(0,0);e.Add(5,0);var v=e.Snapshot();Assert(v.ObservedSeconds==5&&v.WattHours==0&&v.AverageWatts==0&&v.PeakWatts==0);});
Test("GPU energy ignores backwards and duplicate time",()=>{var e=new GpuEnergy();e.Add(10,100);e.Add(5,999);e.Add(10,999);e.Add(15,100);Assert(e.Snapshot().Samples==2&&e.Snapshot().AverageWatts==100);Throws(()=>e.Add(double.NaN,100));Throws(()=>e.Add(-1,100));});
Test("GPU energy stays accurate over repeated sampling",()=>{var e=new GpuEnergy();for(int n=0;n<=720;n++)e.Add(n*5,100);var v=e.Snapshot();Assert(v.ObservedSeconds==3600&&Math.Abs(v.WattHours-100)<1e-9&&v.AverageWatts==100);});
Test("Competitive profile is 400 FPS with full performance policy",()=>{var p=Profile.Defaults.Single(p=>p.Id=="competitive");p.Validate();Assert(p.Fps==400&&p.Epp==0&&p.GpuPercent==100&&p.Boost);});
Test("Competitive upgrade preserves user choices and is idempotent",()=>{var p=new Preferences{Selected="work",LightTheme=true,Profiles=Profile.Defaults.Where(p=>p.Id!="competitive").ToList(),SteamPresets=[new("730","120")]};Assert(p.EnsureBuiltIns()==1);p.Validate();Assert(p.Selected=="work"&&p.LightTheme&&p.SteamPresets.Single().ProfileId=="120"&&p.EnsureBuiltIns()==0);});
Test("Competitive choice takes precedence over Steam 120 without rewriting bindings",()=>{var p=new Preferences{Selected="competitive",SteamPresets=[new("730","120")]};SteamGame[] games=[new("730","CS2",@"C:\CS2")];Assert(p.ResolveGamingProfile([@"C:\CS2\game.exe"],games)=="competitive");Assert(p.SteamPresets.Single().ProfileId=="120");p.Selected="120";Assert(p.ResolveGamingProfile([@"C:\CS2\game.exe"],games)=="120");});
Test("Competitive automation returns to Work then competitive on next game",()=>{var a=new AutoMode();var p=Profile.Defaults.Single(p=>p.Id=="competitive");var now=DateTimeOffset.UtcNow;a.Select(p,true,Profile.Defaults[1]);Assert(a.Tick(true,true,false,"competitive",30,now) is null);Assert(a.Tick(true,true,false,"competitive",30,now.AddSeconds(31))=="work");Assert(a.Tick(true,true,true,"work",30,now.AddSeconds(40))=="competitive");});
Test("Competitive GPU budget and 400 FPS both restore original values",()=>{var(_,_,api,fps)=FpsSetup();api.Value=new(true,144,0,false);fps.Apply([],400,true);Assert(api.Value.Value==400);fps.Restore();Assert(api.Value.Value==144);var path=Path.Combine(Path.GetTempPath(),"quiet-tests",Guid.NewGuid()+".json");var gpu=new FakeGpu();var engine=new GpuPowerRecovery(path,()=>gpu);engine.Apply(100,5);Assert(gpu.Value==185000);engine.Restore();Assert(gpu.Value==180000);});
Console.WriteLine($"TOTAL {passed} passed, {failed} failed");return failed==0?0:1;
sealed class Fake:IPower {
 public Guid Current=Guid.NewGuid();public Dictionary<Guid,Scheme>Schemes=[];public bool FailActivate,IgnoreActivate,FailDelete;
 public Guid Active()=>Current;
 public Scheme Clone(Guid source,uint epp){var s=new Scheme(Guid.NewGuid(),epp,epp,"ok");Schemes.Add(s.Id,s);return s;}
 public void Activate(Guid id){if(FailActivate){FailActivate=false;throw new IOException("fault injection");}if(!IgnoreActivate)Current=id;}
 public Scheme Read(Guid id)=>Schemes.TryGetValue(id,out var s)?s:new(id,0,0,"missing");
 public void Delete(Guid id){if(FailDelete)throw new IOException("delete fault");Schemes.Remove(id);}
}

sealed class FakeDrs:INvidiaDrs {
 public DrsValue Value=new(true,0,2,true);public bool NoProfile,FailSave;
 public void Dispose(){}public void Reload(){}
 public void Save(){if(FailSave){FailSave=false;throw new IOException("save fault");}}
 public string ProfileFor(string path)=>NoProfile?throw new NotSupportedException("no profile"):"game-profile";
 public DrsValue Read(string name)=>Value;
 public void Write(string name,uint fps)=>Value=new(true,fps,0,false);
 public void Revert(string name,DrsValue before)=>Value=before;
}
sealed class FakeGpu:IGpuPower {
 public uint Value=180000,Maximum=190000;public bool Reject,NoDevice;
 public GpuState[] Devices()=>NoDevice?[]:[new("test-gpu",Value,100000,Maximum)];
 public uint Read(string id)=>Value;
 public void Set(string id,uint value){if(Reject)throw new NotSupportedException("denied");Value=value;}
 public void Dispose(){}
}

sealed class FakePriority:IProcessPriority {
 public Dictionary<int,ProcessState> States=[];public bool Reject;
 public ProcessState? Read(int id)=>States.GetValueOrDefault(id);
 public void Set(ProcessState expected,int value){if(Reject)throw new IOException("denied");if(States[expected.Id]!=expected)throw new IOException("identity");States[expected.Id]=expected with{Priority=value};}
}


