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
 // Pixel-level check: render a control on a dark host and measure the average luminance of its own area. Template triggers (hover / disabled) are only visible this way.
 // Render the WHOLE real dashboard (this is what R.exe proved renders correctly off-screen) and measure only the pixels covered by `el`.
 static double Luma(FrameworkElement el){
  var root=el;while(root.Parent as FrameworkElement!=null)root=(FrameworkElement)root.Parent;
  var host=root as DashboardView;if(host==null)throw new Exception("control is not inside a DashboardView");
  Layout(host);var origin=el.TransformToAncestor(host).Transform(new Point(0,0));
  int W=(int)host.ActualWidth,H=(int)Math.Min(host.ActualHeight,400);
  var rtb=new System.Windows.Media.Imaging.RenderTargetBitmap(W,H,96,96,System.Windows.Media.PixelFormats.Pbgra32);rtb.Render(host);
  var px=new byte[W*H*4];rtb.CopyPixels(px,W*4,0);
  int x0=(int)origin.X,y0=(int)origin.Y,w=(int)el.ActualWidth,h=(int)el.ActualHeight;double best=0;
  for(int by=y0;by+8<=Math.Min(H,y0+h);by+=4)for(int bx=x0;bx+8<=Math.Min(W,x0+w);bx+=4){double sum=0;int n=0;
   for(int y=by;y<by+8;y+=2)for(int x=bx;x<bx+8;x+=2){int i=(y*W+x)*4;sum+=0.2126*px[i+2]+0.7152*px[i+1]+0.0722*px[i];n++;}
   double avg=sum/n;if(avg>best)best=avg;}
  return best;}
 static T FindDescendant<T>(DependencyObject root) where T:DependencyObject{
  int n=System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
  for(int i=0;i<n;i++){var c=System.Windows.Media.VisualTreeHelper.GetChild(root,i);var t=c as T;if(t!=null)return t;var d=FindDescendant<T>(c);if(d!=null)return d;}
  return null;}
 static double HoverLuma(System.Windows.Controls.Primitives.ToggleButton toggle,FrameworkElement measured){
  // IsMouseOver is read-only; setting it through WPF's internal key runs the same template triggers a real hover does.
  var key=typeof(UIElement).GetField("IsMouseOverPropertyKey",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic);
  Check(key!=null,"IsMouseOverPropertyKey reachable");
  var k=(DependencyPropertyKey)key.GetValue(null);toggle.SetValue(k,true);double l=Luma(measured);toggle.ClearValue(k);return l;}
 static int WhitePx(FrameworkElement el){
  var host=el;while(host.Parent as FrameworkElement!=null)host=(FrameworkElement)host.Parent;var dv=host as DashboardView;if(dv==null)throw new Exception("not inside a DashboardView");
  Layout(dv);var o=el.TransformToAncestor(dv).Transform(new Point(0,0));int W=1500,H=950;
  var rtb=new System.Windows.Media.Imaging.RenderTargetBitmap(W,H,96,96,System.Windows.Media.PixelFormats.Pbgra32);rtb.Render(dv);var px=new byte[W*H*4];rtb.CopyPixels(px,W*4,0);
  int x0=(int)o.X,y0=(int)o.Y,w=(int)el.ActualWidth,h=(int)el.ActualHeight,n=0;
  for(int y=y0;y<Math.Min(H,y0+h);y++)for(int x=x0;x<Math.Min(W,x0+w);x++){int i=(y*W+x)*4;if(px[i]>=240&&px[i+1]>=240&&px[i+2]>=240)n++;}
  return n;}
 static void CopyColumnsFitWorstCaseContentAndWasteNothing(){
  // Measured from the real window: Bal (141) / State (104) / P/L (103) were mostly empty. Pin the worst-case text widths so a shrink can never truncate real content.
  var v=Fresh();v.Sections.SelectedIndex=0;Layout(v);
  Func<string,double,string,double> textW=(txt,size,fam)=>{var t=new System.Windows.Media.Typeface(new System.Windows.Media.FontFamily(fam),FontStyles.Normal,FontWeights.Normal,FontStretches.Normal);return new System.Windows.Media.FormattedText(txt,System.Globalization.CultureInfo.InvariantCulture,FlowDirection.LeftToRight,t,size,System.Windows.Media.Brushes.White).WidthIncludingTrailingWhitespace;};
  var worst=new System.Collections.Generic.Dictionary<string,double>{
   {"Bal",textW("1,250,000.00 USD",13,"Segoe UI")+18},          // BalanceText cell, 9px margins
   {"State",textW("Rechecking",13,"Segoe UI")+22},               // longest StateLabel + lamp/padding
   {"P/L",textW("-123,456.78",13,"Consolas")+18}};
  foreach(var c in v.AccountGrid.Columns){
   string h=""+c.Header;if(!worst.ContainsKey(h))continue;
   double actual=c.ActualWidth;
   Check(actual+0.5>=worst[h],h+" column ("+actual+" px) must fit its worst-case content ("+Math.Round(worst[h])+" px)");
   Check(actual<=worst[h]+60,h+" column ("+actual+" px) wastes more than 60 px over its worst-case content ("+Math.Round(worst[h])+" px)");
  }
 }
 static void PlainCheckBoxesStayDark(){
  // Live (RO) / Grp / Dry run (Risk tab) used WPF's default CheckBox template: a pure-white square in NinjaTrader's dark window, in every state.
  var v=Fresh();
  var key=(DependencyPropertyKey)typeof(UIElement).GetField("IsMouseOverPropertyKey",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic).GetValue(null);
  v.Sections.SelectedIndex=1;Layout(v);   // Sections holds Copy, Risk, Logs (the Dashboard title tab is outside it)
  // every plain (non-row) check box on the Risk tab: found by walking the real visual tree, not by field name
  var boxes=new System.Collections.Generic.List<CheckBox>();
  Action<DependencyObject> walk=null;walk=d=>{int n=System.Windows.Media.VisualTreeHelper.GetChildrenCount(d);for(int i=0;i<n;i++){var c=System.Windows.Media.VisualTreeHelper.GetChild(d,i);var cb=c as CheckBox;if(cb!=null&&cb.ActualWidth>5&&!boxes.Contains(cb)&&cb.Content is string)boxes.Add(cb);walk(c);}};
  walk(v);
  Check(boxes.Count>=3,"found Live (RO), Grp and Dry run among the Risk tab check boxes ("+boxes.Count+")");
  Check((DashboardSafety.ListAllAccounts?v.ShowLive.Visibility==Visibility.Collapsed:boxes.Contains(v.ShowLive))&&boxes.Contains(v.DryRunBox),"the walk reaches Dry run; Live (RO) is hidden when every account is listed");
  foreach(var b in boxes){
   string name=""+b.Content;
   // Live (RO) is disabled in a fresh view: enable it first, otherwise normal/hover/ticked would only ever measure the disabled look.
   bool was=b.IsEnabled;b.IsEnabled=true;
   int normal=WhitePx(b);b.SetValue(key,true);int hover=WhitePx(b);b.ClearValue(key);
   b.IsChecked=true;Layout(v);int ticked=WhitePx(b);b.IsChecked=false;Layout(v);
   b.IsEnabled=false;int dis=WhitePx(b);b.IsEnabled=was;
   // WPF's default CheckBox draws a pure-white square (about 130 near-white pixels). Text is anti-aliased and stays far below this.
   Check(normal<40,name+" normal: near-white pixels="+normal);
   Check(hover<40,name+" hover: near-white pixels="+hover);
   Check(ticked<40,name+" ticked: near-white pixels="+ticked);
   Check(dis<40,name+" disabled: near-white pixels="+dis);
  }
 }
 static void ComboAndInputStayDarkWhenDisabledOrHovered(){
  // Real NinjaTrader keeps these controls in the app's dark colours in EVERY state. WPF's default templates turn them white on hover / disabled.
  var v=Fresh();var lead=v.LeaderBox;var inst=v.InstrumentBox;
  var toggle=FindDescendant<System.Windows.Controls.Primitives.ToggleButton>(lead);Check(toggle!=null,"leader selector has its toggle");
  double normal=Luma(lead),instNormal=Luma(inst);
  v.Editable(false);Layout(v);double leadDisabled=Luma(lead),instDisabled=Luma(inst);v.Editable(true);Layout(v);
  double leadHover=HoverLuma(toggle,lead);
  Check(normal<175,"leader selector normal is dark, brightest block="+normal);
  Check(leadDisabled<175,"leader selector DISABLED (session armed) stays dark, brightest block="+leadDisabled);
  Check(leadHover<175,"leader selector HOVER stays dark, brightest block="+leadHover);
  Check(instNormal<175&&instDisabled<175,"instrument box stays dark normal/disabled: "+instNormal+"/"+instDisabled);
  // The "All" account-state filter (Copy tab) uses the same Combo(); hover it like a real mouse: the combo and its toggle both get IsMouseOver.
  var filter=(ComboBox)typeof(DashboardView).GetField("filter",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(v);
  Check(filter!=null&&filter.SelectedItem as string=="All","the All filter exists and shows All");
  var key=(DependencyPropertyKey)typeof(UIElement).GetField("IsMouseOverPropertyKey",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic).GetValue(null);
  var ft=FindDescendant<System.Windows.Controls.Primitives.ToggleButton>(filter);
  double allNormal=Luma(filter);filter.SetValue(key,true);ft.SetValue(key,true);double allHover=Luma(filter);ft.ClearValue(key);filter.ClearValue(key);
  filter.IsEnabled=false;double allDisabled=Luma(filter);filter.IsEnabled=true;
  Check(allNormal<175,"All filter normal is dark, brightest block="+allNormal);
  Check(allHover<175,"All filter HOVER stays dark (was white), brightest block="+allHover);
  Check(allDisabled<175,"All filter DISABLED stays dark, brightest block="+allDisabled);
 }
 static void BarIsOnCopyTabAndKeepsHolds(){
  var v=Fresh();Layout(v);
  Check(v.AccountGrid.Columns.Count==14,"14 Copy columns retained (13 + the per-account Flat column)");
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
 // Account name hidden by default (applied by the tab from columns.txt defaults), Alias identifies the row: tooltip = full account name.
 static void AccountNameHiddenByDefaultAliasCarriesTheName(){
  var v=Fresh();v.ApplyColumns(ColumnPrefs.Parse(null));Layout(v);
  Check(Col(v.AccountGrid,"Acct").Visibility==Visibility.Collapsed&&Col(v.RiskGrid,"Acct").Visibility==Visibility.Collapsed,"account name hidden by default on Copy and Risk");
  Check(Col(v.AccountGrid,"Alias").Visibility==Visibility.Visible,"alias shown");
  v.SetColumnVisible("copy","Acct",true);Layout(v);Check(Col(v.AccountGrid,"Acct").Visibility==Visibility.Visible,"can be shown again");
  Check(ColumnPrefs.Parse(ColumnPrefs.Serialize(v.HiddenColumns)).SetEquals(v.HiddenColumns)&&!v.HiddenColumns.Contains("copy:Acct"),"the choice to show it is saved");
  var st=((DataGridTextColumn)Col(v.AccountGrid,"Alias")).ElementStyle;
  var tip=st.Setters.OfType<Setter>().FirstOrDefault(x=>x.Property==FrameworkElement.ToolTipProperty);
  Check(tip!=null&&tip.Value is System.Windows.Data.Binding&&((System.Windows.Data.Binding)tip.Value).Path.Path=="Name","alias tooltip shows the full account name");
 }
 static void PnlIsGreenForProfitRedForLossMutedForZero(){
  var v=Fresh();
  var host=new System.Windows.Controls.Border{Child=v};host.Measure(new Size(1400,720));host.Arrange(new Rect(0,0,1400,720));host.UpdateLayout();System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(System.Windows.Threading.DispatcherPriority.ContextIdle,new Action(()=>{}));
  v.Rows[1].Pnl=125.5;v.Rows[2].Pnl=-40;v.Rows[3].Pnl=0;v.Rows[4].Pnl=double.NaN;v.Rows[5].Pnl=0.001;
  foreach(var r in v.Rows)r.Changed();
  foreach(var grid in new[]{v.AccountGrid,v.RiskGrid}){
   v.Sections.SelectedIndex=grid==v.AccountGrid?0:1;host.UpdateLayout();System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(System.Windows.Threading.DispatcherPriority.ContextIdle,new Action(()=>{}));
   var col=Col(grid,"P/L");
   foreach(var pair in new[]{Tuple.Create(1,DashboardView.Good),Tuple.Create(2,DashboardView.Bad),Tuple.Create(3,DashboardView.Muted),Tuple.Create(4,DashboardView.Muted),Tuple.Create(5,DashboardView.Muted)}){
    var row=v.Rows[pair.Item1];var cell=col.GetCellContent(row);if(cell==null){grid.ScrollIntoView(row);host.UpdateLayout();cell=col.GetCellContent(row);}
    var tb=cell as TextBlock??(cell==null?null:Visual<TextBlock>(cell).FirstOrDefault());Check(tb!=null,"P/L cell realized, row "+pair.Item1);
    Check(tb.Foreground==pair.Item2,(grid==v.AccountGrid?"Copy":"Risk")+" P/L "+row.Pnl+" colour "+tb.Foreground+" expected "+pair.Item2);
   }
   v.Rows[2].Pnl=12;v.Rows[2].Changed();host.UpdateLayout();
   var c2=col.GetCellContent(v.Rows[2]);var t2=c2 as TextBlock??Visual<TextBlock>(c2).FirstOrDefault();Check(t2.Foreground==DashboardView.Good,"turns green when the loss becomes a profit");
   v.Rows[2].Pnl=-40;v.Rows[2].Changed();host.UpdateLayout();
  }
 }
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
 // ---- Per-account Flat column (UI layer only: it asks the tab to flatten ONE account; it never calls the engine itself).
 static Button FlatButtonFor(DashboardView v,int rowIndex){
  v.Sections.SelectedIndex=0;Layout(v);var col=v.AccountGrid.Columns.Single(c=>(string)c.Header=="Flat");
  v.AccountGrid.ScrollIntoView(v.Rows[rowIndex],col);Layout(v);
  var cell=col.GetCellContent(v.Rows[rowIndex]);Check(cell!=null,"Flat cell realized for row "+rowIndex);
  var b=cell as Button??Visual<Button>(cell).FirstOrDefault();Check(b!=null,"Flat button found in row "+rowIndex);return b;}
 static void FlatColumnExistsAndEveryFollowerRowHasItsOwnButton(){
  var v=Fresh();Check(v.AccountGrid.Columns.Any(c=>(string)c.Header=="Flat"),"Flat column present on the Copy table");
  var b1=FlatButtonFor(v,1);var b2=FlatButtonFor(v,2);Check(!ReferenceEquals(b1,b2),"each row has its own button");
  Check(""+b1.Content=="Flat","button is text-labelled Flat (never the icon glyph of Off)");
  Check(""+b1.Content!="\\uE7E8","not the Off glyph");}
 static void FlatButtonOnLeaderRowIsDisabled(){var v=Fresh();var b=FlatButtonFor(v,0);Check(!b.IsEnabled,"the leader row has no per-account Flat (it is not a follower; Flatten all covers it)");}
 static void FlatButtonRequestsExactlyThatRowAndNothingElse(){
  var v=Fresh();var asked=new System.Collections.Generic.List<string>();v.FlatRow=r=>asked.Add(r.Name);int follow=0;v.FollowRow=(r,on)=>follow++;
  // the demo view never forwards (no trading from synthetic rows): prove the wiring on a view that is not demo by invoking the handler through the same routed event the button uses.
  var b=FlatButtonFor(v,3);b.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
  Check(asked.Count==0,"Demo rows must never reach the engine path");Check(follow==0,"Flat is not Off: it must not call the follow path");}
 static void FlatAndOffAreDifferentControlsWithDifferentGlyphsAndTips(){
  var v=Fresh();var off=v.AccountGrid.Columns.Single(c=>(string)c.Header=="Off");var flat=v.AccountGrid.Columns.Single(c=>(string)c.Header=="Flat");
  Check(!ReferenceEquals(off,flat),"separate columns");
  Check(((string)flat.HeaderStyle.Setters.OfType<Setter>().First(s=>s.Property==FrameworkElement.ToolTipProperty).Value).Contains("Flatten ONE account"),"header tip explains Flat");
  Check(((string)off.HeaderStyle.Setters.OfType<Setter>().First(s=>s.Property==FrameworkElement.ToolTipProperty).Value).Contains("not Flatten"),"Off header still says it is not Flatten");}
 static void FlatButtonFitsItsCellAtNarrowWidth(){
  var v=Fresh();var host=new System.Windows.Controls.Border{Child=v};host.Measure(new Size(900,720));host.Arrange(new Rect(0,0,900,720));host.UpdateLayout();
  System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(System.Windows.Threading.DispatcherPriority.ContextIdle,new Action(()=>{}));host.UpdateLayout();
  var col=v.AccountGrid.Columns.Single(c=>(string)c.Header=="Flat");v.AccountGrid.ScrollIntoView(v.Rows[1],col);host.UpdateLayout();
  var cell=col.GetCellContent(v.Rows[1]);var b=cell as Button??Visual<Button>(cell).FirstOrDefault();Check(b!=null,"button");
  Check(b.ActualWidth<=col.ActualWidth+0.5,"button ("+b.ActualWidth+") fits the column ("+col.ActualWidth+")");Check(b.ActualWidth>=40,"button stays readable ("+b.ActualWidth+")");}
 static void FlatCanFlatFollowsSimAndLeaderRules(){
  var r=new AccountRow{Name="x",Role="Leader",Connection="Synthetic"};Check(!r.CanFlat,"leader cannot Flat per-account");
  r.Role="Follower";Check(r.CanFlat,"synthetic follower can (demo only; never sent)");
  r.Connection="Disconnected";Check(!r.CanFlat,"a disconnected account cannot be asked to flatten");}
 static void FlatOnARealViewForwardsExactlyTheClickedRowOnce(){
  // A non-demo view (as the tab builds it) forwards the clicked row to FlatRow; the demo view never does. Synthetic rows keep this off any account.
  var v=new DashboardView(false);v.Editable(true);Layout(v);
  v.Rows.Add(new AccountRow{Name="real lead",Role="Leader",Connection="Synthetic"});v.Rows.Add(new AccountRow{Name="real A",Role="Follower",Connection="Synthetic"});v.Rows.Add(new AccountRow{Name="real B",Role="Follower",Connection="Synthetic"});
  Layout(v);var asked=new System.Collections.Generic.List<string>();int follow=0;v.FlatRow=r=>asked.Add(r.Name);v.FollowRow=(r,on)=>follow++;
  var b=FlatButtonFor(v,2);b.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
  Check(asked.Count==1&&asked[0]=="real B","exactly the clicked row, once; got ["+string.Join(",",asked)+"]");
  Check(follow==0,"Flat must not go through the follow (tick/Off) path");
  var lead=FlatButtonFor(v,0);lead.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
  Check(asked.Count==1,"the leader row's Flat is inert even if a click is forced");}
 // Confirmation windows: three "Ask before" switches on the Risk tab, all ticked by default; unticking reports the exact kind; Unlock has no switch.
 static void AskBeforeSwitchesDefaultOnAndReportKind(){
  var v=Fresh();v.Sections.SelectedIndex=1;Layout(v);
  var got=new System.Collections.Generic.List<string>();v.AskChanged=(k,on)=>got.Add(k+"="+on);
  foreach(var k in ConfirmPrefs.Kinds){var cb=v.AskBox(k);Check(cb!=null,"switch exists: "+k);Check(cb.IsChecked==true,"asked by default: "+k);Check(cb.ActualWidth>5,"switch is laid out on the Risk tab: "+k);}
  Check(v.AskBox("unlock")==null,"Unlock has no switch");
  v.AskFlatOneBox.IsChecked=false;v.AskFlatOneBox.IsChecked=true;v.AskStartBox.IsChecked=false;
  Check(string.Join(",",got)=="flatOne=False,flatOne=True,start=False","switches report their own kind: "+string.Join(",",got));
 }
 // Risk tab widths (user request 2026-10-14): Tgt / DLL wider, St / Preset / Cons narrower; long St / Preset / Cons text keeps its full text as tooltip.
 // Binding paths are property names in every language (the St / Preset paths used to be the translated label, so they were blank in Chinese).
 static void RiskColumnsWidthAndBindingsInEveryLanguage(){
  string keep=Lang.Current;
  try{
   foreach(var code in Lang.Codes){
    Lang.Set(code);var v=Fresh();v.Sections.SelectedIndex=1;Layout(v);
    v.Rows[3].Status="Disconnected | Unknown follower order / terminal fill evidence — no adoption; manual recovery required";
    Func<string,DataGridColumn> col=key=>v.RiskGrid.Columns.First(c=>DashboardView.ColumnKey(c)==key);
    Check(col("Tgt").Width.Value>=100&&col("DLL").Width.Value>=100,code+": Tgt / DLL at least 100 px");
    foreach(var k in new[]{"St","Preset","Cons"})Check(col(k).Width.Value<=120,code+": "+k+" at most 120 px ("+col(k).Width.Value+")");
    foreach(var k in new[]{"St","Preset"}){var b=((DataGridTextColumn)col(k)).Binding as System.Windows.Data.Binding;Check(b!=null&&(b.Path.Path=="Status"||b.Path.Path=="Preset"),code+": "+k+" binds a property, not a label: "+(b==null?"null":b.Path.Path));}
    foreach(var k in new[]{"St","Preset","Cons"}){var st=((DataGridTextColumn)col(k)).ElementStyle;Check(st.Setters.OfType<Setter>().Any(x=>x.Property==FrameworkElement.ToolTipProperty),code+": "+k+" has a full-text tooltip");}
   }
  }finally{Lang.Set(keep);}
 }
 // Flat at limit: per-row tick on the Risk tab, default OFF, bulk on/off acts on highlighted followers only, From Leader copies it.
 static void FlatAtLimitIsPerRowDefaultOffAndBulkScoped(){
  var v=Fresh();
  Check(v.Rows.All(r=>!r.FlatOnLimit),"default OFF on every row");
  Check(v.RiskGrid.Columns.Any(c=>DashboardView.ColumnKey(c)=="FlatLim"),"Risk tab has the Flat@Lim column");
  Select(v,1,2);Check(v.BulkApply("LimitFlat","on")==null,"bulk on");
  Check(v.Rows[1].FlatOnLimit&&v.Rows[2].FlatOnLimit&&!v.Rows[3].FlatOnLimit&&!v.Rows[0].FlatOnLimit,"only highlighted followers, never the leader");
  Check(v.Rows[1].CopyAtm==true,"ATM untouched by the Flat@Lim bulk button");
  Select(v,2);Check(v.BulkApply("LimitFlat","off")==null&&!v.Rows[2].FlatOnLimit&&v.Rows[1].FlatOnLimit,"bulk off on one row");
  Check(v.BulkApply("LimitFlat","maybe")!=null,"bad value refused");
 }
 // Column visibility (user request 2026-10-14: the Risk tab felt crowded). Display only.
 static DataGridColumn Col(DataGrid g,string key){return g.Columns.First(c=>DashboardView.ColumnKey(c)==key);}
 static void ColumnsCanBeHiddenButSafetyColumnsNever(){
  var v=Fresh();
  System.Collections.Generic.HashSet<string> saved=null;v.ColumnsChanged=h=>saved=h;
  foreach(var c in v.AccountGrid.Columns.Concat(v.RiskGrid.Columns))Check(c.Visibility==Visibility.Visible,"all visible by default: "+DashboardView.ColumnKey(c));
  v.SetColumnVisible("risk","Preset",false);v.SetColumnVisible("risk","Cons",false);v.SetColumnVisible("copy","Bal",false);Layout(v);
  Check(Col(v.RiskGrid,"Preset").Visibility==Visibility.Collapsed&&Col(v.RiskGrid,"Cons").Visibility==Visibility.Collapsed,"Risk Preset / Cons hidden");
  Check(Col(v.AccountGrid,"Bal").Visibility==Visibility.Collapsed,"Copy Bal hidden");
  Check(Col(v.AccountGrid,"P/L").Visibility==Visibility.Visible,"Copy P/L untouched");
  Check(saved!=null&&saved.Contains("risk:Preset")&&saved.Contains("copy:Bal")&&saved.Count==3,"change reported for saving: "+(saved==null?"null":string.Join(",",saved)));
  // safety columns ignore any request, from the menu or a hand-edited file
  foreach(var k in new[]{"State","Off","Flat","Qty","Conn","On","Ratio"})v.SetColumnVisible("copy",k,false);
  foreach(var k in new[]{"Tgt","DLL","Qty","St","Conn"})v.SetColumnVisible("risk",k,false);
  var withBad=new System.Collections.Generic.HashSet<string>(saved){"copy:Flat","copy:State","risk:St","risk:Tgt","risk:DLL"};v.ApplyColumns(withBad);Layout(v);
  Check(v.HiddenColumns.SetEquals(saved),"invalid entries are dropped, valid ones kept: "+string.Join(",",v.HiddenColumns));
  foreach(var k in new[]{"State","Off","Flat","Qty","Conn","On","Ratio"})Check(Col(v.AccountGrid,k).Visibility==Visibility.Visible,"copy safety column always shown: "+k);
  foreach(var k in new[]{"Tgt","DLL","Qty","St","Conn"})Check(Col(v.RiskGrid,k).Visibility==Visibility.Visible,"risk safety column always shown: "+k);
  // every hideable key exists in its grid (a renamed column must not leave a dead entry)
  foreach(var k in ColumnPrefs.Hideable("copy"))Check(v.AccountGrid.Columns.Any(c=>DashboardView.ColumnKey(c)==k),"copy hideable key exists: "+k);
  foreach(var k in ColumnPrefs.Hideable("risk"))Check(v.RiskGrid.Columns.Any(c=>DashboardView.ColumnKey(c)==k),"risk hideable key exists: "+k);
  // the header right-click menu exists on both grids and lists only hideable columns
  foreach(var grid in new[]{"copy","risk"}){var m=v.ColumnMenuFor(grid);Check(m!=null,grid+": header context menu");
   m.RaiseEvent(new RoutedEventArgs(ContextMenu.OpenedEvent,m));
   var ticks=m.Items.OfType<MenuItem>().Where(x=>x.IsCheckable).Select(x=>((string)x.Tag).Substring(grid.Length+5)).ToArray();
   Check(ticks.Length==ColumnPrefs.Hideable(grid).Length&&ticks.All(k=>ColumnPrefs.CanHide(grid,k)),grid+": menu lists exactly the hideable columns: "+string.Join(",",ticks));}
  // the menu tick itself hides the column (IsChecked change, as UI Automation Toggle / keyboard do; no Click event)
  {var m=v.ColumnMenuFor("risk");m.RaiseEvent(new RoutedEventArgs(ContextMenu.OpenedEvent,m));var it=m.Items.OfType<MenuItem>().First(x=>(string)x.Tag=="col:risk:Grp");it.IsChecked=false;Layout(v);
   Check(Col(v.RiskGrid,"Grp").Visibility==Visibility.Collapsed&&saved.Contains("risk:Grp"),"unticking the menu item hides Grp and reports it");
   m.RaiseEvent(new RoutedEventArgs(ContextMenu.OpenedEvent,m));it=m.Items.OfType<MenuItem>().First(x=>(string)x.Tag=="col:risk:Grp");Check(it.IsChecked==false,"menu reopens with Grp unticked");
   it.IsChecked=true;Layout(v);Check(Col(v.RiskGrid,"Grp").Visibility==Visibility.Visible&&!saved.Contains("risk:Grp"),"ticking shows it again");}
  // hidden columns survive a language change (new view, same saved set)
  var keep=Lang.Current;try{Lang.Set("zh");var z=Fresh();z.ApplyColumns(saved);Layout(z);Check(Col(z.RiskGrid,"Preset").Visibility==Visibility.Collapsed,"zh view applies the saved set by key");}finally{Lang.Set(keep);}
  // Show all
  var risk=v.ColumnMenuFor("risk");risk.RaiseEvent(new RoutedEventArgs(ContextMenu.OpenedEvent,risk));
  var all=risk.Items.OfType<MenuItem>().First(x=>(string)x.Tag=="col:risk:*");all.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
  Check(Col(v.RiskGrid,"Preset").Visibility==Visibility.Visible&&Col(v.AccountGrid,"Bal").Visibility==Visibility.Collapsed,"Show all restores only that grid");
 }
 // The two read-only delta columns are named for what they show, in every language.
 static void DeltaColumnsHaveReadableNames(){
  var keep=Lang.Current;
  try{foreach(var code in Lang.Codes){Lang.Set(code);var v=Fresh();
   var a=Col(v.RiskGrid,"To Tgt");var b=Col(v.RiskGrid,"Loss left");
   Check(a.IsReadOnly&&b.IsReadOnly,code+": delta columns read-only");
   Check(!((string)a.Header).Contains("Δ")&&!((string)b.Header).Contains("Δ"),code+": no bare Δ header: "+a.Header+" / "+b.Header);}}
  finally{Lang.Set(keep);}
 }
 [STAThread] public static int Main(string[] args){
  try{
   AppDomain.CurrentDomain.AssemblyResolve+=(s,e)=>{string n=e.Name.Split(',')[0];if(n.EndsWith(".resources"))return null;var loaded=AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a=>a.GetName().Name==n);if(loaded!=null)return loaded;string p=System.IO.Path.Combine("C:/Program Files/NinjaTrader 8/bin",n+".dll");return System.IO.File.Exists(p)?System.Reflection.Assembly.LoadFrom(p):null;};
   var all=new Action[]{PnlIsGreenForProfitRedForLossMutedForZero,AccountNameHiddenByDefaultAliasCarriesTheName,ColumnsCanBeHiddenButSafetyColumnsNever,DeltaColumnsHaveReadableNames,FlatAtLimitIsPerRowDefaultOffAndBulkScoped,RiskColumnsWidthAndBindingsInEveryLanguage,AskBeforeSwitchesDefaultOnAndReportKind,CopyColumnsFitWorstCaseContentAndWasteNothing,EveryVisibleHeaderAndTabIsTranslatedExceptKeptAbbreviations,StateFilterWorksInEveryLanguage,LanguagesRenderTranslatedHeadersAndKeepEnglishKeys,LanguagesNeverTruncateOrShowMojibake,LanguageSwitchKeepsRowsAndRefusesOnlyBadInput,PlainCheckBoxesStayDark,ComboAndInputStayDarkWhenDisabledOrHovered,TargetAndLossApplyToSelectedOnly,ZeroTurnsLimitOff,RatioStaysIndependent,InvalidInputChangesNothing,NoSelectionIsRefused,LeaderIsNotEdited,AtmToggleBulk,LockedWhenNotEditable,NeverTouchesFollowOrTrading,BarIsOnCopyTabAndKeepsHolds,FromLeaderCopiesToHighlighted,FromLeaderWithoutHighlightHitsAllVisibleFollowers,FromLeaderRespectsSearchFilter,FromLeaderKeepsRatioTickAndOtherFields,ManualEditAfterwardsSticksAndNoLiveLink,FromLeaderRefusedWhenLockedOrNoLeader,FromLeaderNeverTouchesFollowOrTrading,AdviceShownPerRowAndCounted,AdviceUpdatesAfterBulkApply,AdviceNeverBlocksOrChangesSettings,RatioCellFitsCommonValuesAt1160,LogsReadableAndTopAligned,LogClearWorksAndIsEditableIndependent,BadTargetDraftSurvivesRefreshAndBlocksCommit,BadLossDraftSurvivesRefreshAndBlocksCommit,BadTargetDraftSurvivesRecyclingAndStillBlocksCommit,TargetAndLossSetterRejectInvalidNumbers,LockedAccountIsRedWithReasonAndCountedInFooter,LockedCountIgnoresLeaderAndNeverBlocksUi,BadRatioDraftSurvivesRefreshAndBlocksCommit,UnprotectedReasonReachesRowAndDetail,MaxContractsRejectsInvalidAndKeepsDraft,DivergenceChoiceIsPerAccountDefaultsToNotify,DivergenceBulkAndFromLeaderAndNeverTrades,FlattenOnceShowsAWarningNote,FlatColumnExistsAndEveryFollowerRowHasItsOwnButton,FlatButtonOnLeaderRowIsDisabled,FlatButtonRequestsExactlyThatRowAndNothingElse,FlatAndOffAreDifferentControlsWithDifferentGlyphsAndTips,FlatButtonFitsItsCellAtNarrowWidth,FlatCanFlatFollowsSimAndLeaderRules,FlatOnARealViewForwardsExactlyTheClickedRowOnce};
   int failed=0;foreach(var a in all){try{a();Console.WriteLine("PASS "+a.Method.Name);}catch(Exception ex){failed++;Console.WriteLine("FAIL "+a.Method.Name+" :: "+ex.Message);}}
   return failed==0?0:1;
  }catch(Exception ex){Console.WriteLine("HARNESS "+ex);return 2;}
 }
// Appended to BulkUiTests.cs (WPF suite). Renders the REAL DashboardView in each language.
 static System.Collections.Generic.List<string> Texts(DependencyObject root){
  var list=new System.Collections.Generic.List<string>();
  Action<DependencyObject> walk=null;walk=d=>{
   var tb=d as TextBlock;if(tb!=null&&!string.IsNullOrEmpty(tb.Text))list.Add(tb.Text);
   var ct=d as ContentControl;if(ct!=null){var s=ct.Content as string;if(!string.IsNullOrEmpty(s))list.Add(s);}
   int n=System.Windows.Media.VisualTreeHelper.GetChildrenCount(d);for(int i=0;i<n;i++)walk(System.Windows.Media.VisualTreeHelper.GetChild(d,i));};
  walk(root);return list;
 }
 static bool Truncated(FrameworkElement e){
  var tb=e as TextBlock;if(tb==null||tb.ActualWidth<1)return false;
  var t=new System.Windows.Media.Typeface(tb.FontFamily,tb.FontStyle,tb.FontWeight,tb.FontStretch);
  var ft=new System.Windows.Media.FormattedText(tb.Text,System.Globalization.CultureInfo.CurrentUICulture,FlowDirection.LeftToRight,t,tb.FontSize,System.Windows.Media.Brushes.Black);
  // A deliberately narrow column (Risk tab St / Preset / Cons) shows "…" AND carries the exact full text as its tooltip: nothing is lost, so it is not a truncation.
  // Any other clipped text (no tooltip, or a tooltip that differs from the text) still counts.
  if(tb.TextTrimming==TextTrimming.CharacterEllipsis&&(tb.ToolTip as string)==tb.Text&&!string.IsNullOrEmpty(tb.Text))return false;
  return tb.TextWrapping==TextWrapping.NoWrap&&ft.Width>tb.ActualWidth+1.5&&tb.TextTrimming!=TextTrimming.None;
 }
 static void LanguagesRenderTranslatedHeadersAndKeepEnglishKeys(){
  string was=Lang.Current;
  try{
   var english=new System.Collections.Generic.List<string>();
   foreach(var code in Lang.Codes){
    Lang.Set(code);var v=Fresh();v.Sections.SelectedIndex=0;Layout(v);
    var keys=v.AccountGrid.Columns.Select(c=>DashboardView.ColumnKey(c)).ToList();
    if(code=="en")english=keys;
    Check(keys.SequenceEqual(english),code+": column keys must stay English and identical to the English build: "+string.Join("|",keys));
    var headers=v.AccountGrid.Columns.Select(c=>""+c.Header).ToList();
    if(code!="en"){
     int changed=0;for(int i=0;i<headers.Count;i++)if(headers[i]!=keys[i])changed++;
     Check(changed>=6,code+": at least 6 Copy-table headers must be translated; changed="+changed+" headers="+string.Join("|",headers));
    }
    // star-width table still finds every column by English key: a translated header must not drop the proportional sizing
    foreach(var c in v.AccountGrid.Columns){ var k=DashboardView.ColumnKey(c); if(k=="Acct")Check(c.MinWidth>=100,code+": Acct keeps its minimum width ("+c.MinWidth+")"); }
   }
  } finally { Lang.Set(was); }
 }
 static void LanguagesNeverTruncateOrShowMojibake(){
  string was=Lang.Current;
  try{
   foreach(var code in Lang.Codes){
    Lang.Set(code);var v=Fresh();
    for(int tab=0;tab<3;tab++){
     v.Sections.SelectedIndex=tab;Layout(v);
     var all=Texts(v);
     foreach(var s in all){
      Check(s.IndexOf('\uFFFD')<0,code+" tab "+tab+": replacement character in '"+s+"'");
      Check(s.Trim().Trim('?').Length>0||s.Trim().Length==0,code+": text is only question marks (lost characters): '"+s+"'");
     }
     // anything rendered with NoWrap and clipped (text wider than its box and a trimming mode set) is a truncation
     Action<DependencyObject> walk=null;int bad=0;string first="";
     walk=d=>{var fe=d as FrameworkElement;if(fe!=null&&Truncated(fe)){bad++;if(first=="")first=((TextBlock)fe).Text;}int n=System.Windows.Media.VisualTreeHelper.GetChildrenCount(d);for(int i=0;i<n;i++)walk(System.Windows.Media.VisualTreeHelper.GetChild(d,i));};
     walk(v);
     Check(bad==0,code+" tab "+tab+": "+bad+" clipped text element(s), first: '"+first+"'");
    }
    // the fixed-width header cells (Tgt/DLL/Qty/Bal/State/ATM/Off/Flat/Acct/Alias/Ratio) must fit their header text
    v.Sections.SelectedIndex=0;Layout(v);
    foreach(var c in v.AccountGrid.Columns){
     string h=c.Header as string;if(string.IsNullOrEmpty(h))continue;
     var t=new System.Windows.Media.Typeface(new System.Windows.Media.FontFamily("Segoe UI"),FontStyles.Normal,FontWeights.SemiBold,FontStretches.Normal);
     var ft=new System.Windows.Media.FormattedText(h,System.Globalization.CultureInfo.CurrentUICulture,FlowDirection.LeftToRight,t,13,System.Windows.Media.Brushes.Black);
     Check(c.ActualWidth+0.5>=ft.Width+10,code+": header '"+h+"' ("+Math.Round(ft.Width+10)+" px) must fit its column ("+Math.Round(c.ActualWidth)+" px)");
    }
   }
  } finally { Lang.Set(was); }
 }
 static void LanguageSwitchKeepsRowsAndRefusesOnlyBadInput(){
  // The picker raises LanguageChanging on the real view; the owner may refuse (attached session). A refusal restores the old choice.
  string was=Lang.Current;
  try{
   Lang.Set("en");var v=Fresh();
   string asked=null;v.LanguageChanging=c=>{asked=c;return false;};
   v.LanguageBox.SelectedIndex=Array.IndexOf(Lang.Codes,"ja");
   Check(asked=="ja","picker reports the chosen code");
   Check(v.LanguageBox.SelectedIndex==Array.IndexOf(Lang.Codes,"en"),"a refused change snaps the picker back to the active language");
   Check(Lang.Current=="en","the view itself never switches the language; only the owner does");
   int rows=v.Rows.Count;v.LanguageChanging=c=>true;v.LanguageBox.SelectedIndex=Array.IndexOf(Lang.Codes,"ko");
   Check(v.Rows.Count==rows,"picking a language does not touch the account rows");
  } finally { Lang.Set(was); }
 }
 static void StateFilterWorksInEveryLanguage(){
  // The filter compares by position, never by (translated) text: picking "Paused" in zh/ja/ko must still show only the paused/locked rows.
  string was=Lang.Current;
  try{
   foreach(var code in Lang.Codes){
    Lang.Set(code);var v=Fresh();
    var filter0=(ComboBox)typeof(DashboardView).GetField("filter",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(v);var follower=v.Rows.First(r=>r.Role!="Leader");follower.Paused=true;follower.Changed();
    int total=v.VisibleCount;
    v.AccountGrid.UnselectAll();
    filter0.SelectedIndex=2; // "Paused"
    Layout(v);
    Check(v.VisibleCount>=1&&v.VisibleCount<total,code+": the Paused filter shows only paused/locked rows (visible="+v.VisibleCount+", total="+total+")");
    filter0.SelectedIndex=0;Layout(v);
    Check(v.VisibleCount==total,code+": All shows every row again");
   }
  } finally { Lang.Set(was); }
 }
 static void EveryVisibleHeaderAndTabIsTranslatedExceptKeptAbbreviations(){
  // Abbreviations that deliberately stay Latin in every language (trading jargon / platform names).
  var kept=new System.Collections.Generic.HashSet<string>{"Tgt","DLL","ATM","CCY","P/L","Dashboard","KINFX","⚙","⌕"};
  string was=Lang.Current;
  try{
   foreach(var code in new[]{"zh","ja","ko"}){
    Lang.Set(code);var v=Fresh();
    var untranslated=new System.Collections.Generic.List<string>();
    foreach(var grid in new[]{v.AccountGrid,v.RiskGrid})
     foreach(var c in grid.Columns){
      string key=DashboardView.ColumnKey(c),shown=""+c.Header;
      if(string.IsNullOrEmpty(shown)||kept.Contains(key))continue;
      if(shown==key&&Lang.Row(key)==null)untranslated.Add(key);
      else if(shown==key&&Lang.Row(key)!=null&&Lang.Row(key)[code=="zh"?0:code=="ja"?1:2]==key)untranslated.Add(key+"(same)");
     }
    foreach(TabItem tab in v.Sections.Items){
     string h=""+tab.Header;
     if(Lang.Row("Copy")!=null&&(h=="Copy"||h=="Risk"||h=="Logs"))untranslated.Add("tab "+h);
    }
    Check(untranslated.Count==0,code+": untranslated visible headers/tabs: "+string.Join(", ",untranslated));
   }
  } finally { Lang.Set(was); }
 }
}
