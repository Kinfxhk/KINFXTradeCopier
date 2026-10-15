using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NinjaTrader.NinjaScript.AddOns.Kinfx;
// Interface tour for the README: renders the REAL DashboardView in synthetic DEMO mode (no NinjaTrader account, no engine,
// no orders; every name and number is made up) and draws numbered markers on the controls the README explains.
// Compile it together with src/AddOns/KINFXTradeCopier.cs (SIM TEST define) against the installed NinjaTrader 8 assemblies;
// run with: <output folder> <language code>. Writes tour-copy-<lang>.png and tour-risk-<lang>.png.
public static class Tour {
 static readonly Brush Mark=Frozen("#F2C46D"), MarkInk=Frozen("#17161F");
 static Brush Frozen(string c){var b=(SolidColorBrush)new BrushConverter().ConvertFromString(c);b.Freeze();return b;}
 static void Layout(FrameworkElement e,double w,double h){
  e.Measure(new Size(w,h));e.Arrange(new Rect(0,0,w,h));e.UpdateLayout();
  System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(System.Windows.Threading.DispatcherPriority.ContextIdle,new Action(()=>{}));
  e.UpdateLayout();
 }
 static IEnumerable<DependencyObject> Tree(DependencyObject root){
  var stack=new Stack<DependencyObject>();stack.Push(root);
  while(stack.Count>0){var d=stack.Pop();yield return d;int n=VisualTreeHelper.GetChildrenCount(d);for(int i=n-1;i>=0;i--)stack.Push(VisualTreeHelper.GetChild(d,i));}
 }
 static bool Shown(DependencyObject d){for(;d!=null;d=VisualTreeHelper.GetParent(d)){var u=d as UIElement;if(u!=null&&u.Visibility!=Visibility.Visible)return false;}return true;}
 static FrameworkElement ByTag(DependencyObject root,string tag){return Tree(root).OfType<FrameworkElement>().FirstOrDefault(e=>Shown(e)&&e.ActualWidth>0&&(e.Tag as string)==tag);}
 static FrameworkElement Header(DataGrid g,string key){return Tree(g).OfType<DataGridColumnHeader>().FirstOrDefault(h=>h.Column!=null&&Shown(h)&&h.ActualWidth>0&&DashboardView.ColumnKey(h.Column)==key);}
 static Rect Col(FrameworkElement host,DataGrid g,params string[] keys){
  var r=Rect.Empty;foreach(var k in keys){var x=Box(host,Header(g,k));if(!x.IsEmpty)r.Union(x);}
  if(r.IsEmpty)return r;
  double bottom=r.Bottom;foreach(var row in Tree(g).OfType<DataGridRow>())if(Shown(row)&&row.ActualHeight>0)bottom=Math.Max(bottom,Box(host,row).Bottom);
  return new Rect(r.X,r.Y,r.Width,bottom-r.Y);
 }
 static FrameworkElement Cell(DataGrid g,object item,string tag){var row=g.ItemContainerGenerator.ContainerFromItem(item) as DataGridRow;return row==null?null:ByTag(row,tag);}
 static Rect Box(FrameworkElement host,FrameworkElement e){if(e==null||!Shown(e)||e.ActualWidth<=0)return Rect.Empty;return e.TransformToAncestor(host).TransformBounds(new Rect(0,0,e.ActualWidth,e.ActualHeight));}
 static Rect Union(FrameworkElement host,params FrameworkElement[] es){var r=Rect.Empty;foreach(var e in es){var x=Box(host,e);if(!x.IsEmpty)r.Union(x);}return r;}
 // One marker: outline around the target, numbered badge on its top-left corner.
 static void Marker(Canvas layer,Rect r,int n,double w,double h,bool above=false){
  if(r.IsEmpty)throw new InvalidOperationException("marker "+n+" has no target");
  r.Inflate(1,1);
  var outline=new Border{Width=r.Width,Height=r.Height,BorderBrush=Mark,BorderThickness=new Thickness(2),CornerRadius=new CornerRadius(5)};
  Canvas.SetLeft(outline,r.X);Canvas.SetTop(outline,r.Y);layer.Children.Add(outline);
  var badge=new Border{Width=22,Height=22,CornerRadius=new CornerRadius(11),Background=Mark,BorderBrush=MarkInk,BorderThickness=new Thickness(1.5),
   Child=new TextBlock{Text=n.ToString(),Foreground=MarkInk,FontWeight=FontWeights.Bold,FontSize=12,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center}};
  Canvas.SetLeft(badge,Math.Max(2,Math.Min(w-24,above?r.X:r.X-11)));Canvas.SetTop(badge,Math.Max(2,Math.Min(h-24,above?r.Y-24:r.Y-11)));layer.Children.Add(badge);
 }
 static void Save(FrameworkElement host,double w,double h,string path){
  const double scale=1.5;
  var bmp=new RenderTargetBitmap((int)(w*scale),(int)(h*scale),96*scale,96*scale,PixelFormats.Pbgra32);bmp.Render(host);
  var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bmp));
  using(var fs=System.IO.File.Create(path))png.Save(fs);
  Console.WriteLine("WROTE "+System.IO.Path.GetFileName(path));
 }
 [STAThread] public static int Main(string[] args){
  try{
   string outDir=args[0], lang=args.Length>1?args[1]:"zh";
   Lang.Set(lang);
   const double W=1440,H=640;
   var view=new DashboardView(true);
   // Default columns (account name hidden, alias shown); on the Risk tab Preset and Cons are hidden with the Columns button so the picture fits.
   var hidden=ColumnPrefs.Defaults();hidden.Add(ColumnPrefs.Risk+":Preset");hidden.Add(ColumnPrefs.Risk+":Cons");view.ApplyColumns(hidden);
   // Synthetic rows: a Simulator leader and ten simulated evaluation followers. All names and numbers are invented.
   var firms=new[]{"Firm A","Firm B"};
   view.Rows.Clear();
   for(int i=0;i<11;i++){
    string firm=i==0?"":firms[(i-1)%2];
    var r=new AccountRow{Name=i==0?"Sim101":"DEMO-EVAL-"+(5101+i),Role=i==0?"Leader":"Follower",Connection="Synthetic",Currency="UsDollar",Status="Active",
     Settings=new RiskSettings{Alias=i==0?"Sim101":firm+" "+i.ToString("00"),Group=firm,Ratio=i==4?2:1,Target=i==6?0:1500,Loss=i==6?0:800,MaxContracts=4,CopyAtm=i!=8,Preset="Custom"}};
    view.Rows.Add(r);
    if(i>0)r.Enabled=true;
    r.Connection="Connected";r.Position=i==0?1:i==4?2:i==9?0:1;
    r.Pnl=i==0?212.5:i==9?0:i==7?-96.0:(i==4?425.0:212.5-(i%3)*5);
    r.CashBalance=50000+i*731.25;
    if(i==9){r.Latch.Locked=true;r.Latch.Reason="Exit order still working on a FLAT account";}
    if(i==10){r.Paused=true;}
    r.Changed();
   }
   view.LeaderBox.ItemsSource=new[]{"Sim101"};view.LeaderBox.SelectedIndex=0;view.InstrumentBox.Text="MNQ 12-26";
   view.StatusText.Text=DashboardSafety.HeaderShown;
   view.LogBox.Text=string.Join(Environment.NewLine,new[]{
    "09:31:02  "+Lang.Show("Armed explicitly: 10 simulation follower(s)."),
    "09:31:40  COPY Firm A 01 Buy 1 Market","09:31:40  COPY Firm B 02 Buy 1 Market","09:31:40  COPY Firm A 03 Buy 1 Market"});
   var layer=new Canvas{IsHitTestVisible=false};
   var host=new Grid{Background=DashboardView.Canvas};host.Children.Add(view);host.Children.Add(layer);

   // ---- 1) Copy tab while copying
   view.SetSessionState(true,"Armed explicitly: 10 simulation follower(s).");
   foreach(var r in view.Rows){r.Status=r.Paused?"Paused":"Active";r.Changed();}
   view.StartButton.IsEnabled=true;
   view.Sections.SelectedIndex=0;Layout(host,W,H);
   var g=view.AccountGrid;var follower=view.Rows[1];
   var marks=new List<Rect>{
    Union(host,view.LeaderBox,view.InstrumentBox),                 // 1 leader + contract
    Union(host,view.StartButton,ByTag(view,"pause"),ByTag(view,"stop"),ByTag(view,"refresh")), // 2 start / pause / stop / refresh
    Box(host,view.LanguageBox),                                     // 3 language
    Box(host,ByTag(view,"bulkbar")),                                // 4 bulk edit
    Box(host,ByTag(view,"columns:copy")),                           // 5 columns
    Col(host,g,"On"),           // 6 follow tick
    Col(host,g,"Ratio"),                                  // 7 ratio
    Col(host,g,"Tgt","DLL"),                    // 8 daily target / loss
    Col(host,g,"ATM"),                                    // 9 ATM exit mirroring
    Col(host,g,"State"),                                  // 10 state
    Col(host,g,"Off","Flat"),                   // 11 off / flat
    Box(host,view.SessionText),                                     // 12 session / footer
   };
   // The last (footer) badge goes above its text so it never covers the first letters.
   for(int i=0;i<marks.Count;i++)Marker(layer,marks[i],i+1,W,H,i==marks.Count-1);
   Layout(host,W,H);Save(host,W,H,System.IO.Path.Combine(outDir,"tour-copy-"+lang+".png"));

   // ---- 2) Risk tab while stopped (settings are edited only when stopped)
   layer.Children.Clear();
   view.SetSessionState(false,"Disarmed");
   view.Sections.SelectedIndex=1;Layout(host,W,H);
   var rg=view.RiskGrid;
   var riskMarks=new List<Rect>{
    Union(host,ByTag(view,"batch"),ByTag(view,"save"),ByTag(view,"daySettings")),  // 1 batch / save / trading day
    Union(host,ByTag(view,"flattenSelected"),ByTag(view,"flattenAll")),             // 2 flatten selected / all
    Union(host,view.AskStartBox,view.AskFlatOneBox,view.AskFlattenBox),             // 3 ask before
    Box(host,view.DryRunBox),                                                       // 4 dry run
    Col(host,rg,"Tgt","DLL","FlatLim"),             // 5 limits + flat at limit
    Col(host,rg,"Max"),                                                   // 6 max contracts
    Col(host,rg,"To Tgt","Loss left"),                         // 7 room left
   };
   for(int i=0;i<riskMarks.Count;i++)Marker(layer,riskMarks[i],i+1,W,H);
   Layout(host,W,H);Save(host,W,H,System.IO.Path.Combine(outDir,"tour-risk-"+lang+".png"));
   return 0;
  }catch(Exception ex){Console.WriteLine("FAIL "+ex);return 1;}
 }
}
