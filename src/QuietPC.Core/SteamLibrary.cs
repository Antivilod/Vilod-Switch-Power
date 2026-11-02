using System.Text;
namespace QuietPC.Core;

// Steam's local text manifests only. No account access, network calls or EXE launches.
public sealed record SteamGame(string AppId,string Name,string Directory);
public sealed record SteamScan(SteamGame[] Games,string[] Errors);
public sealed record SteamPreset(string AppId,string ProfileId);
public static class SteamLibrary {
 public static Dictionary<string,object> Parse(string text) {
  int i=0;
  string? Token(){
   while(i<text.Length){if(char.IsWhiteSpace(text[i])||text[i]=='\uFEFF'){i++;continue;}if(text[i]=='/'&&i+1<text.Length&&text[i+1]=='/'){while(i<text.Length&&text[i]!='\n')i++;continue;}break;}
   if(i==text.Length)return null;char c=text[i++];if(c is '{' or '}')return c.ToString();
   if(c!='"')throw new FormatException("Ожидалась строка Steam VDF");var s=new StringBuilder();bool closed=false;
   while(i<text.Length){c=text[i++];if(c=='"'){closed=true;break;}if(c=='\\'&&i<text.Length&&text[i] is '\\' or '"')c=text[i++];s.Append(c);}
   if(!closed)throw new FormatException("Незавершённая строка Steam VDF");return s.ToString();
  }
  Dictionary<string,object> Read(int depth,bool nested){if(depth>32)throw new FormatException("Слишком сложный VDF");var result=new Dictionary<string,object>(StringComparer.OrdinalIgnoreCase);while(true){var key=Token();if(key is null){if(nested)throw new FormatException("Незавершённый VDF");return result;}if(key=="}"){if(!nested)throw new FormatException("Лишняя скобка VDF");return result;}if(key=="{")throw new FormatException("Нет ключа VDF");var value=Token()??throw new FormatException("Нет значения VDF");if(value=="}")throw new FormatException("Нет значения VDF");result[key]=value=="{"?Read(depth+1,true):value;}}
  return Read(0,false);
 }
 static Dictionary<string,object> Section(Dictionary<string,object> data,string key)=>data.TryGetValue(key,out var value)&&value is Dictionary<string,object> section?section:new(StringComparer.OrdinalIgnoreCase);
 static string Value(Dictionary<string,object> data,string key)=>data.GetValueOrDefault(key) as string??"";
 static string ReadFile(string path){if(new FileInfo(path).Length>4_000_000)throw new IOException("Слишком большой манифест");using var f=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);using var r=new StreamReader(f,Encoding.UTF8,true);return r.ReadToEnd();}
 public static SteamScan Scan(IEnumerable<string> roots){
  var libraries=new HashSet<string>(StringComparer.OrdinalIgnoreCase);var games=new Dictionary<string,SteamGame>();var errors=new List<string>();
  foreach(var root in roots.Where(Path.IsPathFullyQualified).Distinct(StringComparer.OrdinalIgnoreCase)){
   libraries.Add(Path.GetFullPath(root));string config=Path.Combine(root,"steamapps","libraryfolders.vdf");if(!File.Exists(config))continue;
   try{foreach(var pair in Section(Parse(ReadFile(config)),"libraryfolders")){if(!uint.TryParse(pair.Key,out _))continue;var path=pair.Value is string legacy?legacy:pair.Value is Dictionary<string,object> entry?Value(entry,"path"):"";if(Path.IsPathFullyQualified(path))libraries.Add(Path.GetFullPath(path));}}catch(Exception e)when(e is IOException or UnauthorizedAccessException or FormatException or ArgumentException){errors.Add(config+": "+e.Message);}
  }
  foreach(var library in libraries){var manifests=Path.Combine(library,"steamapps");if(!Directory.Exists(manifests)){errors.Add(library+": библиотека недоступна");continue;}
   try{foreach(var file in Directory.EnumerateFiles(manifests,"appmanifest_*.acf",SearchOption.TopDirectoryOnly)){
    try{var a=Section(Parse(ReadFile(file)),"AppState");string id=Value(a,"appid"),name=Value(a,"name"),folder=Value(a,"installdir");
     if(!uint.TryParse(id,out uint number)||number==0||string.IsNullOrWhiteSpace(name)||name.Length>300||string.IsNullOrWhiteSpace(folder)||Path.IsPathRooted(folder)||folder.Split('\\','/').Any(p=>p is ".." or "."))continue;
     if(!uint.TryParse(Value(a,"StateFlags"),out uint flags)||(flags&4)==0)continue;
     // Known Steam tools must not activate a gaming profile.
     if(id is "228980" or "250820" or "1905180" or "431960" or "365670"||name.Contains("Redistributable",StringComparison.OrdinalIgnoreCase)||name.Contains("Dedicated Server",StringComparison.OrdinalIgnoreCase))continue;
     string common=Path.GetFullPath(Path.Combine(manifests,"common"))+Path.DirectorySeparatorChar;string directory=Path.GetFullPath(Path.Combine(common,folder));if(!directory.StartsWith(common,StringComparison.OrdinalIgnoreCase)||!Directory.Exists(directory))continue;
     games.TryAdd(id,new(id,name,directory));
    }catch(Exception e)when(e is IOException or UnauthorizedAccessException or FormatException or ArgumentException){errors.Add(file+": "+e.Message);}
   }}catch(Exception e)when(e is IOException or UnauthorizedAccessException){errors.Add(manifests+": "+e.Message);}
  }
  return new(games.Values.OrderBy(g=>g.Name,StringComparer.CurrentCultureIgnoreCase).ToArray(),errors.ToArray());
 }
 public static bool Contains(SteamGame game,string executable)=>executable.StartsWith(game.Directory.TrimEnd('\\','/')+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase);
 public static SteamGame[] Merge(IReadOnlyList<SteamGame> previous,SteamScan scan)=>scan.Errors.Length==0?scan.Games:scan.Games.Concat(previous.Where(g=>!scan.Games.Any(n=>n.AppId==g.AppId))).OrderBy(g=>g.Name).ToArray();
 public static string? Resolve(IEnumerable<string> running,IReadOnlyList<SteamGame> games,IReadOnlyList<SteamPreset> presets,string fallback){
  var paths=running.ToArray();return games.Where(g=>paths.Any(p=>Contains(g,p))).OrderBy(g=>uint.Parse(g.AppId)).Select(g=>presets.FirstOrDefault(p=>p.AppId==g.AppId)?.ProfileId??fallback).FirstOrDefault();
 }
}
