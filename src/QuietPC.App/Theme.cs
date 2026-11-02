using System.Windows.Media;
namespace QuietPC.App;
static class Theme {
 static bool lightTheme;
 public static void ConfigureWindow(System.Windows.Window window){window.SourceInitialized+=(_,_)=>Chrome(window);window.Loaded+=(_,_)=>Clamp(window);window.DpiChanged+=(_,_)=>window.Dispatcher.BeginInvoke(()=>Clamp(window));}
 static void Clamp(System.Windows.Window window){
  try{var handle=new System.Windows.Interop.WindowInteropHelper(window).Handle;if(handle==0)return;
   var area=System.Windows.Forms.Screen.FromHandle(handle).WorkingArea;var dpi=System.Windows.Media.VisualTreeHelper.GetDpi(window);double width=(area.Width-32)/dpi.DpiScaleX,height=(area.Height-32)/dpi.DpiScaleY;
   window.MinWidth=Math.Min(window.MinWidth,width);window.MinHeight=Math.Min(window.MinHeight,height);if(window.Width>width)window.Width=width;if(window.Height>height)window.Height=height;window.UpdateLayout();
   if(GetWindowRect(handle,out var rect)){int x=Math.Clamp(rect.Left,area.Left+16,Math.Max(area.Left+16,area.Right-(rect.Right-rect.Left)-16)),y=Math.Clamp(rect.Top,area.Top+16,Math.Max(area.Top+16,area.Bottom-(rect.Bottom-rect.Top)-16));if(x!=rect.Left||y!=rect.Top)SetWindowPos(handle,0,x,y,0,0,0x15);}
  }catch{}
 }
 [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]struct WindowRect{public int Left,Top,Right,Bottom;}
 [System.Runtime.InteropServices.DllImport("user32.dll")]static extern bool GetWindowRect(nint window,out WindowRect rect);
 [System.Runtime.InteropServices.DllImport("user32.dll")]static extern bool SetWindowPos(nint window,nint after,int x,int y,int width,int height,uint flags);
 static void Chrome(System.Windows.Window window){
  var handle=new System.Windows.Interop.WindowInteropHelper(window).Handle;if(handle==0)return;
  try{int dark=lightTheme?0:1,round=2;DwmSetWindowAttribute(handle,20,ref dark,4);DwmSetWindowAttribute(handle,33,ref round,4);}catch{}
 }
 [System.Runtime.InteropServices.DllImport("dwmapi.dll")]static extern int DwmSetWindowAttribute(nint window,int attribute,ref int value,int size);
 public static Brush Brush(string key)=>(Brush)System.Windows.Application.Current.Resources[key];
 public static void Apply(bool light){
  lightTheme=light;
  string[] keys=["AppBg","PanelBg","TextPrimary","TextSecondary","Edge","Hover","Pressed","Accent","CircleBg","RingOff"];
  string[] dark=["#111A19","#192523","#E8F0ED","#A0B2AD","#344B45","#263E37","#34564B","#72CABB","#1E302B","#4C6960"];
  string[] white=["#F7F7F3","#FFFFFF","#232B2A","#6E7777","#E2E5E3","#E5EFEC","#D5E5DF","#327D73","#F7FAF8","#C7D7D0"];
  var resources=System.Windows.Application.Current.Resources;
  SolidColorBrush Solid(string value){var brush=new SolidColorBrush((Color)ColorConverter.ConvertFromString(value));brush.Freeze();return brush;}
  var colors=light?white:dark;for(int i=0;i<keys.Length;i++)resources[keys[i]]=Solid(colors[i]);
  resources["ActiveFill"]=Solid(light?"#327D73":"#72CABB");
  resources["ActiveHover"]=Solid(light?"#28685F":"#8CD8CB");
  resources["ActivePressed"]=Solid(light?"#20594F":"#59B5A4");
  resources["ActiveText"]=Solid(light?"#FFFFFF":"#102B24");resources["ActiveIcon"]=resources["ActiveText"];
  resources["RingOn"]=resources["ActiveFill"];resources["LogoFrame"]=resources["Edge"];
  resources["MemoryWarning"]=Solid(light?"#8F651C":"#E2BE73");resources["MemoryCritical"]=Solid(light?"#B54B4B":"#ED9994");
  resources["BrandSurface"]=resources["AppBg"];
  foreach(System.Windows.Window window in System.Windows.Application.Current.Windows)Chrome(window);
 }
}
