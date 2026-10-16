using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using NinjaTrader.NinjaScript.AddOns.Kinfx;
public static class UiTestsWp1 {
 public static Action[] Cases(){ return new Action[]{ PickerExistsAndListsRoots, PickerSetsFrontContract, PickerDisabledWhenAttached, StateBadgeReadsStoppedCopyingPaused, UseFrontShowsOnlyWhenStale, PickerListIsDark, HeaderTextNotClippedInEveryLanguage, PickerRowsDoNotInheritIconFont }; }
 static Button FindTag(DependencyObject root,string tag){ return BulkUiTests.Visual<Button>(root).FirstOrDefault(b=>(b.Tag as string)==tag); }
 // A Popup renders in its own window, not inside the DashboardView, so its child is not in the visual tree off-screen.
 // Host the list inside a DashboardView Border so the real rows render and are measurable.
 static Border OpenAndHost(DashboardView v){
  var pick=FindTag(v,"instPick");BulkUiTests.Check(pick!=null,"instPick button exists");
  pick.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
  var list=v.InstrumentList;BulkUiTests.Check(list!=null,"picker list built");
  if(v.InstrumentPopup!=null)v.InstrumentPopup.Child=null;
  var host=new Border{Child=list,Width=440,Height=460,HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Top,Margin=new Thickness(0,90,0,0)};
  ((Grid)v).Children.Add(host);BulkUiTests.Layout(v);
  return list;
 }
 static void PickerExistsAndListsRoots() {
  var v=BulkUiTests.Fresh();
  var list=OpenAndHost(v);
  var roots=new System.Collections.Generic.HashSet<string>();
  foreach(var b in BulkUiTests.Visual<Button>(list))if(b.Tag is string)roots.Add((string)b.Tag);
  BulkUiTests.Check(roots.Count>=14,"picker lists at least 14 roots; got "+roots.Count);
 }
 static void PickerSetsFrontContract() {
  var old=InstrumentCatalog.Now;InstrumentCatalog.Now=()=>new DateTime(2026,12,13);
  try {
   var v=BulkUiTests.Fresh();
   var list=OpenAndHost(v);
   var mnq=FindTag(list,"MNQ");BulkUiTests.Check(mnq!=null,"MNQ row exists in the picker");
   mnq.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
   BulkUiTests.Check(v.InstrumentBox.Text=="MNQ 12-26","picking MNQ writes the front contract; got "+v.InstrumentBox.Text);
  } finally { InstrumentCatalog.Now=old; }
 }
 static void PickerDisabledWhenAttached() {
  var v=BulkUiTests.Fresh();
  var pick=FindTag(v,"instPick");
  BulkUiTests.Check(pick.IsEnabled,"enabled while editable");
  v.Editable(false);BulkUiTests.Check(!pick.IsEnabled,"disabled after Editable(false)");
  v.Editable(true);BulkUiTests.Check(pick.IsEnabled,"enabled again after Editable(true)");
 }
 static void StateBadgeReadsStoppedCopyingPaused() {
  var v=BulkUiTests.Fresh();
  var badge=BulkUiTests.Visual<TextBlock>(v).FirstOrDefault(t=>(t.Tag as string)=="stateBadge");
  BulkUiTests.Check(badge!=null,"state badge exists");
  BulkUiTests.Check(badge.Text==Lang.T("Stopped"),"fresh view is Stopped; got "+badge.Text);
  foreach(var r in v.Rows)r.Paused=false;
  v.SetSessionState(true,"x");
  BulkUiTests.Check(badge.Text==Lang.T("Copying"),"attached + no paused rows is Copying; got "+badge.Text);
  v.Rows[1].Paused=true;v.UpdateCards();
  BulkUiTests.Check(badge.Text==Lang.T("Paused"),"a paused row reads Paused; got "+badge.Text);
 }
 static void UseFrontShowsOnlyWhenStale() {
  var old=InstrumentCatalog.Now;InstrumentCatalog.Now=()=>new DateTime(2026,12,13);
  try {
   var v=BulkUiTests.Fresh();
   var use=FindTag(v,"useFront");BulkUiTests.Check(use!=null,"useFront tool exists");
   BulkUiTests.Check(use.Visibility==Visibility.Collapsed,"hidden when the box already holds the front");
   v.InstrumentBox.Text="MNQ 09-26";v.UpdateCards();
   BulkUiTests.Check(use.Visibility==Visibility.Visible,"shows when the typed contract is stale");
   v.InstrumentBox.Text="MNQ 12-26";v.UpdateCards();
   BulkUiTests.Check(use.Visibility==Visibility.Collapsed,"hidden again when the box is the front");
  } finally { InstrumentCatalog.Now=old; }
 }
 static void PickerListIsDark() {
  var v=BulkUiTests.Fresh();
  var list=OpenAndHost(v);
  int white=BulkUiTests.WhitePx(list);
  BulkUiTests.Check(white<40,"open picker list must be dark; near-white px = "+white);
 }
 static void HeaderTextNotClippedInEveryLanguage() {
  var was=Lang.Current;
  try {
   foreach(var code in Lang.Codes){
    Lang.Set(code);
    var v=BulkUiTests.Fresh();
    var host=new Border{Child=v};host.Measure(new Size(1160,840));host.Arrange(new Rect(0,0,1160,840));host.UpdateLayout();
    System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(System.Windows.Threading.DispatcherPriority.ContextIdle,new Action(()=>{}));host.UpdateLayout();
    int bad=0;string first="";
    Action<DependencyObject> walk=null;walk=d=>{var fe=d as FrameworkElement;if(fe!=null&&BulkUiTests.Truncated(fe)){bad++;if(first=="")first=((TextBlock)fe).Text;}int n=System.Windows.Media.VisualTreeHelper.GetChildrenCount(d);for(int i=0;i<n;i++)walk(System.Windows.Media.VisualTreeHelper.GetChild(d,i));};
    foreach(var child in ((Grid)v).Children.OfType<UIElement>()){int row=Grid.GetRow(child);if(row==0||row==1)walk(child);}
    BulkUiTests.Check(bad==0,code+": clipped header text, first: '"+first+"'");
   }
  } finally { Lang.Set(was); }
 }

 // Found on real NinjaTrader (parent review): the Popup inherited the picker button's icon font, so every row drew as boxes.
 // Checked on the popup in place (not re-hosted inside the DashboardView, which would hide the inheritance).
 static void PickerRowsDoNotInheritIconFont(){
  var v=BulkUiTests.Fresh();BulkUiTests.Layout(v);
  var btn=FindTag(v,"instPick");
  btn.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
  var list=v.InstrumentList;BulkUiTests.Check(list!=null,"picker list built by the button");
  if(v.InstrumentPopup!=null){v.InstrumentPopup.IsOpen=false;v.InstrumentPopup.Child=null;}
  // On real NT the popup root inherits from its PlacementTarget (the icon button). Reproduce that parent explicitly.
  var iconParent=new Border{Child=list};iconParent.SetValue(System.Windows.Documents.TextElement.FontFamilyProperty,btn.FontFamily);
  BulkUiTests.Check(btn.FontFamily.Source.IndexOf("Icons",StringComparison.OrdinalIgnoreCase)>=0,"picker button uses the icon font: "+btn.FontFamily.Source);
  BulkUiTests.Layout(iconParent);
  var blocks=BulkUiTests.Visual<TextBlock>(iconParent).Where(t=>!string.IsNullOrEmpty(t.Text)).ToList();
  BulkUiTests.Check(blocks.Count>=14,"picker text blocks: "+blocks.Count);
  foreach(var tb in blocks){string fam=tb.FontFamily.Source;BulkUiTests.Check(fam.IndexOf("Icons",StringComparison.OrdinalIgnoreCase)<0&&fam.IndexOf("MDL2",StringComparison.OrdinalIgnoreCase)<0,"picker text '"+tb.Text+"' uses icon font "+fam);}
 }
}
