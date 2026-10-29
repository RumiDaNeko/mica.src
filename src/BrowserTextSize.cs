using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Mica {
 // Text size, for anyone who finds Mica's text too small: the sidebar, toolbar, menus, dialogs and island are
 // drawn larger, and pages without a zoom of their own open at the same size. Per profile, like the theme.
 partial class BrowserApp {
  internal static readonly double[] TextSizes={1,1.15,1.3};
  internal static readonly string[] TextSizeNames={"Default","Large","Larger"};
  // One transform shared by everything that grows, so a change applies everywhere at once.
  readonly ScaleTransform uiScale=new ScaleTransform(1,1);
  internal double UiScale {get {return state.TextScale>1?Math.Min(1.5,state.TextScale):1;}}
  // What "100%" means for a page: the chosen text size.
  internal double PageBase {get {return UiScale;}}

  // A layout transform makes WPF measure its whole subtree through it on every layout pass (each frame of the
  // sidebar animation, for one), even when it scales by 1. So parts only carry it while text is larger.
  readonly System.Collections.Generic.List<WeakReference> scaledParts=new System.Collections.Generic.List<WeakReference>();
  void Scaled(FrameworkElement part) {scaledParts.Add(new WeakReference(part));ScaleNow(part);}
  void ScaleNow(FrameworkElement part) {if(part==null)return;if(UiScale>1){if(part.LayoutTransform!=uiScale)part.LayoutTransform=uiScale;}else if(part.ReadLocalValue(FrameworkElement.LayoutTransformProperty)!=DependencyProperty.UnsetValue)part.ClearValue(FrameworkElement.LayoutTransformProperty);}
  void ApplyTextScale() {
   double scale=UiScale;uiScale.ScaleX=scale;uiScale.ScaleY=scale;
   foreach(var name in new[]{"Sidebar","ToolbarBar","Home"})ScaleNow(Find<FrameworkElement>(name));
   scaledParts.RemoveAll(w=>!w.IsAlive);foreach(var w in scaledParts.ToArray())ScaleNow(w.Target as FrameworkElement);
   Find<Grid>("AppLayout").SetValue(MotionProperties.SidebarScaleProperty,scale);
   ScaleNow(islandCard);
   // Pages that you haven't zoomed yourself follow the new size.
   foreach(var view in views.Values.ToList()) {
    var core=LiveCore(view);if(core==null)continue;string site=SiteFor(core.Source);double own;
    if(site!=null&&state.SiteZoom!=null&&state.SiteZoom.TryGetValue(site,out own))continue;
    SetViewZoom(view,scale);
   }
   UpdateZoomBadge();PlaceIsland();
  }
  internal void SetTextScale(double scale) {
   foreach(var browser in Program.Browsers.ToArray())if(!browser.closing&&browser.profile==profile){browser.state.TextScale=scale;browser.ApplyTextScale();browser.Save();}
  }
 }
}
