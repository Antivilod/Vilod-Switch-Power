using System.Windows;
namespace QuietPC.App;
public partial class App:System.Windows.Application {
 Mutex? singleton;
 protected override async void OnStartup(StartupEventArgs e){
  if(e.Args.FirstOrDefault()=="--vsp-live-capture"){ShutdownMode=ShutdownMode.OnExplicitShutdown;await LiveFrameSession.Worker(e.Args);Shutdown();return;}
  if(e.Args.FirstOrDefault()=="--vsp-capture"){ShutdownMode=ShutdownMode.OnExplicitShutdown;await FrameCapture.Worker(e.Args);Shutdown();return;}
  singleton=new Mutex(true,"Local\\QuietPC.UI.v1",out bool first);if(!first){System.Windows.MessageBox.Show("VSP уже запущена. Закройте предыдущую копию перед запуском portable.","VSP");Shutdown();return;}
  System.Windows.Media.RenderOptions.ProcessRenderMode=System.Windows.Interop.RenderMode.SoftwareOnly;Theme.Apply(false);base.OnStartup(e);new MainWindow().Show();
 }
 protected override void OnExit(ExitEventArgs e){singleton?.Dispose();base.OnExit(e);}
}
