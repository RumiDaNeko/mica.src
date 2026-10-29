using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Mica {
 // Discord sign-in, bound to this device: the sync service gives Mica a short code, and after Discord the
 // browser page asks for it. Someone who sends you a sign-in link they started can't finish it with your
 // account, because the code only ever shows here, on the device that asked.
 partial class BrowserApp {
  static readonly Regex SignInCode=new Regex("^[A-HJKMNP-Z2-9]{4}-[A-HJKMNP-Z2-9]{4}$");
  // The service's {"error":"…"} code from a failed request, or null.
  static string ApiError(WebException e) {
   var response=e.Response as HttpWebResponse;if(response==null)return null;
   try {using(var reader=new StreamReader(response.GetResponseStream())){var body=VaultCrypto.Json.Deserialize<System.Collections.Generic.Dictionary<string,object>>(reader.ReadToEnd());object code;return body!=null&&body.TryGetValue("error",out code)?code as string:null;}}
   catch(Exception){return null;}
  }
  async Task SignInWithDiscord(Func<bool> live,Window owner,TextBlock message) {
   var verifier=VaultCrypto.RandomVerifier();LoginStart start;
   try {start=VaultCrypto.Json.Deserialize<LoginStart>(await VaultRequest("/v1/auth/discord/start","POST",new {challenge=VaultCrypto.Challenge(verifier),confirm=true},null));}
   catch(WebException e){if(ApiError(e)=="confirmation_required")throw new InvalidOperationException("Update Mica to sign in.");throw;}
   Uri url;
   if(start==null||string.IsNullOrEmpty(start.requestId)||!Uri.TryCreate(start.authorizationUrl,UriKind.Absolute,out url)||url.Scheme!="https"||url.Host!="discord.com"||!url.IsDefaultPort||!string.IsNullOrEmpty(url.UserInfo)||(url.AbsolutePath!="/oauth2/authorize"&&url.AbsolutePath!="/api/oauth2/authorize"))throw new InvalidOperationException("The service returned an unsafe sign-in address.");
   if(start.confirmationCode==null||!SignInCode.IsMatch(start.confirmationCode))throw new InvalidOperationException("The service didn't send a sign-in code, so Mica won't continue.");
   DateTime until;if(!DateTime.TryParse(start.expiresAt,null,System.Globalization.DateTimeStyles.AdjustToUniversal|System.Globalization.DateTimeStyles.AssumeUniversal,out until)||until>DateTime.UtcNow.AddMinutes(30))until=DateTime.UtcNow.AddMinutes(10);
   if(!live())return;
   bool cancelled=false;Action closeCard=ShowSignInCode(owner,start.confirmationCode,delegate {cancelled=true;});
   try {
    Process.Start(new ProcessStartInfo(url.AbsoluteUri){UseShellExecute=true});
    message.Text="Complete Discord sign-in in your browser, then enter the code Mica shows.";
    while(DateTime.UtcNow<until) {
     await Task.Delay(2000);if(!live()||cancelled){if(live())message.Text="Sign-in cancelled.";return;}
     LoginPoll poll;
     try {poll=VaultCrypto.Json.Deserialize<LoginPoll>(await VaultRequest("/v1/auth/discord/poll","POST",new {requestId=start.requestId,verifier=verifier},null));}
     catch(WebException retry) {
      var response=retry.Response as HttpWebResponse;
      if(response!=null&&(int)response.StatusCode==429){response.Close();continue;}
      if(ApiError(retry)=="authorization_failed"){message.Text="Sign-in was cancelled in the browser. Try again.";return;}
      throw;
     }
     if(!live()||cancelled)return;
     if(poll==null)throw new InvalidOperationException("Invalid sign-in response.");
     if(poll.status!="ready")continue;
     if(poll.user==null||string.IsNullOrEmpty(poll.user.id)||string.IsNullOrEmpty(poll.token))throw new InvalidOperationException("Invalid sign-in response.");
     closeCard();await vaultCompleteSignIn(poll);return;
    }
    message.Text="Sign-in timed out. Please try again.";
   }finally {closeCard();}
  }
  internal Window signInCodeWindow;
  // Returns a way to close it that isn't the user cancelling (sign-in finished, failed or timed out).
  Action ShowSignInCode(Window owner,string code,Action cancel) {
   StackPanel body,actions;var card=CreateMicaDialog(owner,"Sign-in code",460,330,out body,out actions);signInCodeWindow=card;
   body.Children.Add(DialogText("After you approve Mica in Discord, the browser asks for this code. Type it there.",13,"Muted"));
   // A read-only box rather than plain text, so the code can be selected as well as copied.
   var shown=new TextBox {Text=code,IsReadOnly=true,FontSize=34,FontFamily=new FontFamily("Consolas"),FontWeight=FontWeights.SemiBold,HorizontalAlignment=HorizontalAlignment.Center,Margin=new Thickness(0,22,0,18),Tag="signin-code",
    Background=Brushes.Transparent,BorderThickness=new Thickness(0),Padding=new Thickness(0)};
   shown.SetResourceReference(TextBox.ForegroundProperty,"Ink");shown.SetResourceReference(TextBox.SelectionBrushProperty,"Accent");body.Children.Add(shown);
   body.Children.Add(DialogText("Never share this code. If a page or person asks you for it, you didn't start this sign-in.",11,"Muted"));
   var copy=new Button {Content="Copy",Padding=new Thickness(16,10,16,10),Margin=new Thickness(0,0,8,0),Tag="signin-copy"};actions.Children.Add(copy);
   copy.Click+=delegate {
    try {Clipboard.SetText(code);}catch(Exception){return;}
    copy.Content=Localizer.Translate("Copied");var back=new System.Windows.Threading.DispatcherTimer {Interval=TimeSpan.FromSeconds(2)};back.Tick+=delegate {back.Stop();copy.Content=Localizer.Translate("Copy");};back.Start();
   };
   var stop=new Button {Content="Cancel sign-in",Padding=new Thickness(16,10,16,10),Tag="signin-cancel"};actions.Children.Add(stop);
   bool closedByUs=false;stop.Click+=delegate {cancel();card.Close();};
   card.Closed+=delegate {if(signInCodeWindow==card)signInCodeWindow=null;if(!closedByUs)cancel();};
   card.Show();
   return delegate {if(closedByUs)return;closedByUs=true;try {card.Close();}catch(Exception){}};
  }
 }
}
