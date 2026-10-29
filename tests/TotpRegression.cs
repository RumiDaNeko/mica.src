using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;

namespace Mica {
 partial class BrowserApp {
  // 2FA codes: the RFC's own numbers, then a setup page whose key Mica saves, and sign-in pages it fills.
  async Task RunTotpTest() {
   var log=regressionLog;string dir=Path.Combine(profile,"totp");Directory.CreateDirectory(dir);
   window.WindowState=WindowState.Normal;window.Left=-6000;window.Top=-6000;
   try {
    RegressionCheck("codes match RFC 6238's test values",delegate {
     // "12345678901234567890" in base32; 8 digits, as in the RFC's table.
     var sha1=Totp.Parse("otpauth://totp/Test?secret=GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ&digits=8");
     var at=new DateTime(1970,1,1,0,0,0,DateTimeKind.Utc);
     Require(Totp.Code(sha1,at.AddSeconds(59))=="94287082","59 s: "+Totp.Code(sha1,at.AddSeconds(59)));
     Require(Totp.Code(sha1,at.AddSeconds(1111111109))=="07081804","1111111109 s");
     Require(Totp.Code(sha1,at.AddSeconds(20000000000))=="65353130","20000000000 s");
     var sha256=Totp.Parse("otpauth://totp/Test?secret=GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQGEZA&digits=8&algorithm=SHA256");
     Require(Totp.Code(sha256,at.AddSeconds(59))=="46119246","SHA-256 59 s: "+Totp.Code(sha256,at.AddSeconds(59)));
    });
    RegressionCheck("a typed key reads the same however it is spaced or cased",delegate {
     var a=Totp.Parse("jbsw y3dp ehpk 3pxp");var b=Totp.Parse("JBSWY3DPEHPK3PXP");
     Require(a.Secret.SequenceEqual(b.Secret)&&Totp.Parse(Totp.Normalize(a)).Secret.SequenceEqual(a.Secret),"spacing");
     Totp.Key none;Require(!Totp.TryParse("hello world",out none)&&!Totp.TryParse("otpauth://hotp/x?secret=JBSWY3DPEHPK3PXP",out none),"accepted junk");
    });

    const string key="JBSWY3DPEHPK3PXP";
    var pages=new Dictionary<string,string> {
     {"/setup","<!doctype html><title>Two-factor authentication</title><h1>Set up your authenticator app</h1><p>Scan the QR code, or can't scan? Enter this setup key:</p><p><code>jbsw y3dp ehpk 3pxp</code></p><label>Code from the app <input id=code autocomplete=one-time-code inputmode=numeric maxlength=6></label>"},
     {"/verify","<!doctype html><title>Verify</title><p>Enter the 6-digit code from your authenticator app.</p><input id=code name=otp inputmode=numeric maxlength=6>"},
     {"/boxes","<!doctype html><title>Verify</title><p>Two-factor code</p><div id=row><span><input maxlength=1></span><span><input maxlength=1></span><span><input maxlength=1></span><span><input maxlength=1></span><span><input maxlength=1></span><span><input maxlength=1></span></div>"},
     {"/coupon","<!doctype html><title>Checkout</title><p>Two-factor authentication keeps you safe.</p><input id=code name=coupon-code maxlength=6>"},
    };
    Navigate("https://otp.test/",false);await WaitFor(()=>CurrentView()!=null&&CurrentView().CoreWebView2!=null,20000);
    List<Task> registered;await WaitFor(()=>pageScripts.TryGetValue(CurrentView().CoreWebView2,out registered),10000);
    var core=CurrentView().CoreWebView2;var environment=core.Environment;
    core.AddWebResourceRequestedFilter("https://otp.test/*",CoreWebView2WebResourceContext.All);
    core.WebResourceRequested+=delegate(object sender,CoreWebView2WebResourceRequestedEventArgs e) {
     string html;if(!pages.TryGetValue(new Uri(e.Request.Uri).AbsolutePath,out html))html="<!doctype html><title>otp</title>";
     e.Response=environment.CreateWebResourceResponse(new MemoryStream(Encoding.UTF8.GetBytes(html)),200,"OK","Content-Type: text/html");
    };
    await PageScriptsReady(core);fillLog=new List<string>();
    Func<string,Task<string>> value=async selector=>VaultCrypto.Json.Deserialize<string>(await core.ExecuteScriptAsync("document.querySelector("+VaultCrypto.Json.Serialize(selector)+").value"));

    // 1. A setup page's key is offered for saving; once saved, its first code is filled in.
    core.Navigate("https://otp.test/setup");await WaitForPage();
    var bubble=await WaitForBubble(8000);
    RegressionCheck("a setup page's 2FA key is offered for saving",delegate {Require(bubble!=null&&LogicalDescendants<TextBox>(bubble.Child).Any(t=>(t.Tag as string)=="totp-account"),"no bubble");});
    if(bubble==null)throw new Exception("stop");
    LogicalDescendants<TextBox>(bubble.Child).First(t=>(t.Tag as string)=="totp-account").Text="alice@example.com";Choose(bubble,"save");
    string expected=Totp.Code(Totp.Normalize(Totp.Parse(key)));
    await WaitFor(()=>SavedTotps("https://otp.test").Count==1,8000).ContinueWith(t=>{});
    string filled="";for(int i=0;i<40&&filled=="";i++){await Task.Delay(200);filled=await value("#code");}
    RegressionCheck("the saved key fills the setup page's first code",delegate {Require(SavedTotps("https://otp.test").Count==1&&SavedTotps("https://otp.test")[0].Username=="alice@example.com","not saved");Require(filled.Length==6&&(filled==expected||filled==Totp.Code(SavedTotps("https://otp.test")[0].Totp)),"field has '"+filled+"', expected "+expected);});
    RegressionCheck("the key is kept inside the encrypted vault, not in plain text",delegate {string file=File.ReadAllText(VaultFile);Require(!file.Contains(key)&&!file.ToUpperInvariant().Contains(key),"key visible in vault file");});

    // 2. A sign-in page asking for the code gets it without anything to click.
    core.Navigate("https://otp.test/verify");await WaitForPage();
    string code="";for(int i=0;i<40&&code=="";i++){await Task.Delay(200);code=await value("#code");}
    RegressionCheck("a 2FA prompt is filled with the current code",delegate {Require(code.Length==6&&code.All(char.IsDigit),"field has '"+code+"'");});

    // 3. Six one-digit boxes get one digit each.
    core.Navigate("https://otp.test/boxes");await WaitForPage();
    string row="";for(int i=0;i<40&&row.Length<6;i++){await Task.Delay(200);row=VaultCrypto.Json.Deserialize<string>(await core.ExecuteScriptAsync("[...document.querySelectorAll('#row input')].map(i=>i.value).join('')"));}
    RegressionCheck("a row of one-digit boxes is filled digit by digit",delegate {Require(row.Length==6&&row.All(char.IsDigit),"boxes have '"+row+"'");});

    // 4. A coupon box isn't a 2FA prompt, even with the right words nearby.
    core.Navigate("https://otp.test/coupon");await WaitForPage();await Task.Delay(1500);
    string coupon=await value("#code");
    RegressionCheck("a coupon code field is left alone",delegate {Require(coupon=="","coupon got '"+coupon+"'");});

    // 5. Another site never gets this site's codes.
    RegressionCheck("codes stay with the site whose key they are",delegate {Require(SavedTotps("https://other.test").Count==0,"leaked");});

    // 6. Passwords shows the live code for the login.
    // Saving the key made the vault with this device's key; Passwords asks for a master password to view it.
    ShowVault();await Task.Delay(600);
    if(vaultWindow!=null&&vaultOpenUnlock!=null) {
     vaultOpenUnlock();await Task.Delay(200);vaultMaster.Password="correct horse battery staple";vaultUnlockButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
     await WaitFor(()=>vaultUnlockWindow==null&&vaultUnlocked,20000).ContinueWith(t=>{});
    }
    if(vaultWindow!=null) {
     var lists=LogicalDescendants<ListBox>(vaultWindow.Content as DependencyObject).ToList();var logins=lists.FirstOrDefault(l=>l.ItemsSource!=null);
     if(logins!=null){logins.SelectedItem=logins.Items.Cast<object>().OfType<VaultEntry>().FirstOrDefault(e=>!string.IsNullOrEmpty(e.Totp));await Task.Delay(1300);}
     var shown=VaultElements<TextBlock>(vaultWindow.Content as DependencyObject).FirstOrDefault(t=>(t.Tag as string)=="vault-totp-code");
     RegressionCheck("Passwords shows the login's current 2FA code",delegate {Require(shown!=null&&shown.IsVisible&&shown.Text.Replace(" ","").Length==6,shown==null?"no code":"'"+shown.Text+"' visible "+shown.IsVisible);});
     vaultWindow.Close();await Task.Delay(300);
    }else RegressionCheck("Passwords shows the login's current 2FA code",delegate {Require(false,"Passwords didn't open");});
    log.AddRange(fillLog.Select(l=>"  "+l));
   }catch(Exception e){if(e.Message!="stop"){log.Add("ERROR "+e);regressionFailures++;}}
   log.Add(regressionFailures==0?"ALL PASS":regressionFailures+" FAILURE(S)");
   File.WriteAllLines(Path.Combine(dir,"result.txt"),log);window.Close();
  }
 }
}
