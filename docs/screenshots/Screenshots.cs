using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NinjaTrader.NinjaScript.AddOns.Kinfx;
// Renders the real DashboardView in synthetic DEMO mode (no accounts, no engine, no orders) to PNG files.
public static class Screenshots {
 static void Layout(FrameworkElement e,double w,double h){e.Measure(new Size(w,h));e.Arrange(new Rect(0,0,w,h));e.UpdateLayout();System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(System.Windows.Threading.DispatcherPriority.ContextIdle,new Action(()=>{}));e.UpdateLayout();}
 [STAThread] public static int Main(string[] args) {
  try {
   AppDomain.CurrentDomain.AssemblyResolve+=(s,e)=>{string n=e.Name.Split(',')[0];if(n.EndsWith(".resources"))return null;var loaded=AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a=>a.GetName().Name==n);if(loaded!=null)return loaded;string p=System.IO.Path.Combine("C:/Program Files/NinjaTrader 8/bin",n+".dll");return System.IO.File.Exists(p)?System.Reflection.Assembly.LoadFrom(p):null;};
   string outDir=args[0];
   var view=new DashboardView(true);
   for(int i=0;i<view.Rows.Count;i++){view.Rows[i].Name="SIM-DEMO-"+(1001+i).ToString();view.Rows[i].Alias=i==0?"Leader":"Eval "+i.ToString("00");view.Rows[i].Changed();}
   // Two deliberately risky synthetic rows so the settings-note footer is visible in the picture.
   view.Rows[3].Target=0;view.Rows[3].Loss=0;view.Rows[3].Changed();
   view.Rows[5].Ratio=0.2;view.Rows[5].Changed();
   view.Rows[7].Latch.Locked=true;view.Rows[7].Latch.Reason="Unknown / opposite exposure — exit adoption blocked";view.Rows[7].Changed();
   view.LeaderBox.ItemsSource=new[]{view.Rows[0].Name};view.LeaderBox.SelectedIndex=0;view.UpdateCards();
   view.LogBox.Text=string.Join(Environment.NewLine,new[]{"10:02:11  Demo preview loaded - synthetic rows only","10:02:11  REVIEW HOLD - Start / Flatten disabled for every account","10:02:12  Per-account Ratio / Tgt / DLL / ATM are editable on this page","10:02:12  No NinjaTrader account is connected in this screenshot"});
   var host=new Border{Background=DashboardView.Canvas,Child=view};
   string[] names={"1-copy","2-risk","3-logs"};
   for(int t=0;t<3;t++){
    view.Sections.SelectedIndex=t;Layout(host,1160,720);
    var bmp=new RenderTargetBitmap(1160,720,96,96,PixelFormats.Pbgra32);bmp.Render(host);
    var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bmp));
    using(var fs=System.IO.File.Create(System.IO.Path.Combine(outDir,names[t]+".png")))png.Save(fs);
    Console.WriteLine("WROTE "+names[t]);
   }
   return 0;
  } catch(Exception ex){Console.WriteLine("FAIL "+ex);return 1;}
 }
}
