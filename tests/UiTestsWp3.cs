using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using NinjaTrader.NinjaScript.AddOns.Kinfx;

public static class UiTestsWp3 {
  public static Action[] Cases(){ return new Action[]{
    GridShowsAllAddedRows,
    LevelFilterShowsOnlyLock,
    AccountFilterShowsOnlyThatAccount,
    SearchFiltersText,
    ClearEmptiesGridAndLogBox,
    UnseenBadgeIncrementsAndResets,
    TwoThousandLineCap,
    ZhRendersTranslatedLevelNames,
    LevelCellsAreColouredByLevel,
    NoClippedToolbarInFourLanguages,
    ComboAndCheckBoxesStayDark,
    Timing500InZh, PlainLogIsBoundedAndOnlyBuiltWhenShown }; }

  static LogLine Line(string level, string text, string account="") {
    return new LogLine { Time=new DateTime(2026,10,16,12,0,0), Level=level, Account=account, Category="General", Text=text };
  }
  static void LayoutAt(FrameworkElement e, double w, double h) {
    e.Measure(new Size(w,h)); e.Arrange(new Rect(0,0,w,h)); e.UpdateLayout();
    System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(System.Windows.Threading.DispatcherPriority.ContextIdle,new Action(()=>{})); e.UpdateLayout();
  }

  static void GridShowsAllAddedRows() {
    var v=BulkUiTests.Fresh();
    for(int i=0;i<100;i++) v.AddLog(Line("Info","line "+i));
    BulkUiTests.Check(v.LogGrid.Items.Count==100,"100 AddLog -> 100 rows; got "+v.LogGrid.Items.Count);
    BulkUiTests.Check(v.LogGrid.Columns.Count==5,"5 columns (Time/Level/Account/Category/Message); got "+v.LogGrid.Columns.Count);
  }
  static void LevelFilterShowsOnlyLock() {
    var v=BulkUiTests.Fresh();
    v.AddLog(Line("Info","info line"));v.AddLog(Line("Warn","warn line"));v.AddLog(Line("Lock","lock line"));v.AddLog(Line("Lock","lock line 2"));v.AddLog(Line("Error","error line"));
    v.LogLevelBox.SelectedIndex=4; // Lock
    BulkUiTests.Layout(v);
    BulkUiTests.Check(v.LogGrid.Items.Count==2,"Lock filter shows only Lock rows; got "+v.LogGrid.Items.Count);
    foreach(LogLine l in v.LogGrid.Items) BulkUiTests.Check(l.Level=="Lock","only Lock rows remain: "+l.Level);
    v.LogLevelBox.SelectedIndex=0;
  }
  static void AccountFilterShowsOnlyThatAccount() {
    var v=BulkUiTests.Fresh();
    v.AddLog(Line("Info","COPY Demo 01 1 lots","Demo 01"));
    v.AddLog(Line("Info","COPY Demo 02 1 lots","Demo 02"));
    v.AddLog(Line("Info","generic message",""));
    v.Sections.SelectedIndex=2;BulkUiTests.Layout(v); // arriving at Logs refreshes the account list
    BulkUiTests.Check(v.LogAccountBox.Items.Count>=3,"account list refreshed: "+v.LogAccountBox.Items.Count);
    v.LogAccountBox.SelectedIndex=1; // raw name Demo 01 (display alias Eval 1)
    BulkUiTests.Layout(v);
    BulkUiTests.Check(v.LogGrid.Items.Count==1,"account filter shows only Demo 01 rows; got "+v.LogGrid.Items.Count);
    var l=(LogLine)v.LogGrid.Items.GetItemAt(0);
    BulkUiTests.Check(l.Account=="Demo 01","account is Demo 01: "+l.Account);
    v.LogAccountBox.SelectedIndex=0;
  }
  static void SearchFiltersText() {
    var v=BulkUiTests.Fresh();
    v.AddLog(Line("Info","alpha beta"));v.AddLog(Line("Info","gamma delta"));v.AddLog(Line("Warn","alpha warn"));
    v.LogSearchBox.Text="alpha";BulkUiTests.Layout(v);
    BulkUiTests.Check(v.LogGrid.Items.Count==2,"search 'alpha' shows 2 rows; got "+v.LogGrid.Items.Count);
    v.LogSearchBox.Text="";BulkUiTests.Layout(v);
    BulkUiTests.Check(v.LogGrid.Items.Count==3,"clear search shows 3 rows; got "+v.LogGrid.Items.Count);
  }
  static void ClearEmptiesGridAndLogBox() {
    var v=BulkUiTests.Fresh();
    v.AddLog(Line("Info","a"));v.AddLog(Line("Warn","b"));v.AddLog(Line("Lock","c"));
    BulkUiTests.Check(v.LogGrid.Items.Count==3,"3 rows before clear");
    v.ClearLog();
    BulkUiTests.Check(v.LogGrid.Items.Count==0,"grid empty after clear");
    BulkUiTests.Check(v.LogBox.Text=="","LogBox empty after clear");
    BulkUiTests.Check(v.LogUnseen==0,"unseen badge reset after clear");
  }
  static void UnseenBadgeIncrementsAndResets() {
    var v=BulkUiTests.Fresh();
    v.AddLog(Line("Info","i")); BulkUiTests.Check(v.LogUnseen==0,"Info does not increment");
    v.AddLog(Line("Order","o")); BulkUiTests.Check(v.LogUnseen==0,"Order does not increment");
    v.AddLog(Line("Warn","w")); BulkUiTests.Check(v.LogUnseen==1,"Warn increments");
    v.AddLog(Line("Lock","l")); BulkUiTests.Check(v.LogUnseen==2,"Lock increments");
    v.AddLog(Line("Error","e")); BulkUiTests.Check(v.LogUnseen==3,"Error increments");
    v.Sections.SelectedIndex=2;BulkUiTests.Layout(v);
    BulkUiTests.Check(v.LogUnseen==0,"badge resets when Logs tab selected");
    v.AddLog(Line("Warn","w2")); BulkUiTests.Check(v.LogUnseen==0,"no increment while viewing Logs");
    v.Sections.SelectedIndex=0;BulkUiTests.Layout(v);
    v.AddLog(Line("Error","e2")); BulkUiTests.Check(v.LogUnseen==1,"increments again after leaving Logs");
    string h=""+((TabItem)v.Sections.Items[2]).Header;
    BulkUiTests.Check(h==Lang.T("Logs")+" (1)","header shows badge: "+h);
  }
  static void TwoThousandLineCap() {
    var v=BulkUiTests.Fresh();
    for(int i=0;i<2050;i++) v.AddLog(Line(i%2==0?"Info":"Warn","line "+i));
    BulkUiTests.Check(v.LogGrid.Items.Count==2000,"cap at 2000 rows; got "+v.LogGrid.Items.Count);
  }
  static void ZhRendersTranslatedLevelNames() {
    string was=Lang.Current;
    try {
      Lang.Set("zh");
      var v=BulkUiTests.Fresh();
      v.AddLog(Line("Warn","a warning"));
      v.Sections.SelectedIndex=2;BulkUiTests.Layout(v);
      var item=(LogLine)v.LogGrid.Items.GetItemAt(0);
      v.LogGrid.ScrollIntoView(item);BulkUiTests.Layout(v);
      var cell=v.LogGrid.Columns[1].GetCellContent(item);
      var tb=cell as TextBlock ?? BulkUiTests.Visual<TextBlock>(cell).FirstOrDefault();
      BulkUiTests.Check(tb!=null,"level cell realized");
      BulkUiTests.Check(tb.Text==Lang.T("Warn"),"zh level name rendered: '"+tb.Text+"' expected '"+Lang.T("Warn")+"'");
      BulkUiTests.Check(Lang.T("Warn")!="Warn","Warn is translated in zh");
    } finally { Lang.Set(was); }
  }
  static void NoClippedToolbarInFourLanguages() {
    string was=Lang.Current;
    try {
      foreach(var code in Lang.Codes) {
        Lang.Set(code);
        var v=BulkUiTests.Fresh();
        v.Sections.SelectedIndex=2;LayoutAt(v,1160,840);
        int bad=0;string first="";
        Action<DependencyObject> walk=null;
        walk=d=>{var fe=d as FrameworkElement;if(fe!=null&&BulkUiTests.Truncated(fe)){bad++;if(first=="")first=((TextBlock)fe).Text;}int n=System.Windows.Media.VisualTreeHelper.GetChildrenCount(d);for(int i=0;i<n;i++)walk(System.Windows.Media.VisualTreeHelper.GetChild(d,i));};
        walk(v);
        BulkUiTests.Check(bad==0,code+": clipped toolbar text: '"+first+"'");
        foreach(var s in BulkUiTests.Texts(v)) BulkUiTests.Check(s.IndexOf('\uFFFD')<0,code+": replacement char in '"+s+"'");
      }
    } finally { Lang.Set(was); }
  }
  static void ComboAndCheckBoxesStayDark() {
    var v=BulkUiTests.Fresh();
    v.Sections.SelectedIndex=2;BulkUiTests.Layout(v);
    int c=BulkUiTests.WhitePx(v.LogLevelBox)+BulkUiTests.WhitePx(v.LogAccountBox);
    int cb=BulkUiTests.WhitePx(v.LogAutoBox)+BulkUiTests.WhitePx(v.LogPlainBox);
    BulkUiTests.Check(c<40,"combo near-white pixels: "+c);
    BulkUiTests.Check(cb<40,"check box near-white pixels: "+cb);
  }
  static void Timing500InZh() {
    string was=Lang.Current;
    try {
      Lang.Set("zh");
      var v=BulkUiTests.Fresh();
      var sw=System.Diagnostics.Stopwatch.StartNew();
      for(int i=0;i<500;i++) v.AddLog(Line(i%3==0?"Warn":"Info","message number "+i));
      sw.Stop();
      Console.WriteLine("WP3_TIMING_ZH_500_ADDLOG_MS="+sw.ElapsedMilliseconds);
      BulkUiTests.Check(v.LogGrid.Items.Count==500,"500 rows added; got "+v.LogGrid.Items.Count);
      BulkUiTests.Check(sw.ElapsedMilliseconds<1500,"500 AddLog in zh took "+sw.ElapsedMilliseconds+"ms (must be < 1500)");
    } finally { Lang.Set(was); }
  }

 // Review fix (parent): the level cell had a local Foreground, which outranks the style triggers, so every level rendered Muted.
 static void LevelCellsAreColouredByLevel(){
  var v=BulkUiTests.Fresh();
  foreach(var lv in new[]{"Info","Order","Warn","Lock","Error"})v.AddLog(new LogLine{Time=DateTime.Now,Level=lv,Account="",Category="General",Text="x "+lv});
  v.Sections.SelectedIndex=2;BulkUiTests.Layout(v);
  var col=v.LogGrid.Columns[1];
  var want=new System.Collections.Generic.Dictionary<string,System.Windows.Media.Brush>{{"Info",DashboardView.Muted},{"Order",DashboardView.Good},{"Warn",DashboardView.Warn},{"Lock",DashboardView.Bad},{"Error",DashboardView.Bad}};
  foreach(var item in v.LogGrid.Items.Cast<LogLine>()){
   v.LogGrid.ScrollIntoView(item);BulkUiTests.Layout(v);
   var cell=col.GetCellContent(item);var tb=cell as TextBlock??BulkUiTests.Visual<TextBlock>(cell).FirstOrDefault();
   BulkUiTests.Check(tb!=null,"level cell realized for "+item.Level);
   BulkUiTests.Check(ReferenceEquals(tb.Foreground,want[item.Level]),item.Level+" level text colour is "+tb.Foreground+", expected "+want[item.Level]);
  }
 }

  // Independent review: the plain-text copy grew without bound on the UI thread even when the grid view was shown.
  static void PlainLogIsBoundedAndOnlyBuiltWhenShown(){
    var v=BulkUiTests.Fresh();v.Sections.SelectedIndex=2;BulkUiTests.Layout(v);
    for(int i=0;i<2600;i++){var l=LogClassifier.Classify("line "+i,new string[0]);l.Time=DateTime.Now;v.AddLog(l);}
    BulkUiTests.Check(v.LogGrid.Items.Count==2000,"grid capped at 2000: "+v.LogGrid.Items.Count);
    BulkUiTests.Check(v.LogBox.Text.Length==0,"plain text not built while the grid view is shown: "+v.LogBox.Text.Length);
    v.LogPlainBox.IsChecked=true;BulkUiTests.Layout(v);
    BulkUiTests.Check(v.LogBox.LineCount==2000,"plain view shows the same 2000 lines: "+v.LogBox.LineCount);
    BulkUiTests.Check(v.LogBox.Text.Contains("line 2599")&&!v.LogBox.Text.Contains("line 599\r"),"newest kept, oldest dropped");
    for(int i=0;i<400;i++){var l=LogClassifier.Classify("more "+i,new string[0]);l.Time=DateTime.Now;v.AddLog(l);}
    BulkUiTests.Check(v.LogBox.LineCount<=2201,"plain view stays bounded while shown: "+v.LogBox.LineCount);
    BulkUiTests.Check(v.LogBox.Text.Contains("more 399"),"newest line appended in plain view");
    v.LogPlainBox.IsChecked=false;BulkUiTests.Layout(v);
    BulkUiTests.Check(v.LogBox.Text.Length==0,"plain text released when hidden");
  }
}
