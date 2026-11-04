using System.Windows;
namespace QuietPC.App;
public partial class App:System.Windows.Application {
 Mutex? singleton;
 protected override async void OnStartup(StartupEventArgs e){
  if(e.Args.FirstOrDefault()=="--vsp-live-capture"){ShutdownMode=ShutdownMode.OnExplicitShutdown;await LiveFrameSession.Worker(e.Args);Shutdown();return;}
  if(e.Args.FirstOrDefault()=="--vsp-capture"){ShutdownMode=ShutdownMode.OnExplicitShutdown;await FrameCapture.Worker(e.Args);Shutdown();return;}
  singleton=new Mutex(true,"Local\\QuietPC.UI.v1",out bool first);if(!first){System.Windows.MessageBox.Show("VSP уже запущена. Закройте предыдущую копию перед запуском portable.","VSP");Shutdown();return;}
  System.Windows.Media.RenderOptions.ProcessRenderMode=System.Windows.Interop.RenderMode.SoftwareOnly;Theme.Apply(false);base.OnStartup(e);var window=new MainWindow();window.Show();
  if(e.Args.Length==2&&e.Args[0]=="--vsp-install-smoke"){
   var timer=new System.Windows.Threading.DispatcherTimer{Interval=TimeSpan.FromSeconds(5)};
   timer.Tick+=(_,_)=>{timer.Stop();bool ok=window.IsLoaded&&window.IsVisible&&
    (window.FindName("VersionLabel") as System.Windows.Controls.TextBlock)?.Text.Contains("0.8.1")==true&&
    (window.FindName("Profiles") as System.Windows.Controls.ComboBox)?.Items.Count>=4;
    try{System.IO.File.WriteAllText(e.Args[1],System.Text.Json.JsonSerializer.Serialize(new{ui_initialized=ok,version="0.8.1"}));Shutdown(ok?0:1);}catch{Shutdown(1);}};
   timer.Start();
  }
 }
 protected override void OnExit(ExitEventArgs e){singleton?.Dispose();base.OnExit(e);}
}
