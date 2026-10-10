using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using NinjaTrader.NinjaScript.AddOns.Kinfx;
// Synthetic DEMO only: no accounts, no engine, no orders. Exercises the bulk-edit bar on the real DashboardView.
public static class BulkUiTests {
 static void Layout(FrameworkElement e){e.Measure(new Size(1500,950));e.Arrange(new Rect(0,0,1500,950));e.UpdateLayout();System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(System.Windows.Threading.DispatcherPriority.ContextIdle,new Action(()=>{}));e.UpdateLayout();}
 static void Check(bool ok,string m){if(!ok)throw new Exception(m);}
 static DashboardView Fresh(){var v=new DashboardView(true);v.Editable(true);Layout(v);return v;}
 static void Select(DashboardView v,params int[] idx){v.AccountGrid.UnselectAll();foreach(int i in idx)v.AccountGrid.SelectedItems.Add(v.Rows[i]);}
 static void TargetAndLossApplyToSelectedOnly(){
  var v=Fresh();Select(v,1,2,3);
  string r=v.BulkApply("Tgt","750");Check(r==null,"valid Tgt must apply: "+r);
  Check(v.Rows[1].Target==750&&v.Rows[2].Target==750&&v.Rows[3].Target==750,"selected rows must get Tgt");
  Check(v.Rows[4].Target==1000,"unselected row must keep its own Tgt");
  r=v.BulkApply("DLL","300");Check(r==null,"valid DLL must apply: "+r);
  Check(v.Rows[1].Loss==300&&v.Rows[3].Loss==300&&v.Rows[4].Loss==500,"DLL scope");
 }
 static void ZeroTurnsLimitOff(){var v=Fresh();Select(v,1);Check(v.BulkApply("Tgt","0")==null&&v.Rows[1].Target==0,"0 must be accepted as OFF");}
 static void RatioStaysIndependent(){
  var v=Fresh();v.Rows[1].Ratio=2;v.Rows[2].Ratio=0.5;Select(v,1,2);
  Check(v.BulkApply("Tgt","900")==null,"apply");
  Check(v.Rows[1].Ratio==2&&v.Rows[2].Ratio==0.5,"bulk Tgt/DLL must never touch each row's own Ratio");
 }
 static void InvalidInputChangesNothing(){
  var v=Fresh();Select(v,1,2);
  foreach(string bad in new[]{"","abc","-5","NaN","1e400","1,000"}){
   string r=v.BulkApply("Tgt",bad);Check(r!=null,"invalid '"+bad+"' must be refused");
   Check(v.Rows[1].Target==1000&&v.Rows[2].Target==1000,"refused input must not change any row: '"+bad+"'");
  }
 }
 static void NoSelectionIsRefused(){var v=Fresh();v.AccountGrid.UnselectAll();Check(v.BulkApply("Tgt","5")!=null,"empty selection must be refused");Check(v.Rows.All(r=>r.Target==1000),"nothing may change");}
 static void LeaderIsNotEdited(){var v=Fresh();Select(v,0,1);Check(v.BulkApply("Tgt","10")==null,"apply");Check(v.Rows[0].Target==1000&&v.Rows[1].Target==10,"leader row is not a follower and must be skipped");}
 static void AtmToggleBulk(){
  var v=Fresh();Select(v,1,2);Check(v.BulkApply("ATM","on")==null,"ATM on");Check(v.Rows[1].CopyAtm&&v.Rows[2].CopyAtm,"ATM on selected");
  Check(v.BulkApply("ATM","off")==null,"ATM off");Check(!v.Rows[1].CopyAtm&&!v.Rows[2].CopyAtm,"ATM off selected");
  Check(v.BulkApply("ATM","maybe")!=null,"bad ATM value refused");
 }
 static void LockedWhenNotEditable(){var v=Fresh();v.Editable(false);Select(v,1);Check(v.BulkApply("Tgt","5")!=null,"attached/locked state must refuse bulk edits");Check(v.Rows[1].Target==1000,"unchanged");}
 static void NeverTouchesFollowOrTrading(){
  var v=Fresh();int follow=0;v.FollowRow=(r,on)=>follow++;bool enabledBefore=v.Rows[1].Enabled;Select(v,1,2);
  v.BulkApply("Tgt","5");v.BulkApply("DLL","5");v.BulkApply("ATM","on");
  Check(follow==0,"bulk settings must not call the follow/engine path");Check(v.Rows[1].Enabled==enabledBefore,"bulk settings must not change the tick");
  Check(!v.StartButton.IsEnabled,"Start must stay held");
 }
 static void BarIsOnCopyTabAndKeepsHolds(){
  var v=Fresh();Layout(v);
  Check(v.AccountGrid.Columns.Count==13,"13 Copy columns retained");
  Check(v.BulkBarPresent,"bulk bar must be on the Copy tab");
 }
 static void SetLeader(DashboardView v,double tgt,double dll,bool atm){var l=v.Rows[0];l.Target=tgt;l.Loss=dll;l.CopyAtm=atm;}
 static void FromLeaderCopiesToHighlighted(){
  var v=Fresh();Check(v.Rows[3].CopyAtm,"fixture default is ATM on");SetLeader(v,2000,800,false);Select(v,1,2);
  Check(v.BulkFromLeader()==null,"must apply");
  Check(v.Rows[1].Target==2000&&v.Rows[1].Loss==800&&!v.Rows[1].CopyAtm,"row1 gets leader Tgt/DLL/ATM(off)");
  Check(v.Rows[2].Target==2000&&v.Rows[2].Loss==800&&!v.Rows[2].CopyAtm,"row2 gets leader Tgt/DLL/ATM(off)");
  Check(v.Rows[3].Target==1000&&v.Rows[3].Loss==500&&v.Rows[3].CopyAtm,"unhighlighted row untouched (ATM stays on)");
 }
 static void FromLeaderWithoutHighlightHitsAllVisibleFollowers(){
  var v=Fresh();SetLeader(v,1500,600,true);v.AccountGrid.UnselectAll();
  Check(v.BulkFromLeader()==null,"must apply");
  Check(v.Rows.Skip(1).All(r=>r.Target==1500&&r.Loss==600&&r.CopyAtm),"all followers get leader values");
  Check(v.Rows[0].Target==1500,"leader keeps its own values");
 }
 static void FromLeaderRespectsSearchFilter(){
  var v=Fresh();SetLeader(v,1500,600,false);v.AccountGrid.UnselectAll();v.SearchText="Lucid";
  Check(v.BulkFromLeader()==null,"must apply");
  foreach(var r in v.Rows.Skip(1)){bool want=r.Group=="Lucid";Check((r.Target==1500)==want,"only visible (Lucid) rows change: "+r.Name);}
  v.SearchText="";
 }
 static void FromLeaderKeepsRatioTickAndOtherFields(){
  var v=Fresh();SetLeader(v,2000,800,true);v.Rows[1].Ratio=2.5;v.Rows[2].Ratio=0.5;bool tick=v.Rows[1].Enabled;int maxBefore=v.Rows[1].Settings.MaxContracts;string aliasBefore=v.Rows[1].Alias;Select(v,1,2);
  Check(v.BulkFromLeader()==null,"must apply");
  Check(v.Rows[1].Ratio==2.5&&v.Rows[2].Ratio==0.5,"each row keeps its own Ratio");
  Check(v.Rows[1].Enabled==tick,"tick unchanged");Check(v.Rows[1].Settings.MaxContracts==maxBefore&&v.Rows[1].Alias==aliasBefore,"other fields unchanged");
 }
 static void ManualEditAfterwardsSticksAndNoLiveLink(){
  var v=Fresh();SetLeader(v,2000,800,true);Select(v,1,2);Check(v.BulkFromLeader()==null,"apply");
  v.Rows[1].Target=1234;Check(v.Rows[1].Target==1234,"manual fine-tune on one row");
  Check(v.Rows[2].Target==2000,"sibling unaffected by that edit");
  v.Rows[0].Target=9999;Check(v.Rows[1].Target==1234&&v.Rows[2].Target==2000,"changing the leader later must not rewrite followers (one-shot copy, no live link)");
  Select(v,3);Check(v.BulkApply("DLL","111")==null,"normal bulk still works after");Check(v.Rows[1].Target==1234,"fine-tuned row still intact");
 }
 static void FromLeaderRefusedWhenLockedOrNoLeader(){
  var v=Fresh();SetLeader(v,2000,800,true);Select(v,1);v.Editable(false);
  Check(v.BulkFromLeader()!=null,"locked must refuse");Check(v.Rows[1].Target==1000,"unchanged when locked");
  var w=Fresh();foreach(var r in w.Rows)r.Role="Follower";w.AccountGrid.UnselectAll();
  Check(w.BulkFromLeader()!=null,"no leader must refuse");Check(w.Rows.All(r=>r.Target==1000),"unchanged without leader");
 }
 static void FromLeaderNeverTouchesFollowOrTrading(){
  var v=Fresh();int follow=0;v.FollowRow=(r,on)=>follow++;SetLeader(v,2000,800,true);Select(v,1,2);v.BulkFromLeader();
  Check(follow==0,"must not call follow/engine path");Check(!v.StartButton.IsEnabled,"Start stays held");
 }
 static void AdviceShownPerRowAndCounted(){
  var v=Fresh();
  Check(v.AdviceCount==0,"fixture rows all have Tgt/DLL so no warnings; count="+v.AdviceCount);
  v.Rows[1].Target=0;v.Rows[1].Loss=0;v.Rows[1].Changed();v.UpdateCards();
  Check(v.Rows[1].Advice.Contains("no daily"),"row shows advice: "+v.Rows[1].Advice);
  Check(v.AdviceCount==1,"count=1 after one risky row; got "+v.AdviceCount);
  Check(v.SelectionSummary.Length>0,"footer present");
  Check(v.AdviceText.Contains("1"),"footer advice text mentions the count: "+v.AdviceText);
  v.Rows[1].Target=1000;v.Rows[1].Loss=500;v.Rows[1].Changed();v.UpdateCards();
  Check(v.Rows[1].Advice==""&&v.AdviceCount==0,"advice clears when fixed");
 }
 static void AdviceUpdatesAfterBulkApply(){
  var v=Fresh();v.AccountGrid.UnselectAll();v.AccountGrid.SelectedItems.Add(v.Rows[2]);v.AccountGrid.SelectedItems.Add(v.Rows[3]);
  Check(v.BulkApply("DLL","0")==null,"apply");
  Check(v.AdviceCount==2,"two rows lost their loss limit; count="+v.AdviceCount);
  Check(v.Rows[2].Advice.Contains("no daily loss"),"row2 advice: "+v.Rows[2].Advice);
 }
 static void AdviceNeverBlocksOrChangesSettings(){
  var v=Fresh();v.Rows[1].Target=0;v.Rows[1].Loss=0;v.Rows[1].Changed();v.UpdateCards();
  Check(v.Rows[1].Target==0&&v.Rows[1].Loss==0,"advice must not alter the values");
  Check(!v.StartButton.IsEnabled,"Start stays held regardless of advice");
  Check(v.Rows[0].Advice=="","leader row never gets follower advice");
 }
 static System.Collections.Generic.IEnumerable<T> Visual<T>(DependencyObject d) where T:DependencyObject{for(int i=0;i<System.Windows.Media.VisualTreeHelper.GetChildrenCount(d);i++){var c=System.Windows.Media.VisualTreeHelper.GetChild(d,i);if(c is T)yield return (T)c;foreach(var n in Visual<T>(c))yield return n;}}
 static void RatioCellFitsCommonValuesAt1160(){
  var v=Fresh();
  var host=new System.Windows.Controls.Border{Child=v};host.Measure(new Size(1160,720));host.Arrange(new Rect(0,0,1160,720));host.UpdateLayout();System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(System.Windows.Threading.DispatcherPriority.ContextIdle,new Action(()=>{}));host.UpdateLayout();
  var col=v.AccountGrid.Columns.Single(c=>(string)c.Header=="Ratio");
  v.Rows[1].Ratio=0.125;v.Rows[2].Ratio=12.5;v.Rows[3].Ratio=100;host.UpdateLayout();
  foreach(int i in new[]{1,2,3}){
   var cell=col.GetCellContent(v.Rows[i]);if(cell==null){v.AccountGrid.ScrollIntoView(v.Rows[i]);host.UpdateLayout();cell=col.GetCellContent(v.Rows[i]);}
   Check(cell!=null,"Ratio cell realized for row "+i);
   var tb=Visual<TextBox>(cell).FirstOrDefault()??cell as TextBox;Check(tb!=null,"Ratio TextBox found");
   var ft=new System.Windows.Media.FormattedText(tb.Text,System.Globalization.CultureInfo.InvariantCulture,FlowDirection.LeftToRight,new System.Windows.Media.Typeface(tb.FontFamily,tb.FontStyle,tb.FontWeight,tb.FontStretch),tb.FontSize,System.Windows.Media.Brushes.White);
   double need=ft.Width+tb.Padding.Left+tb.Padding.Right+tb.BorderThickness.Left+tb.BorderThickness.Right+2;
   Check(tb.ActualWidth+0.5>=need,"Ratio text '"+tb.Text+"' needs "+need.ToString("0.0")+"px but the box is "+tb.ActualWidth.ToString("0.0")+"px (column "+col.ActualWidth.ToString("0.0")+")");
  }
 }
 static void LogsReadableAndTopAligned(){
  var v=Fresh();
  var fg=v.LogBox.Foreground as System.Windows.Media.SolidColorBrush;Check(fg!=null,"log text uses a solid brush");
  double lum=(0.2126*fg.Color.R+0.7152*fg.Color.G+0.0722*fg.Color.B)/255.0;
  Check(lum>=0.80,"log text must be bright enough to read on the dark panel; luminance="+lum.ToString("0.00"));
  Check(v.LogBox.VerticalContentAlignment==VerticalAlignment.Top,"log text starts at the top, not the bottom; is "+v.LogBox.VerticalContentAlignment);
  Check(v.LogBox.IsReadOnly,"log stays read-only");
 }
 static void LogClearWorksAndIsEditableIndependent(){
  var v=Fresh();v.LogBox.Text="a"+Environment.NewLine+"b";v.ClearLog();
  Check(v.LogBox.Text=="","clear empties the log");
  v.Editable(false);v.LogBox.Text="x";v.ClearLog();Check(v.LogBox.Text=="","clearing the log is allowed while attached/held (it is not a setting)");
  Check(!v.StartButton.IsEnabled,"Start stays held");
 }
 // Tgt / DLL are text columns: a TextBlock until the cell enters edit mode. Use the grid's real edit path.
 static TextBox CellBox(DashboardView v,string header,AccountRow row){
  var col=v.AccountGrid.Columns.Single(c=>(string)c.Header==header);v.AccountGrid.ScrollIntoView(row,col);Layout(v);
  v.AccountGrid.SelectedItem=row;v.AccountGrid.CurrentCell=new DataGridCellInfo(row,col);
  if(!v.AccountGrid.BeginEdit()){throw new Exception("grid refused to enter edit mode for "+header);}Layout(v);
  var cell=col.GetCellContent(row);var tb=cell as TextBox??(cell==null?null:Visual<TextBox>(cell).FirstOrDefault());
  if(tb==null)throw new Exception("no TextBox after BeginEdit in "+header+" cell ("+(cell==null?"null":cell.GetType().Name)+")");return tb;}
 static void TypeBad(TextBox tb,string text){tb.Focus();tb.Text=text;var be=tb.GetBindingExpression(TextBox.TextProperty);if(be!=null)be.UpdateSource();}
 // A malformed hand-typed Target must survive a broad refresh and must block commit; it must never silently turn into the old value.
 static void BadTargetDraftSurvivesRefreshAndBlocksCommit(){
  var v=Fresh();var row=v.Rows[1];var tb=CellBox(v,"Tgt",row);
  TypeBad(tb,"12x5");
  Check(Validation.GetHasError(tb),"malformed Target must show a validation error right away");
  row.Changed();v.UpdateCards();Layout(v);
  var again=CellBox(v,"Tgt",row);
  Check(again.Text=="12x5","the typed text must not be replaced by the old value after a refresh; shows '"+again.Text+"'");
  Check(!v.TryCommitSettings(),"commit/persist must be refused while a Target draft is invalid");
  Check(row.Target==1000,"accepted value stays the last valid one (1000), never a half-parsed one; is "+row.Target);
 }
 static void BadLossDraftSurvivesRefreshAndBlocksCommit(){
  var v=Fresh();var row=v.Rows[2];var tb=CellBox(v,"DLL",row);
  TypeBad(tb,"5oo");
  Check(Validation.GetHasError(tb),"malformed DLL must show a validation error");
  row.Changed();v.UpdateCards();Layout(v);
  var again=CellBox(v,"DLL",row);
  Check(again.Text=="5oo","typed DLL text kept after refresh; shows '"+again.Text+"'");
  Check(!v.TryCommitSettings(),"commit refused while a DLL draft is invalid");
  Check(row.Loss==500,"accepted DLL unchanged; is "+row.Loss);
 }
 // The invalid row scrolls out of view (recycled). Commit must STILL be refused.
 static void BadTargetDraftSurvivesRecyclingAndStillBlocksCommit(){
  var v=Fresh();var row=v.Rows[1];var tb=CellBox(v,"Tgt",row);
  TypeBad(tb,"1e");
  var last=v.Rows[v.Rows.Count-1];v.AccountGrid.ScrollIntoView(last);Layout(v);v.AccountGrid.ScrollIntoView(last,v.AccountGrid.Columns.Last());Layout(v);
  Check(!v.TryCommitSettings(),"commit must be refused even after the invalid row was scrolled out of view");
  var back=CellBox(v,"Tgt",row);
  Check(back.Text=="1e","draft returns when the row is scrolled back; shows '"+back.Text+"'");
  back.Text="900";var be=back.GetBindingExpression(TextBox.TextProperty);if(be!=null)be.UpdateSource();Layout(v);
  Check(row.Target==900,"correcting the draft applies it; is "+row.Target);
  Check(v.TryCommitSettings(),"commit allowed again once every draft is valid");
 }
 // The model must reject values the engine treats as invalid, so a stray minus sign or NaN can never be accepted silently.
 static void TargetAndLossSetterRejectInvalidNumbers(){
  var v=Fresh();var row=v.Rows[1];
  foreach(double bad in new[]{-1.0,double.NaN,double.PositiveInfinity,double.NegativeInfinity}){
   bool threw=false;try{row.Target=bad;}catch(ArgumentException){threw=true;}Check(threw,"Target must reject "+bad);Check(row.Target==1000,"Target unchanged after rejecting "+bad+"; is "+row.Target);
   threw=false;try{row.Loss=bad;}catch(ArgumentException){threw=true;}Check(threw,"Loss must reject "+bad);Check(row.Loss==500,"Loss unchanged after rejecting "+bad+"; is "+row.Loss);
  }
  row.Target=0;row.Loss=0;Check(row.Target==0&&row.Loss==0,"0 (= off) stays legal");row.Target=1234.5;Check(row.Target==1234.5,"normal value accepted");
 }
 [STAThread] public static int Main(string[] args){
  try{
   AppDomain.CurrentDomain.AssemblyResolve+=(s,e)=>{string n=e.Name.Split(',')[0];if(n.EndsWith(".resources"))return null;var loaded=AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a=>a.GetName().Name==n);if(loaded!=null)return loaded;string p=System.IO.Path.Combine("C:/Program Files/NinjaTrader 8/bin",n+".dll");return System.IO.File.Exists(p)?System.Reflection.Assembly.LoadFrom(p):null;};
   var all=new Action[]{TargetAndLossApplyToSelectedOnly,ZeroTurnsLimitOff,RatioStaysIndependent,InvalidInputChangesNothing,NoSelectionIsRefused,LeaderIsNotEdited,AtmToggleBulk,LockedWhenNotEditable,NeverTouchesFollowOrTrading,BarIsOnCopyTabAndKeepsHolds,FromLeaderCopiesToHighlighted,FromLeaderWithoutHighlightHitsAllVisibleFollowers,FromLeaderRespectsSearchFilter,FromLeaderKeepsRatioTickAndOtherFields,ManualEditAfterwardsSticksAndNoLiveLink,FromLeaderRefusedWhenLockedOrNoLeader,FromLeaderNeverTouchesFollowOrTrading,AdviceShownPerRowAndCounted,AdviceUpdatesAfterBulkApply,AdviceNeverBlocksOrChangesSettings,RatioCellFitsCommonValuesAt1160,LogsReadableAndTopAligned,LogClearWorksAndIsEditableIndependent,BadTargetDraftSurvivesRefreshAndBlocksCommit,BadLossDraftSurvivesRefreshAndBlocksCommit,BadTargetDraftSurvivesRecyclingAndStillBlocksCommit,TargetAndLossSetterRejectInvalidNumbers};
   int failed=0;foreach(var a in all){try{a();Console.WriteLine("PASS "+a.Method.Name);}catch(Exception ex){failed++;Console.WriteLine("FAIL "+a.Method.Name+" :: "+ex.Message);}}
   return failed==0?0:1;
  }catch(Exception ex){Console.WriteLine("HARNESS "+ex);return 2;}
 }
}
