using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Microsoft.Web.WebView2.Core;

namespace Mica {
 partial class BrowserApp {
  static IEnumerable<T> LogicalDescendants<T>(object node) where T:DependencyObject {
   var d=node as DependencyObject;if(d==null)yield break;if(d is T)yield return (T)d;
   foreach(var child in LogicalTreeHelper.GetChildren(d))foreach(var hit in LogicalDescendants<T>(child))yield return hit;
  }
  async Task<Popup> WaitForBubble(int ms) {
   try {await WaitFor(()=>siteBubble!=null&&siteBubble.IsOpen,ms);}catch(Exception){return null;}
   await Task.Delay(150);return siteBubble;
  }
  void DismissSignInOffer() {if(siteBubble!=null&&siteBubble.IsOpen&&siteBubble.Tag is Action)((Action)siteBubble.Tag)();}
  void Choose(Popup bubble,string value) {var button=LogicalDescendants<Button>(bubble.Child).First(b=>(b.Tag as string)==value);button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));}
  async Task SubmitLogin(CoreWebView2 core,string user,string password) {
   await core.ExecuteScriptAsync("u.value="+VaultCrypto.Json.Serialize(user)+";p.value="+VaultCrypto.Json.Serialize(password));
   foreach(var press in new[]{"mousePressed","mouseReleased"})await core.CallDevToolsProtocolMethodAsync("Input.dispatchMouseEvent","{\"type\":\""+press+"\",\"x\":40,\"y\":40,\"button\":\"left\",\"clickCount\":1}");
  }
  [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool PostMessage(IntPtr hwnd,int msg,IntPtr w,IntPtr l);
  async Task RunAutofillTest() {
   var log=regressionLog;string dir=Path.Combine(profile,"autofill");Directory.CreateDirectory(dir);
   try {
    RegressionCheck("a tab that played music recently is not put to sleep",delegate {
     var music=new PageEntry {Id="music",Url="https://music.test/",LastUsed=DateTime.UtcNow.AddMinutes(-10)};
     audibleAt["music"]=DateTime.UtcNow.AddMinutes(-5);Require(RecentlyAudible("music",null)&&!TabLifecycle.ShouldSleep(music,false,RecentlyAudible("music",null),DateTime.UtcNow,true),"slept a paused music tab");
     audibleAt["music"]=DateTime.UtcNow.AddMinutes(-40);Require(!RecentlyAudible("music",null),"never sleeps");audibleAt.Remove("music");
    });
    Navigate("https://login.test/",false);await WaitFor(()=>CurrentView()!=null&&CurrentView().CoreWebView2!=null,20000);
    // The engine appears before Mica has set the tab up; its login watcher is registered just after.
    List<Task> registered;await WaitFor(()=>pageScripts.TryGetValue(CurrentView().CoreWebView2,out registered),10000);
    EventHandler<CoreWebView2WebResourceRequestedEventArgs> serve=delegate(object sender,CoreWebView2WebResourceRequestedEventArgs e) {
     var uri=new Uri(e.Request.Uri);
     if(uri.Host=="sso.test"&&uri.AbsolutePath=="/otp"){
      // A step that asks for the one-time code above the password.
      const string otpPage="<!doctype html><title>Verify</title><form method=post action=/done><input id=code name=otp_code inputmode=numeric maxlength=6><input id=pw type=password name=password><button id=go style='position:fixed;left:0;top:0;width:140px;height:140px'>Verify</button></form>";
      e.Response=environment.CreateWebResourceResponse(new MemoryStream(Encoding.UTF8.GetBytes(otpPage)),200,"OK","Content-Type: text/html");return;
     }
     if(uri.Host=="sso.test"){
      const string googleLike="<!doctype html><title>Sign in</title><div id=card><input type=email id=identifierId name=identifier autocomplete=username><input type=password name=hiddenPassword style='display:none'><div id=identifierNext><button style='position:fixed;left:0;top:0;width:140px;height:140px'><span>Next</span></button></div></div>"+
       "<script>document.querySelector('#identifierNext button').onclick=()=>{setTimeout(()=>{card.innerHTML=\"<div>someone</div><input type=password name=Passwd autocomplete=current-password><div id=passwordNext><button style='position:fixed;left:0;top:0;width:140px;height:140px'><span>Next</span></button></div>\";document.querySelector('#passwordNext button').onclick=()=>setTimeout(()=>{history.pushState({},'','/welcome');card.innerHTML='Welcome'},300)},200)}</script>";
      e.Response=environment.CreateWebResourceResponse(new MemoryStream(Encoding.UTF8.GetBytes(googleLike)),200,"OK","Content-Type: text/html");return;
     }
     if(uri.Host!="login.test")return;
     string passkey=uri.AbsolutePath=="/passkey"?"<script>PublicKeyCredential.isConditionalMediationAvailable().then(ok=>{if(!ok)return;window.pageAsked='conditional';navigator.credentials.get({mediation:'conditional',publicKey:{challenge:new Uint8Array(32),rpId:'login.test'}}).catch(()=>{})})</script>":"";
     const string checkout="<!doctype html><title>checkout</title><div style=display:none><form><input name=email autocomplete=username><input type=password name=password></form></div><form id=pay><input id=mail type=email autocomplete=email><input id=card autocomplete=cc-number><input autocomplete=cc-exp><input id=cvv name=cvv type=password maxlength=4 inputmode=numeric><button id=paynow style='position:fixed;left:0;top:0;width:140px;height:140px'>Pay</button></form>";
     string html=uri.AbsolutePath=="/pay"?checkout:uri.AbsolutePath=="/app"?"<!doctype html><title>home</title><input id=s placeholder=\"Find or start a conversation\"><input id=q aria-label=\"Search\" type=text>":uri.AbsolutePath=="/done"?"<!doctype html><title>welcome</title>signed in":"<!doctype html><title>sign in</title><form method=post action=/done><input id=u name=u autocomplete=username><input id=p name=p type=password><button id=go style='position:fixed;left:0;top:0;width:140px;height:140px'>Sign in</button></form>"+passkey;
     e.Response=environment.CreateWebResourceResponse(new MemoryStream(Encoding.UTF8.GetBytes(html)),200,"OK","Content-Type: text/html");
    };
    var core=CurrentView().CoreWebView2;core.AddWebResourceRequestedFilter("https://login.test/*",CoreWebView2WebResourceContext.All);core.WebResourceRequested+=serve;
    await PageScriptsReady(core); // Mica registers its page scripts before a new tab's first load; so does this one.
    core.Navigate("https://login.test/");await WaitForPage();await Task.Delay(400);
    log.Add("login script on page: "+await core.ExecuteScriptAsync("typeof window.__micaUsePasskey"));
    // 1. Submitting a login offers to save, with the details editable.
    await SubmitLogin(core,"alice@example.com","pw-123456");
    var bubble=await WaitForBubble(8000);
    RegressionCheck("submitting a login offers to save it",delegate {Require(bubble!=null,"no bubble");});
    if(bubble==null)throw new Exception("stop");
    var user=LogicalDescendants<TextBox>(bubble.Child).First(t=>(t.Tag as string)=="password-username");
    var master=LogicalDescendants<PasswordBox>(bubble.Child).FirstOrDefault(t=>(t.Tag as string)=="password-master");
    var buttons=string.Join(" | ",LogicalDescendants<Button>(bubble.Child).Select(b=>b.Content as string).Where(s=>s!=null&&s.Length>1));
    log.Add("save card: user="+user.Text+" buttons: "+buttons+" master field "+(master!=null));
    var hwnd=new System.Windows.Interop.WindowInteropHelper(window).Handle;CurrentView().Focus();await Task.Delay(200);var before=KeyboardWindow();
    user.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,0,MouseButton.Left) {RoutedEvent=UIElement.PreviewMouseDownEvent});await Task.Delay(100);
    user.CaretIndex=user.Text.Length;var target=KeyboardWindow();foreach(char c in ".x")PostMessage(target,0x0102,new IntPtr(c),IntPtr.Zero);await Task.Delay(300);
    log.Add("wpf focus: "+(Keyboard.FocusedElement==null?"null":Keyboard.FocusedElement.GetType().Name)+" user focused "+user.IsKeyboardFocused);log.Add("keyboard before: "+before+" after click: "+target+" (bubble window "+PopupHandle(bubble)+", main "+hwnd+"); typed text: "+user.Text);
    RegressionCheck("the save card shows the account, a Don't save button and no master password",delegate {Require(user.Text.StartsWith("alice@example.com"),"user "+user.Text);Require(buttons.Contains("Don't save")&&buttons.Contains("Save")&&buttons.Contains("Never"),buttons);Require(master==null,"asked for a master password");});
    RegressionCheck("the save card can really be typed in after the page had the keyboard",delegate {Require(user.Text=="alice@example.com.x","typed "+user.Text);});
    {var sc=(FrameworkElement)bubble.Child;sc.UpdateLayout();var si=new System.Windows.Media.Imaging.RenderTargetBitmap((int)Math.Ceiling(sc.ActualWidth+30),(int)Math.Ceiling(sc.ActualHeight+30),96,96,System.Windows.Media.PixelFormats.Pbgra32);si.Render(sc);var sp=new System.Windows.Media.Imaging.PngBitmapEncoder();sp.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(si));using(var sf=File.Create(Path.Combine(dir,"save-card.png")))sp.Save(sf);}
    user.Text="alice.edited";Choose(bubble,"save");
    await WaitFor(()=>SavedLogins("https://login.test").Count==1&&File.Exists(VaultFile),15000).ContinueWith(t=>{});
    RegressionCheck("Save puts the edited account straight into the encrypted vault, no password asked",delegate {
     var saved=SavedLogins("https://login.test");Require(saved.Count==1,"entries "+saved.Count);
     Require(saved[0].Username=="alice.edited"&&saved[0].Password=="pw-123456",saved[0].Username);
     Require(File.Exists(VaultFile)&&!File.ReadAllText(VaultFile).Contains("pw-123456"),"vault file");
     Require(!File.Exists(DeviceLogins.PathFor(profile)),"used the device store");
    });
    // 2. After a restart (nothing unlocked in memory) the login page fills by itself.
    fillLog=new List<string>();
    sessionVault=null;sessionMaster=null;
    core=CurrentView().CoreWebView2;core.Navigate("https://login.test/");await WaitForPage();
    // The page never gets the password from its own message; you pick the login in Mica's bubble.
    var offer=await WaitForBubble(8000);string unpicked=await core.ExecuteScriptAsync("u.value+'/'+p.value");
    RegressionCheck("a saved login waits for you to pick it",delegate {Require(offer!=null&&!unpicked.Contains("pw-"),unpicked+(offer==null?", no bubble":""));});
    if(offer!=null)Choose(offer,"0");
    string filled="";for(int i=0;i<40;i++){await Task.Delay(150);filled=await core.ExecuteScriptAsync("u.value+'/'+p.value");if(filled.Contains("pw-"))break;}
    if(sessionVault==null){var sw=System.Diagnostics.Stopwatch.StartNew();bool opened=await OpenAutofillVault();log.Add("direct open: "+opened+" in "+sw.ElapsedMilliseconds+" ms; page script "+await core.ExecuteScriptAsync("typeof window.__micaUsePasskey"));}
    log.Add("fill calls: "+string.Join(" ; ",fillLog));
    log.Add("restart diag: vault open "+(sessionVault!=null)+", saved "+SavedLogins("https://login.test").Count+", key "+AutofillKeyStore.Exists(profile)+", status "+Find<TextBlock>("StatusText").Text);
    log.Add("after restart the page has: "+filled+(siteBubble!=null?" (a bubble is open)":""));
    RegressionCheck("autofill works without unlocking",delegate {Require(filled=="\"alice.edited/pw-123456\"",filled);});
    // A checkout page: card fields, a masked CVV, a contact email and a hidden sign-in form are no sign-in.
    DismissSignInOffer();await Task.Delay(300);
    core.Navigate("https://login.test/pay");await WaitForPage();var payOffer=await WaitForBubble(3000);
    RegressionCheck("a payment page doesn't offer a saved login",delegate {Require(payOffer==null,"offered");});
    if(payOffer!=null)DismissSignInOffer();
    await core.ExecuteScriptAsync("mail.value='alice@example.com';card.value='4242424242424242';cvv.value='123';cvv.dispatchEvent(new Event('input',{bubbles:true}));document.getElementById('pay').onsubmit=e=>{e.preventDefault();cvv.remove()}");
    foreach(var press in new[]{"mousePressed","mouseReleased"})await core.CallDevToolsProtocolMethodAsync("Input.dispatchMouseEvent","{\"type\":\""+press+"\",\"x\":40,\"y\":40,\"button\":\"left\",\"clickCount\":1}");
    var cvvSave=await WaitForBubble(2500);
    RegressionCheck("paying doesn't offer to save the card code as a password",delegate {Require(cvvSave==null,"offered to save");});
    if(cvvSave!=null)Choose(cvvSave,"skip");
    core.Navigate("https://login.test/");await WaitForPage();await Task.Delay(600);DismissSignInOffer();
    // 3. Don't save keeps nothing.
    await Task.Delay(1600);await SubmitLogin(core,"bob","other-pass");bubble=await WaitForBubble(8000);
    RegressionCheck("a second account offers to save too, without asking to unlock",delegate {Require(bubble!=null,"no bubble");Require(!LogicalDescendants<PasswordBox>(bubble.Child).Any(t=>(t.Tag as string)=="password-master"),"asked for master");});
    if(bubble!=null)Choose(bubble,"skip");await Task.Delay(800);
    RegressionCheck("Don't save keeps nothing",delegate {Require(SavedLogins("https://login.test").Count==1,"entries "+SavedLogins("https://login.test").Count);});
    // 4. Never for this site.
    core=CurrentView().CoreWebView2;core.Navigate("https://login.test/");await WaitForPage();await Task.Delay(1800);
    DismissSignInOffer();await core.ExecuteScriptAsync("u.value='';p.value=''");
    await SubmitLogin(core,"carol","third-pass");bubble=await WaitForBubble(8000);if(bubble!=null)Choose(bubble,"never");
    core=CurrentView().CoreWebView2;core.Navigate("https://login.test/");await WaitForPage();await Task.Delay(1800);
    DismissSignInOffer();await core.ExecuteScriptAsync("u.value='';p.value=''");
    await SubmitLogin(core,"dave","fourth-pass");var again=await WaitForBubble(2500);
    RegressionCheck("Never for this site stops the offers",delegate {Require(again==null,"offered again");Require(SavedLogins("https://login.test").Count==1,"entries");});
    // 5. Google-style sign-in: email step, then a password step on the same page, then the page moves on.
    core=CurrentView().CoreWebView2;core.AddWebResourceRequestedFilter("https://sso.test/*",CoreWebView2WebResourceContext.All);
    core.Navigate("https://sso.test/signin");await WaitForPage();await Task.Delay(500);
    Func<string,Task> type=async text=>{await core.CallDevToolsProtocolMethodAsync("Input.insertText","{\"text\":"+VaultCrypto.Json.Serialize(text)+"}");};
    Func<Task> next=async delegate {foreach(var t in new[]{"mousePressed","mouseReleased"})await core.CallDevToolsProtocolMethodAsync("Input.dispatchMouseEvent","{\"type\":\""+t+"\",\"x\":40,\"y\":40,\"button\":\"left\",\"clickCount\":1}");};
    await core.ExecuteScriptAsync("document.getElementById('identifierId').focus()");await type("someone@gmail.com");await next();await Task.Delay(600);
    await core.ExecuteScriptAsync("document.querySelector('input[name=Passwd]').focus()");await type("g00gle-pass");await next();
    bubble=await WaitForBubble(8000);string offered=bubble==null?"none":LogicalDescendants<TextBox>(bubble.Child).First(t=>(t.Tag as string)=="password-username").Text;
    log.Add("google-style sign-in offered: "+offered);
    RegressionCheck("a Google-style two-step sign-in offers to save, with the email",delegate {Require(offered=="someone@gmail.com",offered);});
    if(bubble!=null){Choose(bubble,"save");await WaitFor(()=>SavedLogins("https://sso.test").Any(),5000).ContinueWith(t=>{});}
    core.Navigate("https://sso.test/signin");await WaitForPage();var pick=await WaitForBubble(6000);if(pick!=null)Choose(pick,"0");await Task.Delay(800);
    string step1=await core.ExecuteScriptAsync("document.getElementById('identifierId').value");
    RegressionCheck("the email step fills once you pick the login",delegate {Require(step1=="\"someone@gmail.com\"",step1);});
    // 5b. A one-time code above the password is never taken for the username, nor filled with it.
    core.Navigate("https://sso.test/otp");await WaitForPage();await Task.Delay(500);
    var otpPick=await WaitForBubble(3000);if(otpPick!=null)Choose(otpPick,"0");await Task.Delay(600);
    string codeBox=await core.ExecuteScriptAsync("document.getElementById('code').value");
    await core.ExecuteScriptAsync("document.getElementById('code').value='';document.getElementById('pw').value='';document.getElementById('code').focus()");await type("123456");
    await core.ExecuteScriptAsync("document.getElementById('pw').focus()");await type("otp-step-pass");await next();
    bubble=await WaitForBubble(8000);string otpOffered=bubble==null?"none":LogicalDescendants<TextBox>(bubble.Child).First(t=>(t.Tag as string)=="password-username").Text;
    log.Add("otp step: filled code box "+codeBox+", offered "+otpOffered);
    RegressionCheck("a one-time code box is never filled with the username",delegate {Require(codeBox=="\"\"",codeBox);});
    RegressionCheck("a one-time code above the password isn't taken for the username",delegate {Require(otpOffered!="none"&&!otpOffered.Contains("123456"),otpOffered);});
    if(bubble!=null)Choose(bubble,"skip");await Task.Delay(300);
    // 6. The dropdown under a login field.
    core=CurrentView().CoreWebView2;core.Navigate("https://login.test/passkey");await WaitForPage();await Task.Delay(1200);
    await core.ExecuteScriptAsync("u.value='';p.value='';u.blur();u.focus()");
    try {await WaitFor(()=>loginMenu!=null&&loginMenu.IsOpen,5000);}catch(Exception){}
    Func<string> rows=()=>loginMenu==null||loginMenu.Child==null?"":string.Join(" | ",LogicalDescendants<TextBlock>(loginMenu.Child).Select(t=>t.Text).Where(t=>!string.IsNullOrEmpty(t)&&t.Length>1));
    log.Add("window active when focusing: "+window.IsActive+", foreground is "+ForegroundOwner());
    string menu=rows();log.Add("dropdown: "+menu);
    RegressionCheck("focusing the username opens Mica's dropdown with saved accounts and a passkey",delegate {Require(loginMenu!=null&&loginMenu.IsOpen,"not open");Require(menu.Contains("alice.edited")&&menu.Contains("Use a passkey")&&menu.Contains("Saved in Mica"),menu);});
    if(loginMenu!=null&&loginMenu.IsOpen) {
     var card=(FrameworkElement)loginMenu.Child;card.UpdateLayout();
     var image=new System.Windows.Media.Imaging.RenderTargetBitmap((int)Math.Ceiling(card.ActualWidth+24),(int)Math.Ceiling(card.ActualHeight+24),96,96,System.Windows.Media.PixelFormats.Pbgra32);image.Render(card);
     var png=new System.Windows.Media.Imaging.PngBitmapEncoder();png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(image));using(var file=File.Create(Path.Combine(dir,"login-menu.png")))png.Save(file);
    }
    simulatedLoginKeys=true; // CDP key events never reach the real keyboard state.
    foreach(var key in new[]{"ArrowDown","Enter"}) {
     foreach(var t in new[]{"rawKeyDown","keyUp"})await core.CallDevToolsProtocolMethodAsync("Input.dispatchKeyEvent","{\"type\":\""+t+"\",\"key\":\""+key+"\",\"code\":\""+key+"\",\"windowsVirtualKeyCode\":"+(key=="Enter"?13:40)+"}");
     await Task.Delay(300);
    }
    await Task.Delay(400);string picked=await core.ExecuteScriptAsync("u.value+'/'+p.value");log.Add("after ArrowDown + Enter: "+picked);simulatedLoginKeys=false;
    RegressionCheck("arrow keys and Enter pick an account from the dropdown",delegate {Require(picked=="\"alice.edited/pw-123456\"",picked);Require(loginMenu==null||!loginMenu.IsOpen,"still open");});
    await core.ExecuteScriptAsync("u.value='';u.blur();u.focus()");await Task.Delay(500);
    await core.CallDevToolsProtocolMethodAsync("Input.insertText","{\"text\":\"zz\"}");await Task.Delay(500);
    log.Add("after typing zz: "+rows());
    RegressionCheck("typing filters the saved accounts",delegate {var now=rows();Require(!now.Contains("alice.edited")&&now.Contains("Use a passkey"),now);});
    string passkeyApi=await core.ExecuteScriptAsync("typeof window.__micaUsePasskey+'/'+(window.pageAsked||'')");
    RegressionCheck("the page's passkey request waits for the dropdown",delegate {Require(passkeyApi=="\"function/conditional\"",passkeyApi);});
    await core.ExecuteScriptAsync("u.blur()");await Task.Delay(400);
    RegressionCheck("leaving the field closes the dropdown",delegate {Require(loginMenu==null||!loginMenu.IsOpen,"still open");});
    // 7b. A signed-in page with search boxes: never filled, no dropdown.
    core=CurrentView().CoreWebView2;core.Navigate("https://login.test/app");await WaitForPage();await Task.Delay(600);
    string late=await core.ExecuteScriptAsync(FillScript("https://login.test","alice.edited","pw-123456"));
    await core.ExecuteScriptAsync("q.focus()");await Task.Delay(700);bool menuOnSearch=loginMenu!=null&&loginMenu.IsOpen;
    string boxes=await core.ExecuteScriptAsync("s.value+'|'+q.value");
    RegressionCheck("search boxes are never filled and get no login dropdown",delegate {Require(late=="\"none\""&&boxes=="\"|\"","fill "+late+" boxes "+boxes);Require(!menuOnSearch,"dropdown opened");});
    // 7. Passwords: choose the master password (a passphrase for viewing) for the vault Mica made.
    ShowVault();await Task.Delay(400);
    vaultOpenUnlock();await Task.Delay(300);CaptureDialog(vaultUnlockWindow,"mica-vault-choose-passphrase.png");vaultMaster.Password="correct horse battery";vaultUnlockButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    await WaitFor(()=>!File.Exists(PassphrasePendingPath),15000).ContinueWith(t=>{});
    RegressionCheck("Passwords sets the passphrase on the vault Mica made",delegate {Require(!File.Exists(PassphrasePendingPath),"still pending");var check=VaultCrypto.Decrypt(VaultCrypto.Json.Deserialize<VaultEnvelope>(File.ReadAllText(VaultFile)),"correct horse battery");Require(check.Entries.Any(e=>e.Username=="alice.edited"),"entries");});
    // Syncing merges: logins only one side has are kept, a shared login keeps this device's password, nothing is removed.
    var here=new VaultPayload {Entries=new List<VaultEntry>{new VaultEntry {Id="a",Origin="https://a.test",Username="me",Password="local-new"},new VaultEntry {Id="b",Origin="https://b.test",Username="me",Password="only-here"}}};
    int merged=VaultCrypto.MergeEntries(here,new[]{new VaultEntry {Id="a",Origin="https://a.test",Username="me",Password="cloud-old",Totp="otpauth://totp/x?secret=JBSWY3DPEHPK3PXP"},new VaultEntry {Id="b",Origin="https://c.test",Username="you",Password="only-cloud"}});
    RegressionCheck("syncing merges logins instead of replacing them",delegate {Require(merged==1&&here.Entries.Count==3,"added "+merged+", now "+here.Entries.Count);var a=here.Entries.First(x=>x.Origin=="https://a.test");Require(a.Password=="local-new"&&a.Totp!=null,"a "+a.Password);Require(here.Entries.Select(x=>x.Id).Distinct().Count()==3,"duplicate ids");});
    // The password shows only when asked, and hides again on another login.
    var vaultList=LogicalDescendants<ListBox>(vaultWindow).FirstOrDefault(l=>l.ItemsSource!=null&&l.Items.Count>0);
    var revealButton=LogicalDescendants<Button>(vaultWindow).FirstOrDefault(b=>(b.Tag as string)=="vault-password-reveal");
    var passwordText=LogicalDescendants<TextBlock>(vaultWindow).FirstOrDefault(t=>(t.Tag as string)=="vault-password-text");
    string hiddenAt="",shownAt="",rehiddenAt="";
    if(vaultList!=null&&revealButton!=null&&passwordText!=null) {
     vaultList.SelectedItem=vaultList.Items.Cast<VaultEntry>().First(v=>v.Username=="alice.edited");await Task.Delay(200);hiddenAt=passwordText.Text;
     revealButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Task.Delay(100);shownAt=passwordText.Text;
     vaultList.SelectedItem=null;vaultList.SelectedItem=vaultList.Items[0];await Task.Delay(200);rehiddenAt=passwordText.Text;
    }
    RegressionCheck("Show reveals a saved password, and picking a login hides it again",delegate {Require(vaultList!=null&&revealButton!=null&&passwordText!=null,"missing parts");Require(!hiddenAt.Contains("pw-")&&shownAt=="pw-123456"&&!rehiddenAt.Contains("pw-"),hiddenAt+" / "+shownAt+" / "+rehiddenAt);});
    vaultWindow.Close();await Task.Delay(300);
    // 8. Locked vault and no device key: saving still goes into the vault, sealed, without asking.
    sessionVault=null;sessionMaster=null;AutofillKeyStore.Forget(profile);
    await StoreLogin("https://login.test","erin","pw-erin-1");
    var locked=VaultCrypto.Json.Deserialize<VaultEnvelope>(File.ReadAllText(VaultFile));
    RegressionCheck("a locked vault takes new logins without the master password",delegate {Require(sessionVault==null,"unlocked");Require(locked.inbox!=null&&locked.inbox.Count==1,"inbox "+(locked.inbox==null?0:locked.inbox.Count));Require(!File.ReadAllText(VaultFile).Contains("pw-erin-1")&&!File.Exists(DeviceLogins.PathFor(profile)),"plain or device store");});
    RegressionCheck("the sync copy leaves the inbox out",delegate {var sync=VaultCrypto.Json.Serialize(VaultCrypto.ForSync(locked));Require(!sync.Contains("inbox"),sync.Substring(0,60));});
    await UnlockSessionVault(()=>true,"correct horse battery");
    await WaitFor(()=>!VaultCrypto.HasInbox(VaultCrypto.Json.Deserialize<VaultEnvelope>(File.ReadAllText(VaultFile))),8000).ContinueWith(t=>{});
    RegressionCheck("the master password opens the sealed login and folds it into the vault",delegate {Require(sessionVault!=null&&sessionVault.Entries.Any(e=>e.Username=="erin"&&e.Password=="pw-erin-1"),"not merged");Require(!VaultCrypto.HasInbox(VaultCrypto.Json.Deserialize<VaultEnvelope>(File.ReadAllText(VaultFile))),"inbox kept");});
    var envelope=VaultCrypto.Json.Deserialize<VaultEnvelope>(File.ReadAllText(VaultFile));
    RegressionCheck("the device key is protected and is the vault key, never the master password",delegate {var key=Path.Combine(profile,"autofill-vault-key.bin");Require(File.Exists(key)&&!File.Exists(Path.Combine(profile,"autofill-key.bin"))&&!Encoding.UTF8.GetString(File.ReadAllBytes(key)).Contains("correct horse"),"key file");var stored=AutofillKeyStore.Load(profile);Require(VaultCrypto.IsKey(stored)&&!stored.Contains("correct horse")&&VaultCrypto.Decrypt(envelope,stored).Entries.Any(e=>e.Username=="erin"),"stored key");});
    // 9. An older device key file held the master password: it becomes the vault key and the old file goes.
    byte[] legacyEntropy;using(var sha=System.Security.Cryptography.SHA256.Create())legacyEntropy=sha.ComputeHash(Encoding.UTF8.GetBytes("Mica autofill key v1|"+Path.GetFullPath(profile).ToUpperInvariant()));
    AutofillKeyStore.Forget(profile);sessionVault=null;sessionMaster=null;
    File.WriteAllBytes(Path.Combine(profile,"autofill-key.bin"),System.Security.Cryptography.ProtectedData.Protect(Encoding.UTF8.GetBytes("correct horse battery"),legacyEntropy,System.Security.Cryptography.DataProtectionScope.CurrentUser));
    bool migrated=await OpenAutofillVault();
    RegressionCheck("an old password key file is swapped for the vault key and erased",delegate {Require(migrated&&sessionVault!=null&&VaultCrypto.IsKey(sessionMaster),"not opened");Require(!File.Exists(Path.Combine(profile,"autofill-key.bin"))&&File.Exists(Path.Combine(profile,"autofill-vault-key.bin")),"old file kept");Require(VaultCrypto.IsKey(AutofillKeyStore.Load(profile)),"stored");});
    // 10. Locking the vault removes the device key, so the next fill asks to unlock.
    ShowVault();await Task.Delay(300);vaultOpenUnlock();await Task.Delay(200);vaultMaster.Password="correct horse battery";vaultUnlockButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    await WaitFor(()=>vaultUnlockWindow==null&&vaultUnlocked,15000).ContinueWith(t=>{});
    bool keyWhileOpen=AutofillKeyStore.Exists(profile);
    vaultLockButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Task.Delay(200);
    bool silent=await OpenAutofillVault();
    RegressionCheck("locking the vault deletes the device key and stops silent filling",delegate {Require(keyWhileOpen,"no key after unlock");Require(!vaultUnlocked&&!AutofillKeyStore.Exists(profile)&&sessionVault==null&&sessionMaster==null&&!silent,"still open");});
    if(vaultUnlockWindow!=null)vaultUnlockWindow.Close();vaultWindow.Close();await Task.Delay(300);
   }catch(Exception e){if(e.Message!="stop"){log.Add("ERROR "+e);regressionFailures++;}}
   log.Add(regressionFailures==0?"ALL PASS":regressionFailures+" FAILURE(S)");
   File.WriteAllLines(Path.Combine(dir,"result.txt"),log);window.Close();
  }
 }
}
