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
    GroupToggleMirrorsGroupByAccount
  };}
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
    var v=new DashboardView(false);v.Editable(true);v.Sections.SelectedIndex=0;BulkUiTests.Layout(v);
    var banner=Tag(v,"banner");
    var unlock=Find<Button>(v,"unlockAll");
    BulkUiTests.Check(banner!=null&&unlock!=null,"banner and unlockAll exist");
    // unlockAll is inside the banner host
    bool inside=false;for(var a=(DependencyObject)unlock;a!=null;a=System.Windows.Media.VisualTreeHelper.GetParent(a))if(a==banner){inside=true;break;}
    BulkUiTests.Check(inside,"unlockAll lives inside the banner");
    var fs=Find<Button>(v,"flattenSelected");var fa=Find<Button>(v,"flattenAll");
    BulkUiTests.Check(fs!=null&&fa!=null,"flatten buttons live on the monitor tab");
    BulkUiTests.Check(fs.IsEnabled==DashboardSafety.CanExecute("flattenSelected"),"flattenSelected follows CanExecute");
    BulkUiTests.Check(fa.IsEnabled==DashboardSafety.CanExecute("flattenAll"),"flattenAll follows CanExecute");
  }
  static void BulkSectionAppliesOnSettingsTab(){
    var v=BulkUiTests.Fresh();v.Sections.SelectedIndex=1;BulkUiTests.Layout(v);
    v.RiskGrid.UnselectAll();v.RiskGrid.SelectedItems.Add(v.Rows[1]);v.RiskGrid.SelectedItems.Add(v.Rows[2]);
    var field=Find<ComboBox>(v,"bulkField");var value=Find<TextBox>(v,"bulkValue");var apply=Find<Button>(v,"bulkApply");
    BulkUiTests.Check(field!=null&&value!=null&&apply!=null,"bulk controls exist on settings tab");
    field.SelectedIndex=0;BulkUiTests.Layout(v);
    value.Text="750";BulkUiTests.Layout(v);
    apply.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
    BulkUiTests.Check(v.Rows[1].Target==750&&v.Rows[2].Target==750&&v.Rows[3].Target==1000,"Daily target applied to highlighted rows only");
    double ratioBefore=v.Rows[1].Ratio;
    field.SelectedIndex=2;BulkUiTests.Layout(v);var combo=Find<ComboBox>(v,"bulkValueCombo");combo.SelectedIndex=1;BulkUiTests.Layout(v);
    apply.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
    BulkUiTests.Check(!v.Rows[1].CopyAtm&&!v.Rows[2].CopyAtm,"ATM exit mirroring off applied");
    field.SelectedIndex=4;BulkUiTests.Layout(v);combo=Find<ComboBox>(v,"bulkValueCombo");combo.SelectedIndex=1;BulkUiTests.Layout(v);
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
        bf.SelectedIndex=2;BulkUiTests.Layout(v2);
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
}
