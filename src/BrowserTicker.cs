using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Mica {
 partial class BrowserApp {
  // Changing text moves instead of jumping. Numbers roll like an odometer: only the digits that change
  // slide, up when the value grows and down when it shrinks. Other text slides up and fades into the new words.
  readonly Dictionary<TextBlock,int> tickerRuns=new Dictionary<TextBlock,int>();bool tickerInTests;
  void RollText(TextBlock block,string text) {
   text=text??"";string old=block.Text??"";
   int run;tickerRuns.TryGetValue(block,out run);tickerRuns[block]=++run;
   if(old==text&&block.Inlines.Count<=1){block.Text=text;return;}
   if((smoke&&!tickerInTests)||!SystemParameters.ClientAreaAnimation||!block.IsVisible||old.Length==0||text.Length==0){Settle(block,text);return;}
   // Line both up on the right, the way digits of a number line up.
   int width=Math.Max(old.Length,text.Length);string from=old.PadLeft(width),to=text.PadLeft(width);
   bool digitsOnly=true;
   for(int i=0;i<width;i++)if(from[i]!=to[i]&&!((char.IsDigit(from[i])||from[i]==' ')&&(char.IsDigit(to[i])||to[i]==' '))){digitsOnly=false;break;}
   if(digitsOnly)RollDigits(block,from,to,text,run);else SlideText(block,text,run);
  }
  void Settle(TextBlock block,string text) {
   var shift=block.RenderTransform as TranslateTransform;if(shift!=null)shift.BeginAnimation(TranslateTransform.YProperty,null);
   block.BeginAnimation(UIElement.OpacityProperty,null);block.Text=text;
  }
  void SlideText(TextBlock block,string text,int run) {
   var shift=block.RenderTransform as TranslateTransform;
   if(shift==null){if(block.RenderTransform!=null&&block.RenderTransform!=Transform.Identity){Settle(block,text);return;}shift=new TranslateTransform();block.RenderTransform=shift;}
   var ease=new CubicEase {EasingMode=EasingMode.EaseOut};
   var leave=new DoubleAnimation(0,TimeSpan.FromMilliseconds(90));
   leave.Completed+=delegate {
    int now;if(!tickerRuns.TryGetValue(block,out now)||now!=run)return;
    block.Text=text;
    block.BeginAnimation(UIElement.OpacityProperty,new DoubleAnimation(0,1,TimeSpan.FromMilliseconds(160)) {FillBehavior=FillBehavior.Stop});
    shift.BeginAnimation(TranslateTransform.YProperty,new DoubleAnimation(5,0,TimeSpan.FromMilliseconds(200)) {EasingFunction=ease,FillBehavior=FillBehavior.Stop});
   };
   block.BeginAnimation(UIElement.OpacityProperty,leave);
   shift.BeginAnimation(TranslateTransform.YProperty,new DoubleAnimation(0,-5,TimeSpan.FromMilliseconds(90)));
  }
  void RollDigits(TextBlock block,string from,string to,string text,int run) {
   long a,b;bool up=!long.TryParse(new string(from.Where(char.IsDigit).ToArray()),out a)||!long.TryParse(new string(to.Where(char.IsDigit).ToArray()),out b)||b>=a;
   var face=new Typeface(block.FontFamily,block.FontStyle,block.FontWeight,block.FontStretch);double dip=VisualTreeHelper.GetDpi(block).PixelsPerDip;
   Func<char,double> measure=delegate(char c) {return c==' '?0:new FormattedText(c.ToString(),CultureInfo.CurrentUICulture,FlowDirection.LeftToRight,face,block.FontSize,Brushes.Black,dip).WidthIncludingTrailingWhitespace;};
   double ascent=block.FontFamily.Baseline*block.FontSize,line=block.FontFamily.LineSpacing*block.FontSize,below=block.FontSize*0.2;
   var inlines=new List<Inline>();int changed=0,total=from.Zip(to,(x,y)=>x!=y).Count(d=>d);
   var ease=new CubicEase {EasingMode=EasingMode.EaseOut};
   for(int i=0;i<to.Length;i++) {
    if(from[i]==to[i]){if(to[i]!=' ')inlines.Add(new Run(to[i].ToString()));continue;}
    // The window spans the ascent plus a little below; its negative bottom margin keeps the baseline in place.
    var window=new Grid {Height=ascent+below,Margin=new Thickness(0,0,0,-below),ClipToBounds=true,Width=measure(from[i])};
    Func<char,TextBlock> glyph=delegate(char c) {var t=new TextBlock {Text=c.ToString(),Height=line,VerticalAlignment=VerticalAlignment.Top,HorizontalAlignment=HorizontalAlignment.Center,RenderTransform=new TranslateTransform()};return t;};
    var leaving=glyph(from[i]);var coming=glyph(to[i]);window.Children.Add(leaving);window.Children.Add(coming);
    double travel=(ascent+below)*(up?1:-1);((TranslateTransform)coming.RenderTransform).Y=travel;
    int order=total-1-changed++;var delay=TimeSpan.FromMilliseconds(order*45);var time=TimeSpan.FromMilliseconds(380);
    ((TranslateTransform)leaving.RenderTransform).BeginAnimation(TranslateTransform.YProperty,new DoubleAnimation(0,-travel,time) {EasingFunction=ease,BeginTime=delay});
    ((TranslateTransform)coming.RenderTransform).BeginAnimation(TranslateTransform.YProperty,new DoubleAnimation(travel,0,time) {EasingFunction=ease,BeginTime=delay});
    leaving.BeginAnimation(UIElement.OpacityProperty,new DoubleAnimation(1,0,time) {BeginTime=delay});
    window.BeginAnimation(FrameworkElement.WidthProperty,new DoubleAnimation(measure(from[i]),measure(to[i]),time) {EasingFunction=ease,BeginTime=delay});
    inlines.Add(new InlineUIContainer(window) {BaselineAlignment=BaselineAlignment.Baseline});
   }
   block.Inlines.Clear();block.Inlines.AddRange(inlines);
   var done=new System.Windows.Threading.DispatcherTimer {Interval=TimeSpan.FromMilliseconds(380+45*Math.Max(0,total-1)+30)};
   done.Tick+=delegate {done.Stop();int now;if(tickerRuns.TryGetValue(block,out now)&&now==run)block.Text=text;};done.Start();
  }
 }
}
