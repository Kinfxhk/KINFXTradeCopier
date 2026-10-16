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
// run with: <output folder> <language code>. Writes tour-monitor-<lang>.png (13 markers), tour-settings-<lang>.png (8 markers),
// tour-logs-<lang>.png and tour-issues-<lang>.png (no markers).
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
 static FrameworkElement[] ByTagPrefix(DependencyObject root,string prefix){return Tree(root).OfType<FrameworkElement>().Where(e=>Shown(e)&&e.ActualWidth>0&&(e.Tag as string??"").StartsWith(prefix,StringComparison.Ordinal)).ToArray();}
 static T Up<T>(DependencyObject d) where T:DependencyObject{for(d=d==null?null:VisualTreeHelper.GetParent(d);d!=null;d=VisualTreeHelper.GetParent(d)){var t=d as T;if(t!=null)return t;}return null;}
 static FrameworkElement Header(DataGrid g,string key){return Tree(g).OfType<DataGridColumnHeader>().FirstOrDefault(h=>h.Column!=null&&Shown(h)&&h.ActualWidth>0&&DashboardView.ColumnKey(h.Column)==key);}
 static Rect Col(FrameworkElement host,DataGrid g,params string[] keys){
  var r=Rect.Empty;foreach(var k in keys){var x=Box(host,Header(g,k));if(x.IsEmpty)throw new InvalidOperationException("column "+k+" not shown");r.Union(x);}
  double bottom=r.Bottom;foreach(var row in Tree(g).OfType<DataGridRow>())if(Shown(row)&&row.ActualHeight>0)bottom=Math.Max(bottom,Box(host,row).Bottom);
  return new Rect(r.X,r.Y,r.Width,bottom-r.Y);
 }
 static Rect Box(FrameworkElement host,FrameworkElement e){if(e==null||!Shown(e)||e.ActualWidth<=0)return Rect.Empty;return e.TransformToAncestor(host).TransformBounds(new Rect(0,0,e.ActualWidth,e.ActualHeight));}
 static Rect Union(FrameworkElement host,params FrameworkElement[] es){var r=Rect.Empty;foreach(var e in es){var x=Box(host,e);if(!x.IsEmpty)r.Union(x);}return r;}
 // One marker: outline around the target, numbered badge on its top-left corner (or above it, for the footer).
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
 static void Shot(Grid host,Canvas layer,DashboardView view,int tab,double w,double h,string path,Func<List<Rect>> marks,params int[] above){
  layer.Children.Clear();view.Sections.SelectedIndex=tab;Layout(host,w,h);
  foreach(var sv in Tree(view).OfType<ScrollViewer>().Where(x=>Shown(x)&&x.ActualWidth>0&&x.TemplatedParent is DataGrid))
   if(sv.ExtentWidth>sv.ViewportWidth+0.5)Console.WriteLine("HSCROLL tab "+tab+" extent="+sv.ExtentWidth+" viewport="+sv.ViewportWidth);
  // Measured clipping check: re-measure each single-line TextBlock with unlimited room (WPF's own text pipeline, same fonts and
  // fallback as the render); a TextBlock arranged narrower than that, or poking past its parent cell/button, is cut off.
  var clips=new List<string>();
  foreach(var tb in Tree(view).OfType<TextBlock>().Where(x=>Shown(x)&&x.ActualWidth>0&&x.TextWrapping==TextWrapping.NoWrap&&!string.IsNullOrEmpty(x.Text)).ToArray()){
   double have=tb.ActualWidth;tb.Measure(new Size(double.PositiveInfinity,double.PositiveInfinity));double need=tb.DesiredSize.Width-tb.Margin.Left-tb.Margin.Right-tb.Padding.Left-tb.Padding.Right; // DesiredSize includes margin
   bool cut=need>have+1;
   var owner=Up<Control>(tb);
   if(!cut&&owner!=null&&!(owner is ScrollViewer)){var ob=Box(view,tb);var cb=Box(view,owner);if(!ob.IsEmpty&&!cb.IsEmpty&&ob.Right>cb.Right+1)cut=true;}
   if(cut)clips.Add("CLIP tab "+tab+" ["+tb.Text+"] need="+Math.Ceiling(need)+" have="+Math.Floor(have)+" in "+(owner==null?"-":owner.GetType().Name));
  }
  Layout(host,w,h);
  foreach(var c in clips.Distinct())Console.WriteLine(c);
  var list=marks==null?new List<Rect>():marks();
  for(int i=0;i<list.Count;i++)Marker(layer,list[i],i+1,w,h,above.Contains(i+1));
  Layout(host,w,h);Save(host,w,h,path);
 }
 [STAThread] public static int Main(string[] args){
  try{
   string outDir=args[0], lang=args.Length>1?args[1]:"zh";
   Lang.Set(lang);
   const double W=1280,H=760;
   var view=new DashboardView(true);
   view.ApplyColumns(ColumnPrefs.Defaults()); // default columns: account name hidden, alias shown
   // Synthetic rows: a Simulator leader and ten simulated evaluation followers. All names and numbers are invented.
   var firms=new[]{"Firm A","Firm B"};
   view.Rows.Clear();
   var names=new List<string>();
   for(int i=0;i<11;i++){
    string firm=i==0?"":firms[(i-1)%2];
    var r=new AccountRow{Name=i==0?"Sim101":"DEMO-EVAL-"+(5101+i),Role=i==0?"Leader":"Follower",Connection="Synthetic",Currency="UsDollar",Status="Active",
     Settings=new RiskSettings{Alias=i==0?"Sim101":firm+" "+i.ToString("00"),Group=firm,Ratio=i==4?2:1,Target=i==6?0:1500,Loss=i==6?0:800,MaxContracts=4,CopyAtm=i!=8,Preset="Custom",AutoRefollow=i==2||i==3}};
    view.Rows.Add(r);names.Add(r.Name);
    if(i>0)r.Enabled=true;
    r.Connection="Connected";r.Position=i==0?1:i==4?2:i==9?0:1;
    r.Pnl=i==0?212.5:i==9?0:i==7?-96.0:(i==4?425.0:212.5-(i%3)*5);
    r.CashBalance=50000+i*731.25;
    if(i==9){r.Latch.Locked=true;r.Latch.Reason="Follower order rejected — review protection";}
    if(i==10){r.Paused=true;}
    r.Changed();
   }
   view.LeaderBox.ItemsSource=new[]{"Sim101"};view.LeaderBox.SelectedIndex=0;view.InstrumentBox.Text="MNQ 12-26";
   view.StatusText.Text=DashboardSafety.HeaderShown;
   var t0=new DateTime(2026,10,16,9,31,0);int sec=0;
   foreach(var m in new[]{"Armed explicitly: 10 simulation follower(s).","COPY DEMO-EVAL-5102 Buy 1 Market","COPY DEMO-EVAL-5103 Buy 1 Market","COPY DEMO-EVAL-5105 Buy 2 Market",
     "DEMO-EVAL-5110 LOCK: Follower order rejected — review protection",
     "Paused NEW entries. Working entries may still fill; new exits and changes/cancels remain mirrored. Flatten explicitly to remove exposure.",
     "Resumed NEW entries. Leader entries seen while paused are NOT copied; only later new entries are. Locked or unticked followers stay as they are.",
     "Auto re-follow: DEMO-EVAL-5102 — only later new entries are copied."}){
    var l=LogClassifier.Classify(m,names);l.Time=t0.AddSeconds(sec+=7);view.AddLog(l);
   }
   var layer=new Canvas{IsHitTestVisible=false};
   var host=new Grid{Background=DashboardView.Canvas};host.Children.Add(view);host.Children.Add(layer);

   // ---- 1) Monitor while copying
   view.SetSessionState(true,"Armed explicitly: 10 simulation follower(s).");
   foreach(var r in view.Rows){r.Status=r.Paused?"Paused":"Active";r.Changed();}
   view.StartButton.IsEnabled=true;view.ShowRefollow(1);view.UpdateCards();
   var g=view.AccountGrid;
   Shot(host,layer,view,0,W,H,System.IO.Path.Combine(outDir,"tour-monitor-"+lang+".png"),()=>new List<Rect>{
    Union(host,view.LeaderBox,view.InstrumentBox,view.InstPickButton),                                    // 1 leader + contract + picker
    Union(host,ByTag(view,"stateBadge"),ByTag(view,"dryBadge")),                                           // 2 state + dry run badge
    Union(host,view.StartButton,ByTag(view,"pause"),ByTag(view,"stop"),ByTag(view,"refresh")),             // 3 start / pause / stop / refresh
    Box(host,view.LanguageBox),                                                                            // 4 language
    Union(host,ByTagPrefix(view,"card:")),                                                                 // 5 summary cards
    Box(host,Up<WrapPanel>(ByTag(view,"tickAll"))),                                                       // 6 search / filter / ticks / group / columns
    Col(host,g,"On"),                                                                                      // 7 follow tick
    Col(host,g,"State","Conn"),                                                                            // 8 state + connection
    Col(host,g,"Qty","P/L","Bal"),                                                                         // 9 position / P/L / balance
    Col(host,g,"Ratio","Tgt","DLL","ATM"),                                                                 // 10 per-row settings
    Col(host,g,"Off","Flat"),                                                                              // 11 off / flat
    Union(host,ByTag(view,"refollow"),ByTag(view,"flattenSelected"),ByTag(view,"flattenAll")),             // 12 re-follow / flatten
    Box(host,view.SessionText),                                                                            // 13 footer
   },2,13); // badges 2 and 13 sit above their target so they never cover the first letters

   // ---- 2) Settings while stopped (settings are edited only when stopped)
   view.SetSessionState(false,"Disarmed");
   var rg=view.RiskGrid;
   Shot(host,layer,view,1,W,H+140,System.IO.Path.Combine(outDir,"tour-settings-"+lang+".png"),()=>new List<Rect>{
    Union(host,ByTag(view,"batch"),ByTag(view,"save"),ByTag(view,"daySettings"),ByTag(view,"pauseSelected"),ByTag(view,"resumeSelected")), // 1 session tools
    Union(host,view.AskStartBox,view.AskFlatOneBox,view.AskFlattenBox,view.DryRunBox),                     // 2 ask before + dry run
    Box(host,ByTag(view,"notifySection")),                                                                 // 3 email alerts
    Box(host,Up<Border>(ByTag(view,"bulkbar"))),                                                          // 4 bulk edit
    Col(host,rg,"Max","Ratio"),                                                                            // 5 max / ratio
    Col(host,rg,"Tgt","DLL","FlatLim"),                                                                    // 6 limits + flat at limit
    Col(host,rg,"AutoRefollow"),                                                                           // 7 auto re-follow
    Col(host,rg,"To Tgt","Loss left"),                                                                     // 8 room left
   });

   // ---- 3) Logs and 4) Issues (no markers)
   Shot(host,layer,view,2,W,H,System.IO.Path.Combine(outDir,"tour-logs-"+lang+".png"),null);
   Shot(host,layer,view,3,W,H,System.IO.Path.Combine(outDir,"tour-issues-"+lang+".png"),null);
   return 0;
  }catch(Exception ex){Console.WriteLine("FAIL "+ex);return 1;}
 }
}
