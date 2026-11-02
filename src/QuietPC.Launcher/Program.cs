using System.Diagnostics;
using System.Runtime.InteropServices;
static class Program {
 [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern int MessageBoxW(nint window,string text,string title,uint flags);
 static void Main(){try{string app=Path.Combine(AppContext.BaseDirectory,"app","QuietPC.exe");if(!File.Exists(app))throw new FileNotFoundException("Распакуйте весь архив VSP. Папки app, service и monitor должны находиться рядом с VSP.exe.");Process.Start(new ProcessStartInfo(app){UseShellExecute=true,WorkingDirectory=Path.GetDirectoryName(app)!});}catch(Exception ex){MessageBoxW(0,ex.Message,"VSP Portable",0x10);}}
}
