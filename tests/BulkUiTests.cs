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
 static void RiskDialogInvalidDraftSurvivesPresetLoad() {
  var initial=new RiskSettings{Floor=100};var dialog=new RiskSettingsDialog(initial,"MES",1);
  try {
   var boxes=(System.Collections.Generic.Dictionary<string,TextBox>)typeof(RiskSettingsDialog).GetField("boxes",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(dialog);
   boxes["Floor"].Text="0,125";
   Check(boxes["Floor"].BorderBrush==DashboardView.Bad&&boxes["Floor"].ToolTip!=null,"invalid draft immediately indicated");
   Check(dialog.CurrentDraft.Floor==100&&initial.Floor==100,"invalid text never publishes accepted settings");
   dialog.FirmChoice.SelectedIndex=1;dialog.LoadSelectedPreset();
   Check(boxes["Floor"].Text=="0,125"&&boxes["Floor"].BorderBrush==DashboardView.Bad,"template refresh retains invalid typed text");
   boxes["Floor"].Text="125";Check(dialog.CurrentDraft.Floor==125&&boxes["Floor"].BorderBrush==DashboardView.Line,"correction clears draft error");
  } finally {dialog.Close();}
 }
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
 // Issue #5: the SAME draft rules for Ratio (positive finite) and Max (whole number >= 1). Typed junk is kept visible, blocks commit,
 // and the accepted value stays the last valid one.
 static TextBox CellBoxIn(DataGrid g,string header,AccountRow row){
  var col=g.Columns.First(c=>(c.Header as string)==header);g.SelectedItem=row;g.CurrentCell=new DataGridCellInfo(row,col);
  if(!g.BeginEdit())throw new Exception("grid refused to enter edit mode for "+header);Layout(Fresh0);
  var cell=col.GetCellContent(row);return cell as TextBox??(cell==null?null:Visual<TextBox>(cell).FirstOrDefault());}
 static DashboardView Fresh0;
 static void BadRatioDraftSurvivesRefreshAndBlocksCommit(){
  var v=Fresh();Fresh0=v;var row=v.Rows[1];double before=row.Ratio;var tb=CellBox(v,"Ratio",row);
  TypeBad(tb,"1.5x");
  Check(Validation.GetHasError(tb),"malformed Ratio must show a validation error right away");
  row.Changed();v.UpdateCards();Layout(v);
  var again=CellBox(v,"Ratio",row);
  Check(again.Text=="1.5x","typed Ratio text must survive a refresh; shows '"+again.Text+"'");
  Check(!v.TryCommitSettings(),"commit refused while a Ratio draft is invalid");
  Check(row.Ratio==before,"accepted Ratio stays "+before+"; is "+row.Ratio);
  var last=v.Rows[v.Rows.Count-1];v.AccountGrid.ScrollIntoView(last);Layout(v);v.AccountGrid.ScrollIntoView(last,v.AccountGrid.Columns.Last());Layout(v);
  Check(!v.TryCommitSettings(),"commit still refused after the row was recycled out of view");
  var back=CellBox(v,"Ratio",row);Check(back.Text=="1.5x","Ratio draft returns when scrolled back; shows '"+back.Text+"'");
  back.Text="2";var be=back.GetBindingExpression(TextBox.TextProperty);if(be!=null)be.UpdateSource();Layout(v);
  Check(row.Ratio==2&&v.TryCommitSettings(),"correcting the draft applies it and re-allows commit; ratio="+row.Ratio);
 }
 static void MaxContractsRejectsInvalidAndKeepsDraft(){
  var v=Fresh();Fresh0=v;var row=v.Rows[1];int before=row.MaxContracts;
  foreach(int bad in new[]{0,-1,-100}){bool threw=false;try{row.MaxContracts=bad;}catch(ArgumentException){threw=true;}Check(threw,"Max must reject "+bad);Check(row.MaxContracts==before,"Max unchanged after rejecting "+bad+"; is "+row.MaxContracts);}
  v.Sections.SelectedIndex=1;Layout(v);
  var tb=CellBoxIn(v.RiskGrid,"Max",row);
  TypeBad(tb,"3.5");
  Check(Validation.GetHasError(tb),"a non-whole Max must show a validation error");
  row.Changed();v.UpdateCards();Layout(v);
  Check(!v.TryCommitSettings(),"commit refused while a Max draft is invalid");
  Check(row.MaxContracts==before,"accepted Max unchanged; is "+row.MaxContracts);
  Check(row.Settings.Valid(),"the row's accepted settings must remain Valid(); a rejected Max must never leave them invalid");
 }
 // Issue #3 policy: an UNPROTECTED lock reason is carried to the row and its detail text unchanged, so the user can read how many lots and what to do.
 static void UnprotectedReasonReachesRowAndDetail(){
  var v=Fresh();var row=v.Rows[3];
  string why="Late uncovered entry fill after exit completion — manual recovery required; coverage not established — 2 lots UNPROTECTED (no protective exit working) — check NinjaTrader now and place or flatten by hand; this copier will not create it";
  row.Latch.Locked=true;row.Latch.Reason=why;row.Changed();v.UpdateCards();Layout(v);
  Check(row.StateDetail.Contains("2 lots UNPROTECTED"),"row detail must carry the unprotected count; was: "+row.StateDetail);
  Check(row.StateDetail.Contains("NinjaTrader"),"row detail must tell the user where to act; was: "+row.StateDetail);
  Check(v.LockedCount>=1&&v.LockedText.Contains("LOCKED"),"footer counts the locked row; was: '"+v.LockedText+"'");
 }
 // Divergence / opposite-exposure handling today is notify-only: lock, red lamp, text and reason. It must be unmistakable.
 static void LockedAccountIsRedWithReasonAndCountedInFooter(){
  var v=Fresh();var row=v.Rows[4];
  Check(v.LockedCount==0,"fixture starts with no locked rows; got "+v.LockedCount);
  row.Latch.Locked=true;row.Latch.Reason="Unknown / opposite exposure — exit adoption blocked";row.Changed();v.UpdateCards();
  Check(row.StateLabel=="Locked","state text says Locked, not a colour alone; is "+row.StateLabel);
  Check(object.ReferenceEquals(row.StateLight,DashboardView.Bad),"state lamp is the red Bad brush");
  Check(row.StateDetail.Contains("opposite exposure"),"tooltip carries the engine reason: "+row.StateDetail);
  Check(v.LockedCount==1,"footer counts locked accounts; got "+v.LockedCount);
  Check(v.LockedText.Contains("1")&&v.LockedText.ToLowerInvariant().Contains("locked"),"footer text names the lock: "+v.LockedText);
  Check(!row.Enabled||row.StateLabel=="Locked","a locked row never reads as Copying");
  row.Latch.Locked=false;row.Latch.Reason="";row.Changed();v.UpdateCards();
  Check(v.LockedCount==0&&v.LockedText=="","footer clears when the latch is gone");
 }
 static void LockedCountIgnoresLeaderAndNeverBlocksUi(){
  var v=Fresh();v.Rows[0].Latch.Locked=true;v.Rows[0].Latch.Reason="leader row";v.Rows[0].Changed();v.UpdateCards();
  Check(v.LockedCount==0,"leader row is not counted as a locked follower");
  Check(!v.StartButton.IsEnabled,"Start stays held");
 }
 // Issue #12 setting: per-account reaction to divergence. Default Notify; FlattenOnce is an explicit per-account choice.
 static void DivergenceChoiceIsPerAccountDefaultsToNotify(){
  var v=Fresh();
  Check(v.Rows.Skip(1).All(r=>r.OnDivergence=="Notify"),"every row defaults to Notify");
  v.Rows[2].OnDivergence="FlattenOnce";Check(v.Rows[2].OnDivergence=="FlattenOnce"&&v.Rows[3].OnDivergence=="Notify","choice is per account");
  bool threw=false;try{v.Rows[2].OnDivergence="Retry";}catch(ArgumentException){threw=true;}
  Check(threw&&v.Rows[2].OnDivergence=="FlattenOnce","an invalid value is refused and the previous choice stays");
 }
 static void DivergenceBulkAndFromLeaderAndNeverTrades(){
  var v=Fresh();v.AccountGrid.UnselectAll();v.AccountGrid.SelectedItems.Add(v.Rows[1]);v.AccountGrid.SelectedItems.Add(v.Rows[2]);
  Check(v.BulkApply("Diverge","FlattenOnce")==null,"bulk apply");
  Check(v.Rows[1].OnDivergence=="FlattenOnce"&&v.Rows[2].OnDivergence=="FlattenOnce"&&v.Rows[3].OnDivergence=="Notify","bulk sets selected only");
  Check(v.BulkApply("Diverge","Retry")!=null,"invalid bulk value refused");Check(v.Rows[1].OnDivergence=="FlattenOnce","unchanged after refusal");
  v.Rows[0].OnDivergence="FlattenOnce";v.AccountGrid.UnselectAll();Check(v.BulkFromLeader()==null,"from leader");
  Check(v.Rows.Skip(1).All(r=>r.OnDivergence=="FlattenOnce"),"From Leader copies the divergence choice too");
  int follow=0;v.FollowRow=(r,on)=>follow++;v.BulkApply("Diverge","Notify");Check(follow==0,"setting never calls follow/engine");
  Check(!v.StartButton.IsEnabled,"Start stays held");
  v.Editable(false);Check(v.BulkApply("Diverge","FlattenOnce")!=null,"locked while attached");
 }
 static void FlattenOnceShowsAWarningNote(){
  var v=Fresh();v.Rows[3].OnDivergence="FlattenOnce";v.Rows[3].Changed();v.UpdateCards();
  Check(v.Rows[3].Advice.Contains("flatten"),"a FlattenOnce row carries a visible note that it is not active in this alpha: "+v.Rows[3].Advice);
  v.Rows[3].OnDivergence="Notify";v.Rows[3].Changed();v.UpdateCards();Check(!v.Rows[3].Advice.Contains("flatten"),"note gone for Notify");
 }
 [STAThread] public static int Main(string[] args){
  try{
   AppDomain.CurrentDomain.AssemblyResolve+=(s,e)=>{string n=e.Name.Split(',')[0];if(n.EndsWith(".resources"))return null;var loaded=AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a=>a.GetName().Name==n);if(loaded!=null)return loaded;string p=System.IO.Path.Combine("C:/Program Files/NinjaTrader 8/bin",n+".dll");return System.IO.File.Exists(p)?System.Reflection.Assembly.LoadFrom(p):null;};
   var all=new Action[]{TargetAndLossApplyToSelectedOnly,ZeroTurnsLimitOff,RatioStaysIndependent,InvalidInputChangesNothing,NoSelectionIsRefused,LeaderIsNotEdited,AtmToggleBulk,LockedWhenNotEditable,NeverTouchesFollowOrTrading,BarIsOnCopyTabAndKeepsHolds,FromLeaderCopiesToHighlighted,FromLeaderWithoutHighlightHitsAllVisibleFollowers,FromLeaderRespectsSearchFilter,FromLeaderKeepsRatioTickAndOtherFields,ManualEditAfterwardsSticksAndNoLiveLink,FromLeaderRefusedWhenLockedOrNoLeader,FromLeaderNeverTouchesFollowOrTrading,AdviceShownPerRowAndCounted,AdviceUpdatesAfterBulkApply,AdviceNeverBlocksOrChangesSettings,RatioCellFitsCommonValuesAt1160,LogsReadableAndTopAligned,LogClearWorksAndIsEditableIndependent,BadTargetDraftSurvivesRefreshAndBlocksCommit,BadLossDraftSurvivesRefreshAndBlocksCommit,BadTargetDraftSurvivesRecyclingAndStillBlocksCommit,TargetAndLossSetterRejectInvalidNumbers,LockedAccountIsRedWithReasonAndCountedInFooter,LockedCountIgnoresLeaderAndNeverBlocksUi,BadRatioDraftSurvivesRefreshAndBlocksCommit,UnprotectedReasonReachesRowAndDetail,MaxContractsRejectsInvalidAndKeepsDraft,DivergenceChoiceIsPerAccountDefaultsToNotify,DivergenceBulkAndFromLeaderAndNeverTrades,FlattenOnceShowsAWarningNote};
   int failed=0;foreach(var a in all){try{a();Console.WriteLine("PASS "+a.Method.Name);}catch(Exception ex){failed++;Console.WriteLine("FAIL "+a.Method.Name+" :: "+ex.Message);}}
   return failed==0?0:1;
  }catch(Exception ex){Console.WriteLine("HARNESS "+ex);return 2;}
 }
}
