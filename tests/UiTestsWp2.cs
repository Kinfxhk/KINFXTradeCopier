using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using NinjaTrader.NinjaScript.AddOns.Kinfx;
// WP2: Monitor page (cards, issue banner, flatten row) + Settings page (session / bulk sections, Max warn border, group header).
public static class UiTestsWp2 {
  public static Action[] Cases(){ return new Action[]{
    SummaryCardsShowCorrectValues, BannerCollapsesAndLinksSwitchTabs, UnlockAllAndFlattenLiveOnMonitor,
    BulkSectionAppliesOnSettingsTab, GroupHeaderShowsFirmAndCount, MaxIsDefaultWarnBorder, NoClippedTextNoWhiteBlocksAllLanguages,
    GroupToggleMirrorsGroupByAccount, IssuesHaveTheirOwnTab, LeaderRowIsAlwaysFirst, BulkEditsRatioAndMax, ColumnsFollowImportanceOrder, MonitorTableFitsDefaultWindowInEveryLanguage, SettingsStatusAndConnectionAreTranslated, MaxAndBalanceHeadersSort, IssuesListNotRebuiltWhenUnchanged, CurrencyCodeUsesSharedTableAndIssueOrderIgnored, NotifySectionEditsDraftOnly, ResumeAndRefollowControls, SettingsTableFitsDefaultWindowInEveryLanguage };}
  static T Find<T>(DashboardView v,string tag) where T:FrameworkElement{return BulkUiTests.Visual<T>(v).FirstOrDefault(x=>(x.Tag as string)==tag);}
  static FrameworkElement Tag(DashboardView v,string tag){return BulkUiTests.Visual<FrameworkElement>(v).FirstOrDefault(x=>(x.Tag as string)==tag);}
  static string CardValue(DashboardView v,string tag){var c=Tag(v,tag);BulkUiTests.Check(c!=null,"card exists: "+tag);return BulkUiTests.Visual<TextBlock>(c).Last().Text;}

  static void SummaryCardsShowCorrectValues(){
    var v=BulkUiTests.Fresh();
    v.Rows[1].Latch.Locked=true;v.Rows[1].Latch.Reason="Late uncovered entry fill — coverage not established — 2 lots UNPROTECTED — check now";
    v.Rows[2].Latch.Locked=true;v.Rows[2].Latch.Reason="Unknown / opposite exposure — exit adoption blocked";
    v.Rows[0].Latch.Locked=true;v.Rows[0].Latch.Reason="leader lock (must not be counted)";
    v.Rows[3].Currency="EUR";
    foreach(var r in v.Rows)r.Changed();
    v.UpdateCards();BulkUiTests.Layout(v);
    BulkUiTests.Check(CardValue(v,"card:copying")=="29 / 29","copying card 29/29: "+CardValue(v,"card:copying"));
    BulkUiTests.Check(CardValue(v,"card:locked")=="2","locked card counts followers only: "+CardValue(v,"card:locked"));
    BulkUiTests.Check(CardValue(v,"card:unprotected")=="2","unprotected lots: "+CardValue(v,"card:unprotected"));
    BulkUiTests.Check(CardValue(v,"card:pnl")==Lang.T("Mixed CCY"),"mixed currency pnl card: "+CardValue(v,"card:pnl"));
  }
  static void BannerCollapsesAndLinksSwitchTabs(){
    var v=BulkUiTests.Fresh();
    v.Sections.SelectedIndex=3;BulkUiTests.Layout(v);
    var issues=Tag(v,"issues");
    BulkUiTests.Check(issues!=null,"issues container exists");
    BulkUiTests.Check(issues.Visibility==Visibility.Collapsed,"issues collapsed with no issues");
    v.Rows[1].Latch.Locked=true;v.Rows[1].Latch.Reason="some lock reason";v.Rows[1].Changed();
    v.Rows[2].Latch.Locked=true;v.Rows[2].Latch.Reason="another lock reason";v.Rows[2].Changed();
    v.UpdateCards();BulkUiTests.Layout(v);
    BulkUiTests.Check(issues.Visibility==Visibility.Visible,"issues visible with locks");
    BulkUiTests.Check(BulkUiTests.Visual<Button>(v).Count(b=>(b.Tag as string)=="issue:logs")==2,"one issue:logs link per locked row");
    var logs=BulkUiTests.Visual<Button>(v).First(b=>(b.Tag as string)=="issue:logs");
    logs.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
    BulkUiTests.Check(v.Sections.SelectedIndex==2,"issue:logs switches to the Logs tab");
  }
  static void UnlockAllAndFlattenLiveOnMonitor(){
    var v=new DashboardView(false);v.Editable(true);v.Sections.SelectedIndex=3;BulkUiTests.Layout(v);
    var banner=Tag(v,"banner");
    var unlock=Find<Button>(v,"unlockAll");
    BulkUiTests.Check(banner!=null&&unlock!=null,"banner and unlockAll exist");
    // unlockAll is inside the banner host
    bool inside=false;for(var a=(DependencyObject)unlock;a!=null;a=System.Windows.Media.VisualTreeHelper.GetParent(a))if(a==banner){inside=true;break;}
    BulkUiTests.Check(inside,"unlockAll lives inside the banner");
    v.Sections.SelectedIndex=0;BulkUiTests.Layout(v);
    var fs=Find<Button>(v,"flattenSelected");var fa=Find<Button>(v,"flattenAll");
    BulkUiTests.Check(fs!=null&&fa!=null,"flatten buttons live on the monitor tab");
    BulkUiTests.Check(fs.IsEnabled==DashboardSafety.CanExecute("flattenSelected"),"flattenSelected follows CanExecute");
    BulkUiTests.Check(fa.IsEnabled==DashboardSafety.CanExecute("flattenAll"),"flattenAll follows CanExecute");
    // Independent review: moving to text buttons put Flatten into editButtons, so Start disabled it. Emergency flatten must survive attach.
    v.Editable(false);BulkUiTests.Layout(v);
    BulkUiTests.Check(fs.IsEnabled==DashboardSafety.CanExecute("flattenSelected"),"flattenSelected still follows CanExecute while attached");
    BulkUiTests.Check(fa.IsEnabled==DashboardSafety.CanExecute("flattenAll"),"flattenAll still follows CanExecute while attached");
  }
  static void BulkSectionAppliesOnSettingsTab(){
    var v=BulkUiTests.Fresh();v.Sections.SelectedIndex=1;BulkUiTests.Layout(v);
    v.RiskGrid.UnselectAll();v.RiskGrid.SelectedItems.Add(v.Rows[1]);v.RiskGrid.SelectedItems.Add(v.Rows[2]);
    var field=Find<ComboBox>(v,"bulkField");var value=Find<TextBox>(v,"bulkValue");var apply=Find<Button>(v,"bulkApply");
    BulkUiTests.Check(field!=null&&value!=null&&apply!=null,"bulk controls exist on settings tab");
    field.SelectedIndex=DashboardView.BulkFieldIndex("Tgt");BulkUiTests.Layout(v);
    value.Text="750";BulkUiTests.Layout(v);
    apply.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
    BulkUiTests.Check(v.Rows[1].Target==750&&v.Rows[2].Target==750&&v.Rows[3].Target==1000,"Daily target applied to highlighted rows only");
    double ratioBefore=v.Rows[1].Ratio;
    field.SelectedIndex=DashboardView.BulkFieldIndex("ATM");BulkUiTests.Layout(v);var combo=Find<ComboBox>(v,"bulkValueCombo");combo.SelectedIndex=1;BulkUiTests.Layout(v);
    apply.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
    BulkUiTests.Check(!v.Rows[1].CopyAtm&&!v.Rows[2].CopyAtm,"ATM exit mirroring off applied");
    field.SelectedIndex=DashboardView.BulkFieldIndex("Diverge");BulkUiTests.Layout(v);combo=Find<ComboBox>(v,"bulkValueCombo");combo.SelectedIndex=1;BulkUiTests.Layout(v);
    apply.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
    BulkUiTests.Check(v.Rows[1].OnDivergence=="FlattenOnce"&&v.Rows[2].OnDivergence=="FlattenOnce","On divergence = Flatten once applied");
    BulkUiTests.Check(v.Rows[1].Ratio==ratioBefore&&v.Rows[2].Ratio==1,"Ratio untouched by bulk");
    v.Editable(false);
    BulkUiTests.Check(v.BulkApply("Tgt","750")!=null,"bulk refused when not editable");
  }
  static void GroupHeaderShowsFirmAndCount(){
    var v=BulkUiTests.Fresh();
    v.GroupByAccount=true;BulkUiTests.Layout(v);
    var texts=BulkUiTests.Texts(v);
    BulkUiTests.Check(texts.Any(t=>t!=null&&t.StartsWith("Tradeify (10)")),"group header shows Tradeify (10): "+string.Join(" | ",texts.Where(t=>t!=null&&t.IndexOf("Tradeify",StringComparison.Ordinal)>=0)));
  }
  static void MaxIsDefaultWarnBorder(){
    var v=BulkUiTests.Fresh();v.Sections.SelectedIndex=1;BulkUiTests.Layout(v);
    var col=BulkUiTests.Col(v.RiskGrid,"Max");
    var r=v.Rows[1];r.Settings.Preset="";r.Settings.MaxContracts=1;r.Changed();v.UpdateCards();BulkUiTests.Layout(v);
    v.RiskGrid.ScrollIntoView(r,col);BulkUiTests.Layout(v);
    var cell=CellOf(v.RiskGrid,r,col);
    BulkUiTests.Check(cell!=null,"Max cell realized");
    BulkUiTests.Check(cell.BorderBrush==DashboardView.Warn,"Max warn border on default (Preset empty, Max 1); got "+(cell.BorderBrush==null?"null":cell.BorderBrush.ToString()));
    BulkUiTests.Check((cell.ToolTip as string)==Lang.T("Max is still 1: a leader order larger than this is skipped entirely"),"Max default tooltip");
    var r4=v.Rows[2];r4.Settings.Preset="Custom";r4.Settings.MaxContracts=4;r4.Changed();v.UpdateCards();BulkUiTests.Layout(v);
    v.RiskGrid.ScrollIntoView(r4,col);BulkUiTests.Layout(v);
    var cell4=CellOf(v.RiskGrid,r4,col);
    BulkUiTests.Check(cell4!=null&&cell4.BorderBrush!=DashboardView.Warn,"no warn border on Max 4 row");
  }
  static DataGridCell CellOf(DataGrid g,AccountRow row,DataGridColumn col){
    var content=col.GetCellContent(row);if(content==null)return null;
    for(DependencyObject p=content;p!=null;p=System.Windows.Media.VisualTreeHelper.GetParent(p)){var c=p as DataGridCell;if(c!=null)return c;}
    return null;
  }
  static void NoClippedTextNoWhiteBlocksAllLanguages(){
    string was=Lang.Current;
    try{
      foreach(var code in Lang.Codes){
        Lang.Set(code);var v=BulkUiTests.Fresh();
        var host=new Border{Child=v};host.Measure(new Size(1160,840));host.Arrange(new Rect(0,0,1160,840));host.UpdateLayout();
        System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(System.Windows.Threading.DispatcherPriority.ContextIdle,new Action(()=>{}));host.UpdateLayout();
        for(int tab=0;tab<2;tab++){
          v.Sections.SelectedIndex=tab;host.UpdateLayout();System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(System.Windows.Threading.DispatcherPriority.ContextIdle,new Action(()=>{}));host.UpdateLayout();
          int bad=0;string first="";
          Action<DependencyObject> walk=null;walk=d=>{var fe=d as FrameworkElement;if(fe!=null&&BulkUiTests.Truncated(fe)){bad++;if(first=="")first=((TextBlock)fe).Text;}int n=System.Windows.Media.VisualTreeHelper.GetChildrenCount(d);for(int i=0;i<n;i++)walk(System.Windows.Media.VisualTreeHelper.GetChild(d,i));};
          walk(v);
          BulkUiTests.Check(bad==0,code+" tab "+tab+" clipped text: '"+first+"'");
        }
        var v2=BulkUiTests.Fresh();
        v2.Sections.SelectedIndex=0;BulkUiTests.Layout(v2);
        var gb=BulkUiTests.Visual<CheckBox>(v2).FirstOrDefault(c=>(c.Content as string)==Lang.T("Group by firm"));
        BulkUiTests.Check(gb!=null&&BulkUiTests.WhitePx(gb)<40,code+": group-by-firm check box stays dark");
        v2.Sections.SelectedIndex=1;BulkUiTests.Layout(v2);
        var bf=Find<ComboBox>(v2,"bulkField");
        BulkUiTests.Check(bf!=null&&BulkUiTests.WhitePx(bf)<40,code+": bulk field combo stays dark");
        bf.SelectedIndex=DashboardView.BulkFieldIndex("ATM");BulkUiTests.Layout(v2);
        var bvc=Find<ComboBox>(v2,"bulkValueCombo");
        BulkUiTests.Check(bvc!=null&&BulkUiTests.WhitePx(bvc)<40,code+": bulk value combo stays dark");
      }
    } finally { Lang.Set(was); }
  }

 // Review fix (parent): grouping set in code left the "Group by firm" box unticked, so the screen showed groups with the box off.
 static void GroupToggleMirrorsGroupByAccount(){
  var v=BulkUiTests.Fresh();v.Sections.SelectedIndex=0;BulkUiTests.Layout(v);
  var box=BulkUiTests.Visual<CheckBox>(v).FirstOrDefault(c=>(c.Content as string)==Lang.T("Group by firm"));
  BulkUiTests.Check(box!=null,"Group by firm check box exists on the Monitor page");
  v.GroupByAccount=true;BulkUiTests.Check(box.IsChecked==true,"box ticks when grouping is turned on in code");
  v.GroupByAccount=false;BulkUiTests.Check(box.IsChecked==false,"box clears when grouping is turned off in code");
  box.IsChecked=true;BulkUiTests.Check(v.GroupByAccount,"ticking the box still groups");
  box.IsChecked=false;BulkUiTests.Check(!v.GroupByAccount,"unticking the box still ungroups");
 }

  // User request: Unlock all and the issue list move to their own tab so the Monitor table is not pushed down by many accounts.
  static void IssuesHaveTheirOwnTab(){
    var v=BulkUiTests.Fresh();
    BulkUiTests.Check(v.Sections.Items.Count==4,"four tabs: Monitor, Settings, Logs, Issues; got "+v.Sections.Items.Count);
    var issuesTab=(TabItem)v.Sections.Items[3];
    BulkUiTests.Check((issuesTab.Header as string)==Lang.T("Issues"),"tab 4 header is Issues with no issues: "+issuesTab.Header);
    var monitor=(FrameworkElement)((TabItem)v.Sections.Items[0]).Content;
    BulkUiTests.Check(!BulkUiTests.Visual<FrameworkElement>(monitor).Any(x=>(x.Tag as string)=="unlockAll"||(x.Tag as string)=="issues"||(x.Tag as string)=="banner"),"monitor tab has no Unlock all / issue list");
    var page=(FrameworkElement)issuesTab.Content;
    BulkUiTests.Check((page.Tag as string)=="banner","issues tab content is the issues page");
    for(int i=1;i<=8;i++){v.Rows[i].Latch.Locked=true;v.Rows[i].Latch.Reason="lock reason "+i;v.Rows[i].Changed();}
    v.Rows[0].Latch.Locked=true;v.Rows[0].Latch.Reason="leader lock (not listed)";v.Rows[0].Changed();
    v.UpdateCards();
    BulkUiTests.Check((issuesTab.Header as string)==Lang.T("Issues")+" (8)","header counts follower issues only: "+issuesTab.Header);
    v.Sections.SelectedIndex=3;BulkUiTests.Layout(v);
    int links=BulkUiTests.Visual<Button>(v).Count(b=>(b.Tag as string)=="issue:logs");
    BulkUiTests.Check(links==8,"all 8 issues listed (no '... and N more' cap now that it scrolls): "+links);
    v.Sections.SelectedIndex=0;BulkUiTests.Layout(v);
    var card=BulkUiTests.Visual<Border>(v).First(b=>(b.Tag as string)=="card:locked");
    card.RaiseEvent(new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice,0,System.Windows.Input.MouseButton.Left){RoutedEvent=UIElement.MouseLeftButtonUpEvent});
    BulkUiTests.Check(v.Sections.SelectedIndex==3,"Locked card opens the Issues tab; index "+v.Sections.SelectedIndex);
    foreach(var r in v.Rows){r.Latch.Locked=false;r.Latch.Reason="";r.Changed();}
    v.UpdateCards();
    BulkUiTests.Check((issuesTab.Header as string)==Lang.T("Issues"),"header resets when clear: "+issuesTab.Header);
  }

  // User request: the leader account is always listed first, on both tables, after any column sort and after the leader changes.
  static void LeaderRowIsAlwaysFirst(){
    var v=BulkUiTests.Fresh();
    foreach(var r in v.Rows)r.Role="Follower";
    var leader=v.Rows[17];leader.Role="Leader";
    v.ResortLeaderFirst();v.Sections.SelectedIndex=0;BulkUiTests.Layout(v);
    BulkUiTests.Check(v.AccountGrid.Items[0]==leader,"leader first on Monitor; got "+((AccountRow)v.AccountGrid.Items[0]).Name);
    BulkUiTests.Check(v.RiskGrid.Items[0]==leader,"leader first on Settings");
    foreach(var key in new[]{"Acct","P/L"}){
      var col=v.AccountGrid.Columns.First(c=>(c.Header as string)==Lang.T(key));
      for(int k=0;k<2;k++){
        var args=new DataGridSortingEventArgs(col);
        var m=typeof(DataGrid).GetMethod("OnSorting",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);m.Invoke(v.AccountGrid,new object[]{args});
        BulkUiTests.Layout(v);
        BulkUiTests.Check(v.AccountGrid.Items[0]==leader,"leader first after sorting "+key+" #"+k+"; got "+((AccountRow)v.AccountGrid.Items[0]).Name);
        BulkUiTests.Check(col.SortDirection==(k==0?System.ComponentModel.ListSortDirection.Ascending:System.ComponentModel.ListSortDirection.Descending),"sort direction toggles on "+key);
      }
    }
    var names=v.AccountGrid.Items.Cast<AccountRow>().Skip(1).Select(r=>r.Pnl).ToList();
    var sorted=names.OrderByDescending(x=>x).ToList();
    BulkUiTests.Check(names.SequenceEqual(sorted),"followers still sorted by the clicked column (P/L descending)");
    leader.Role="Follower";var next=v.Rows[5];next.Role="Leader";v.ResortLeaderFirst();BulkUiTests.Layout(v);
    BulkUiTests.Check(v.AccountGrid.Items[0]==next,"new leader moves to the top after the leader changes");
    BulkUiTests.Check(v.Rows.Count==30&&v.AccountGrid.Items.Count==30,"no rows lost");
  }

  // User request: bulk edit can change Ratio and Max contracts too; still only highlighted followers, only while stopped, never the follow tick.
  static void BulkEditsRatioAndMax(){
    var v=BulkUiTests.Fresh();v.Sections.SelectedIndex=1;BulkUiTests.Layout(v);
    var field=Find<ComboBox>(v,"bulkField");var value=Find<TextBox>(v,"bulkValue");var apply=Find<Button>(v,"bulkApply");
    BulkUiTests.Check(field.SelectedIndex==0&&(field.SelectedItem as string)==Lang.T("Ratio"),"Ratio is the first bulk field: "+field.SelectedItem);
    BulkUiTests.Check((field.Items[1] as string)==Lang.T("Max contracts"),"Max contracts is second");
    v.RiskGrid.UnselectAll();v.RiskGrid.SelectedItems.Add(v.Rows[0]);v.RiskGrid.SelectedItems.Add(v.Rows[1]);v.RiskGrid.SelectedItems.Add(v.Rows[2]);
    bool[] ticks=v.Rows.Select(r=>r.Enabled).ToArray();double leaderRatio=v.Rows[0].Ratio;int leaderMax=v.Rows[0].MaxContracts;
    value.Text="0.5";apply.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
    BulkUiTests.Check(v.Rows[1].Ratio==0.5&&v.Rows[2].Ratio==0.5&&v.Rows[3].Ratio!=0.5,"Ratio 0.5 on highlighted followers only");
    BulkUiTests.Check(v.Rows[0].Ratio==leaderRatio,"leader Ratio untouched");
    field.SelectedIndex=DashboardView.BulkFieldIndex("Max");BulkUiTests.Layout(v);
    value.Text="7";apply.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
    BulkUiTests.Check(v.Rows[1].MaxContracts==7&&v.Rows[2].MaxContracts==7&&v.Rows[3].MaxContracts!=7,"Max 7 on highlighted followers only");
    BulkUiTests.Check(v.Rows[0].MaxContracts==leaderMax,"leader Max untouched");
    foreach(var bad in new[]{"0","-1","1,5","abc","",double.NaN.ToString(),"1e400"}){string r=v.BulkApply("Ratio",bad);BulkUiTests.Check(r!=null,"Ratio '"+bad+"' refused");}
    foreach(var bad in new[]{"0","-2","1.5","x",""}){string r=v.BulkApply("Max",bad);BulkUiTests.Check(r!=null,"Max '"+bad+"' refused");}
    BulkUiTests.Check(v.Rows[1].Ratio==0.5&&v.Rows[1].MaxContracts==7,"refused values change nothing");
    BulkUiTests.Check(v.Rows.Select(r=>r.Enabled).SequenceEqual(ticks),"follow ticks unchanged");
    v.Editable(false);
    BulkUiTests.Check(v.BulkApply("Ratio","2")!=null&&v.BulkApply("Max","9")!=null,"refused while attached");
    BulkUiTests.Check(v.Rows[1].Ratio==0.5&&v.Rows[1].MaxContracts==7,"unchanged while attached");
  }
  // User request: columns ordered by importance instead of the order they were added in.
  static void ColumnsFollowImportanceOrder(){
    var v=BulkUiTests.Fresh();
    var copy=v.AccountGrid.Columns.Select(DashboardView.ColumnKey).ToArray();
    var risk=v.RiskGrid.Columns.Select(DashboardView.ColumnKey).ToArray();
    BulkUiTests.Check(copy.SequenceEqual(DashboardView.CopyColumnOrder),"monitor order: "+string.Join(",",copy));
    BulkUiTests.Check(risk.Take(DashboardView.RiskColumnOrder.Length).SequenceEqual(DashboardView.RiskColumnOrder),"settings order: "+string.Join(",",risk));
    BulkUiTests.Check(risk.Length==DashboardView.RiskColumnOrder.Length+1,"only the row-settings button follows, at the far right: "+risk.Length);
    BulkUiTests.Check(v.AccountGrid.FrozenColumnCount==2&&copy[0]=="On"&&copy[1]=="Acct","tick and account stay frozen on the left");
    BulkUiTests.Check(copy[copy.Length-1]=="Flat"&&copy[copy.Length-2]=="Off","row actions stay at the far right");
  }

  // Real NT (1160 x 840): the last column (Flat buttons) was cut off and a horizontal scrollbar showed. Every language must fit with no scrollbar.
  static void MonitorTableFitsDefaultWindowInEveryLanguage(){
    var was=Lang.Current;
    try{
      foreach(var code in Lang.Codes){
        Lang.Set(code);
        var v=BulkUiTests.Fresh();v.Sections.SelectedIndex=0;
        var host=new Border{Child=v};host.Measure(new Size(1160,840));host.Arrange(new Rect(0,0,1160,840));host.UpdateLayout();
        System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(System.Windows.Threading.DispatcherPriority.ContextIdle,new Action(()=>{}));host.UpdateLayout();
        var sv=BulkUiTests.Visual<ScrollViewer>(v.AccountGrid).FirstOrDefault();
        BulkUiTests.Check(sv!=null,code+": grid scroll viewer");
        double cols=v.AccountGrid.Columns.Where(c=>c.Visibility==Visibility.Visible).Sum(c=>c.ActualWidth);
        // Review 3: also prove the spare width is handed out (identity columns wider than their minimum), not left empty on the right.
        var acct=v.AccountGrid.Columns.First(c=>DashboardView.ColumnKey(c)=="Acct");var alias=v.AccountGrid.Columns.First(c=>DashboardView.ColumnKey(c)=="Alias");
        BulkUiTests.Check(acct.ActualWidth>acct.MinWidth+5&&alias.ActualWidth>alias.MinWidth+5,code+": spare width goes to Acct / Alias ("+Math.Round(acct.ActualWidth)+"/"+acct.MinWidth+", "+Math.Round(alias.ActualWidth)+"/"+alias.MinWidth+")");
        BulkUiTests.Check(sv.ViewportWidth-sv.ExtentWidth<30,code+": no more than 30 px left empty at the right ("+Math.Round(sv.ViewportWidth-sv.ExtentWidth)+")");
        BulkUiTests.Check(sv.ExtentWidth<=sv.ViewportWidth+0.5,code+": monitor table needs a horizontal scrollbar (extent "+Math.Round(sv.ExtentWidth)+" > viewport "+Math.Round(sv.ViewportWidth)+", columns "+Math.Round(cols)+")");
      }
    } finally { Lang.Set(was); }
  }

  // Real NT sweep: the Settings table showed "Disarmed" / "Connected" in English while the rest of the window was Chinese / Japanese / Korean.
  static void SettingsStatusAndConnectionAreTranslated(){
    var was=Lang.Current;
    try{
      foreach(var code in new[]{"zh","ja","ko"}){
        Lang.Set(code);
        var v=BulkUiTests.Fresh();v.Sections.SelectedIndex=1;
        v.Rows[1].Status="Disarmed";v.Rows[1].Connection="Connected";v.Rows[1].Changed();
        BulkUiTests.Layout(v);
        var shown=BulkUiTests.Visual<TextBlock>(v.RiskGrid).Select(x=>x.Text).ToList();
        BulkUiTests.Check(shown.Contains(Lang.T("Disarmed"))&&Lang.T("Disarmed")!="Disarmed",code+": status shown translated");
        BulkUiTests.Check(shown.Contains(Lang.T("Connected"))&&Lang.T("Connected")!="Connected",code+": connection shown translated");
        BulkUiTests.Check(!shown.Contains("Disarmed")&&!shown.Contains("Connected"),code+": no English status / connection in the Settings table");
        BulkUiTests.Check(v.Rows[1].Status=="Disarmed"&&v.Rows[1].Connection=="Connected","underlying codes stay English for logic");
      }
    } finally { Lang.Set(was); }
  }

  // Review 2: Max (Settings) and Bal (Monitor) headers toggled their arrow but never reordered (sort path pointed at no property).
  static void MaxAndBalanceHeadersSort(){
    var v=BulkUiTests.Fresh();
    for(int i=1;i<v.Rows.Count;i++){v.Rows[i].MaxContracts=(i*7)%13+1;v.Rows[i].CashBalance=(i*37)%101*100.0;}
    var m=typeof(DataGrid).GetMethod("OnSorting",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
    var maxCol=v.RiskGrid.Columns.First(c=>DashboardView.ColumnKey(c)=="Max");
    m.Invoke(v.RiskGrid,new object[]{new DataGridSortingEventArgs(maxCol)});BulkUiTests.Layout(v);
    var maxes=v.RiskGrid.Items.Cast<AccountRow>().Skip(1).Select(r=>r.MaxContracts).ToList();
    BulkUiTests.Check(maxes.SequenceEqual(maxes.OrderBy(x=>x)),"Max header sorts followers ascending: "+string.Join(",",maxes));
    BulkUiTests.Check(v.RiskGrid.Items[0]==v.Rows.First(r=>r.Role=="Leader"),"leader stays first");
    var balCol=v.AccountGrid.Columns.First(c=>DashboardView.ColumnKey(c)=="Bal");
    m.Invoke(v.AccountGrid,new object[]{new DataGridSortingEventArgs(balCol)});BulkUiTests.Layout(v);
    var bals=v.AccountGrid.Items.Cast<AccountRow>().Skip(1).Select(r=>r.CashBalance).ToList();
    BulkUiTests.Check(bals.SequenceEqual(bals.OrderBy(x=>x)),"Bal header sorts followers ascending");
  }
  // Review 2: the issue list was rebuilt (new buttons and templates) on every UpdateCards call, i.e. every second, even when nothing changed.
  static void IssuesListNotRebuiltWhenUnchanged(){
    var v=BulkUiTests.Fresh();
    v.Rows[1].Latch.Locked=true;v.Rows[1].Latch.Reason="lock A";v.Rows[1].Changed();
    v.UpdateCards();
    var panel=BulkUiTests.Visual<StackPanel>((FrameworkElement)((TabItem)v.Sections.Items[3]).Content).FirstOrDefault(x=>(x.Tag as string)=="issues");
    v.Sections.SelectedIndex=3;BulkUiTests.Layout(v);
    panel=BulkUiTests.Visual<StackPanel>(v).First(x=>(x.Tag as string)=="issues");
    var first=panel.Children.Cast<UIElement>().ToArray();
    for(int i=0;i<5;i++)v.UpdateCards();
    BulkUiTests.Check(panel.Children.Cast<UIElement>().SequenceEqual(first),"unchanged issues keep the same elements (no rebuild)");
    v.Rows[2].Latch.Locked=true;v.Rows[2].Latch.Reason="lock B";v.Rows[2].Changed();v.UpdateCards();
    BulkUiTests.Check(panel.Children.Count>first.Length,"a new lock rebuilds the list");
    v.Rows[2].Latch.Reason="lock B changed";v.UpdateCards();BulkUiTests.Layout(v);
    var texts=BulkUiTests.Visual<TextBlock>(panel).Select(x=>x.Text).ToList();
    BulkUiTests.Check(texts.Any(x=>x.Contains("lock B changed")),"a changed reason is shown");
  }

  // Review 3: one currency table (AccountRow.CurrencyCode reads MonitorSummary.IsoCode); a row reorder with the same issues does not rebuild.
  static void CurrencyCodeUsesSharedTableAndIssueOrderIgnored(){
    BulkUiTests.Check(new AccountRow{Currency="SwissFranc"}.CurrencyCode=="CHF","SwissFranc -> CHF");
    BulkUiTests.Check(new AccountRow{Currency=""}.CurrencyCode=="CCY?","blank -> CCY?");
    BulkUiTests.Check(new AccountRow{Currency="XYZ"}.CurrencyCode=="XYZ","unknown kept");
    var v=BulkUiTests.Fresh();
    v.Rows[1].Latch.Locked=true;v.Rows[1].Latch.Reason="lock A";v.Rows[2].Latch.Locked=true;v.Rows[2].Latch.Reason="lock B";
    v.UpdateCards();v.Sections.SelectedIndex=3;BulkUiTests.Layout(v);
    var panel=BulkUiTests.Visual<StackPanel>(v).First(x=>(x.Tag as string)=="issues");
    var first=panel.Children.Cast<UIElement>().ToArray();
    v.Rows.Move(2,1);v.UpdateCards();
    BulkUiTests.Check(panel.Children.Cast<UIElement>().SequenceEqual(first),"reordering rows with the same issues does not rebuild the list");
  }

  // Email alerts section (Settings): edits a draft only; Save / Send test hand the draft to the tab callbacks; never touches rows, ticks or commands.
  static void NotifySectionEditsDraftOnly(){
    var v=BulkUiTests.Fresh();v.Sections.SelectedIndex=1;BulkUiTests.Layout(v);
    BulkUiTests.Check(v.NotifyToBox!=null&&v.NotifyKindBoxes.Count==NotifyPrefs.Kinds.Length,"address box and one tick per alert kind");
    BulkUiTests.Check(v.NotifyToBox.Text==""&&v.NotifyKindBoxes["lock"].IsChecked==true&&v.NotifyKindBoxes["boundary"].IsChecked!=true,"defaults shown: no address, lock on, boundary off");
    NotifyPrefs.Settings saved=null,tested=null;var cmds=new System.Collections.Generic.List<string>();
    v.NotifySave=x=>saved=x;v.NotifyTest=x=>tested=x;v.Command=c=>cmds.Add(c);
    bool[] ticks=v.Rows.Select(r=>r.Enabled).ToArray();
    v.NotifyToBox.Text=" me@example.com ";v.NotifyKindBoxes["error"].IsChecked=false;v.NotifyKindBoxes["boundary"].IsChecked=true;
    var save=BulkUiTests.Visual<Button>(v).First(b=>(b.Tag as string)=="notifySave");var test=BulkUiTests.Visual<Button>(v).First(b=>(b.Tag as string)=="notifyTest");
    save.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
    BulkUiTests.Check(saved!=null&&saved.To=="me@example.com"&&saved.On.Contains("boundary")&&!saved.On.Contains("error")&&saved.On.Contains("lock"),"Save hands the trimmed draft to the tab");
    test.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
    BulkUiTests.Check(tested!=null&&tested.To=="me@example.com","Send test hands the draft to the tab");
    BulkUiTests.Check(cmds.Count==0,"no dashboard command sent");
    BulkUiTests.Check(v.Rows.Select(r=>r.Enabled).SequenceEqual(ticks),"follow ticks unchanged");
    v.Editable(false);BulkUiTests.Layout(v);
    BulkUiTests.Check(save.IsEnabled&&test.IsEnabled&&v.NotifyToBox.IsEnabled,"alert settings stay editable while attached (they never touch orders)");
    var p=NotifyPrefs.Parse(new[]{"to=x@y.com","on=boundary"});v.ShowNotify(p);
    BulkUiTests.Check(v.NotifyToBox.Text=="x@y.com"&&v.NotifyKindBoxes["boundary"].IsChecked==true&&v.NotifyKindBoxes["lock"].IsChecked==false,"ShowNotify reflects loaded settings");
  }

  // S1 controls: Resume shows only when the engine reports a resumable pause; Re-follow shows a count and needs an attached session;
  // Auto re-follow column binds to RiskSettings.AutoRefollow (default off). Pressing them only sends the command.
  static void ResumeAndRefollowControls(){
    var v=new DashboardView(false);v.Editable(true);BulkUiTests.Layout(v);var cmds=new System.Collections.Generic.List<string>();v.Command=c=>cmds.Add(c); // non-demo: the demo view never forwards
    BulkUiTests.Check(v.ResumeButton!=null&&v.ResumeButton.Visibility!=Visibility.Visible,"resume hidden by default");
    bool canResume=DashboardSafety.CanExecute("resume");
    v.ShowResume(true);BulkUiTests.Layout(v);BulkUiTests.Check(v.ResumeButton.Visibility==Visibility.Visible&&v.ResumeButton.IsEnabled==canResume,"resume shown while resumable; enabled only where the build allows it");
    v.ResumeButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
    BulkUiTests.Check(canResume?(cmds.Count==1&&cmds[0]=="resume"):cmds.Count==0,"resume sends only the resume command (none under the review hold)");
    cmds.Clear();cmds.Add("resume");
    v.ShowResume(false);BulkUiTests.Check(v.ResumeButton.Visibility!=Visibility.Visible,"hidden again");
    BulkUiTests.Check(v.RefollowButton!=null&&!v.RefollowButton.IsEnabled,"re-follow disabled with nothing closed");
    v.ShowRefollow(2);BulkUiTests.Check(!v.RefollowButton.IsEnabled,"re-follow needs an attached session");
    v.SetSessionState(true,"COPYING");v.ShowRefollow(2);
    BulkUiTests.Check(v.RefollowButton.IsEnabled&&(v.RefollowButton.Content as string??"").Contains("2"),"re-follow enabled with count");
    v.RefollowButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
    BulkUiTests.Check(cmds.Count==2&&cmds[1]=="refollow","re-follow sends only its command; got ["+string.Join(",",cmds)+"]");
    v.ShowRefollow(0);BulkUiTests.Check(!v.RefollowButton.IsEnabled,"re-follow disabled at 0");
    v.SetSessionState(false,"Stopped");
    BulkUiTests.Check(v.RiskGrid.Columns.Any(c=>c.SortMemberPath=="AutoRefollow"),"auto re-follow column present");
    var row=new AccountRow{Name="X",Role="Follower"};BulkUiTests.Check(!row.AutoRefollow,"auto re-follow default off");row.AutoRefollow=true;BulkUiTests.Check(row.Settings.AutoRefollow,"binds to settings");
  }

  // Real NT 2026-10-17: at the default 1160 px window the Settings table scrolled sideways (UIA HorizontalViewSize 96.9 %) with the
  // default columns. The Monitor test above never covered the Settings grid.
  static void SettingsTableFitsDefaultWindowInEveryLanguage(){
    var was=Lang.Current;
    try{
      foreach(var code in Lang.Codes){
        Lang.Set(code);
        var v=BulkUiTests.Fresh();v.ApplyColumns(ColumnPrefs.Defaults());v.Sections.SelectedIndex=1;
        var host=new Border{Child=v};host.Measure(new Size(1160,840));host.Arrange(new Rect(0,0,1160,840));host.UpdateLayout();
        System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(System.Windows.Threading.DispatcherPriority.ContextIdle,new Action(()=>{}));host.UpdateLayout();
        var sv=BulkUiTests.Visual<ScrollViewer>(v.RiskGrid).FirstOrDefault();
        BulkUiTests.Check(sv!=null,code+": settings grid scroll viewer");
        // 10 px of slack: real NT laid the same default columns out a few px wider than this host and still scrolled (99.5 %).
        BulkUiTests.Check(sv.ExtentWidth<=sv.ViewportWidth-10,code+": settings table needs a horizontal scrollbar on real NT (extent "+Math.Round(sv.ExtentWidth)+" > viewport "+Math.Round(sv.ViewportWidth)+" - 10)");
      }
    } finally { Lang.Set(was); }
  }
}
