using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;

namespace Mica {
 // Text that changes like an odometer, as on Windows (BrowserTicker.cs RollText): when only digits change they roll,
 // rightmost first, up when the number grows and down when it shrinks; any other change slides out and back in.
 sealed class Ticker : StackPanel {
  string text="";int generation;
  public double FontSizeValue=11;public FontFamily Family;public string Tone="Muted";
  public Ticker() {Orientation=Orientation.Horizontal;VerticalAlignment=VerticalAlignment.Center;}
  public string Text {get {return text;} set {Set(value??"");}}
  TextBlock Glyph(string s) {
   var t=new TextBlock {Text=s,FontSize=FontSizeValue,VerticalAlignment=VerticalAlignment.Center};if(Family!=null)t.FontFamily=Family;
   t.Bind(TextBlock.ForegroundProperty,t.GetResourceObservable(Tone));return t;
  }
  void Plain(string s) {Children.Clear();RenderTransform=null;Opacity=1;Children.Add(Glyph(s));}
  static bool DigitsOnly(string a,string b) {
   if(a.Length!=b.Length)return false;
   for(int i=0;i<a.Length;i++)if(a[i]!=b[i]&&!((char.IsDigit(a[i])||a[i]==' ')&&(char.IsDigit(b[i])||b[i]==' ')))return false;
   return true;
  }
  // A transition from one value to another after a delay (keyframe animations can't drive a transform).
  static async Task Run(Animatable target,AvaloniaProperty property,double from,double to,int ms,int delay,Easing easing) {
   target.Transitions=null;target.SetValue(property,from);
   if(delay>0)await Task.Delay(delay);else await Task.Yield();
   target.Transitions=new Transitions {new DoubleTransition {Property=property,Duration=TimeSpan.FromMilliseconds(ms),Easing=easing??new LinearEasing()}};
   target.SetValue(property,to);
   await Task.Delay(ms);
  }
  async void Set(string next) {
   string old=text;text=next;int mine=++generation;
   if(old.Length==0||old==next||!IsAttachedToVisualTree()){Plain(next);return;}
   int width=Math.Max(old.Length,next.Length);string a=old.PadLeft(width),b=next.PadLeft(width);
   if(DigitsOnly(a,b)) {
    Children.Clear();RenderTransform=null;Opacity=1;
    double travel=FontSizeValue*1.25;bool up=Number(next)>=Number(old);int changed=0;
    var tasks=new System.Collections.Generic.List<Task>();
    for(int i=width-1;i>=0;i--) {
     if(a[i]==b[i]){Children.Insert(0,Glyph(b[i].ToString()));continue;}
     int delay=45*changed++;
     var cell=new Grid {ClipToBounds=true,Height=FontSizeValue*1.4,VerticalAlignment=VerticalAlignment.Center};
     var leaving=Glyph(a[i].ToString());var coming=Glyph(b[i].ToString());
     var lt=new TranslateTransform();var ct=new TranslateTransform(0,up?travel:-travel);leaving.RenderTransform=lt;coming.RenderTransform=ct;
     cell.Children.Add(leaving);cell.Children.Add(coming);Children.Insert(0,cell);
     var ease=new CubicEaseOut();
     tasks.Add(Run(lt,TranslateTransform.YProperty,0,up?-travel:travel,380,delay,ease));
     tasks.Add(Run(leaving,OpacityProperty,1,0,380,delay,ease));
     tasks.Add(Run(ct,TranslateTransform.YProperty,up?travel:-travel,0,380,delay,ease));
    }
    await Task.WhenAll(tasks);
    if(mine==generation)Plain(next);
    return;
   }
   // Anything else slides: out up 5 px in 90 ms, then in from 5 px below.
   var move=new TranslateTransform();RenderTransform=move;
   await Task.WhenAll(Run(this,OpacityProperty,1,0,90,0,null),Run(move,TranslateTransform.YProperty,0,-5,90,0,null));
   if(mine!=generation)return;
   Children.Clear();Children.Add(Glyph(next));
   await Task.WhenAll(Run(this,OpacityProperty,0,1,160,0,null),Run(move,TranslateTransform.YProperty,5,0,200,0,new CubicEaseOut()));
   if(mine==generation)RenderTransform=null;
  }
  static double Number(string s) {var digits=new string(s.Where(char.IsDigit).ToArray());double n;return double.TryParse(digits,out n)?n:0;}
  bool IsAttachedToVisualTree() {return Avalonia.VisualTree.VisualExtensions.GetVisualRoot(this)!=null;}
 }
}
