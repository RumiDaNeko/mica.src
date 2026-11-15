using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace Mica {
 partial class BrowserApp {
  // Discord sign-in from the account card, then settings syncing between two profiles that stand in for two
  // PCs on one Discord account. The sync service is the real Worker on loopback (server/worker/test/
  // desktop-fixture.mjs) with Discord's token exchange mocked; the test plays the browser side: Discord sends
  // it back to the callback, and it types the code Mica shows into the confirm page.
  async Task RunDiscordSyncTest() {
   var log=regressionLog;string dir=Path.Combine(profile,"discord-sync");Directory.CreateDirectory(dir);
   Process fixture=null;BrowserApp other=null;
   try {
    string endpoint=Path.Combine(dir,"endpoint.txt");
    string script=Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"..","server","worker","test","desktop-fixture.mjs"));
    fixture=Process.Start(new ProcessStartInfo {FileName="node.exe",Arguments="--no-warnings \""+script+"\" \""+endpoint+"\"",UseShellExecute=false,CreateNoWindow=true});
    await WaitFor(()=>File.Exists(endpoint)&&new FileInfo(endpoint).Length>0||fixture.HasExited,30000);Require(File.Exists(endpoint),"Worker fixture failed to start");
    string api=File.ReadAllText(endpoint).Trim();vaultTestApi=api;
    var steps=new List<string>();
    Func<BrowserApp,Func<Uri,Task>> browserSide=app=>async url=>{
     try {
      string oauthState=url.Query.TrimStart('?').Split('&').Single(s=>s.StartsWith("state=",StringComparison.Ordinal)).Substring(6);
      Require(url.Host=="discord.com"&&url.Query.Contains("client_id=desktop-test"),"Unexpected authorization address "+url);
      string page=await Task.Run(()=>FixtureHttp(api+"/v1/auth/discord/callback?state="+oauthState+"&code=fixture",null));
      var form=Regex.Match(page,"name=\"form\" value=\"([A-Za-z0-9_-]{43})\"");Require(form.Success,"Callback did not ask for the code: "+page);
      Require(app.signInCodeWindow!=null,"Mica showed no sign-in code");
      string code=DiscordTestFind<TextBox>(app.signInCodeWindow).Single(t=>(t.Tag as string)=="signin-code").Text;
      string done=await Task.Run(()=>FixtureHttp(api+"/v1/auth/discord/confirm","form="+form.Groups[1].Value+"&code="+Uri.EscapeDataString(code)));
      steps.Add(done.Contains("Sign-in complete")?"confirmed":"confirm page: "+done);
     }catch(Exception e){steps.Add("browser side: "+e.Message);}
    };
    Func<BrowserApp,Task> clickSignIn=async app=>{
     app.openSignInPage=browserSide(app);app.ShowAccount();await Task.Delay(300);
     var button=DiscordTestFind<Button>(app.accountPopup.Child).Single(b=>(b.Tag as string)=="account-passwords");
     Require(DiscordTestFind<TextBlock>(button).Any(t=>t.Text==Localizer.Translate("Sign in with Discord")),"Signed-out card has no Sign in with Discord button");
     button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
     await WaitFor(()=>app.ReadVaultAccount()!=null||steps.Any(s=>s!="confirmed"),30000);
     Require(app.ReadVaultAccount()!=null,"Sign-in did not finish: "+string.Join("; ",steps));
    };

    // PC 1: sign in straight from the account card.
    await clickSignIn(this);
    var account=ReadVaultAccount();
    RegressionCheck("Sign in with Discord on the account card signs in without opening Passwords",delegate {Require(account.id=="223456789012345678"&&activeVaultWindow==null&&signInCodeWindow==null,"vault "+(activeVaultWindow!=null)+", code card "+(signInCodeWindow!=null));});

    // PC 1 changes how Mica looks; it goes to the account a moment later.
    if(!state.Dark)ToggleTheme();state.TextScale=1.3;ApplyTextScale();state.TreeTabs=true;state.FrameColor="#336699";state.CloseEffect="fade";state.ReaderSize=22;state.HomeGlass=40;Save();
    try {await WaitFor(()=>!SettingsPending,15000);}
    catch(Exception) {
     var sync=LoadSettingsSync();log.Add("INFO pending: updated "+sync.Updated+", seen "+sync.Seen+", synced "+sync.Synced);
     try {log.Add("INFO manual push: "+SettingsRequest("PUT",new {settings=SettingsSnapshot(),updatedAt=sync.Updated},10000));}catch(WebException e){log.Add("INFO manual push failed: "+e.Message+" "+(e.Response==null?"":new StreamReader(e.Response.GetResponseStream()).ReadToEnd()));}
     throw;
    }
    var cloud=VaultCrypto.Json.Deserialize<RemoteSettings>(await VaultRequest("/v1/settings","GET",null,account));
    RegressionCheck("PC 1's settings reach the account",delegate {Require(cloud.settings!=null&&(bool)cloud.settings["dark"]&&(bool)cloud.settings["treeTabs"]&&(string)cloud.settings["frameColor"]=="#336699"&&(string)cloud.settings["closeEffect"]=="fade",VaultCrypto.Json.Serialize(cloud));});

    // PC 2: a fresh profile with its own data folder, signed in to the same Discord account.
    var second=CreateProfile("Second PC","#3B82F6");
    // A new profile copies this one's theme; another PC wouldn't, so it starts from Mica's defaults.
    var fresh=new BrowserState();fresh.AddTab("mica://home");StateStore.Save(Path.Combine(ProfileFolder(second.Id),"session.json"),fresh);
    other=OpenProfile(second.Id);
    await WaitFor(()=>other.window.IsLoaded,15000);await Task.Delay(500);other.vaultTestApi=api;
    Require(other.profile!=profile&&other.ReadVaultAccount()==null&&!other.state.Dark&&!other.state.TreeTabs,"Second profile did not start clean");
    await clickSignIn(other);
    await WaitFor(()=>other.state.TreeTabs&&other.state.Dark,15000).ContinueWith(t=>{});
    RegressionCheck("PC 2 signs in with the same Discord account and pulls PC 1's settings",delegate {
     var s=other.state;Require(s.Dark&&Math.Abs(s.TextScale-1.3)<0.001&&s.TreeTabs&&s.FrameColor=="#336699"&&s.CloseEffect=="fade"&&s.ReaderSize==22&&s.HomeGlass==40,
      "dark "+s.Dark+", text "+s.TextScale+", tree "+s.TreeTabs+", frame "+s.FrameColor+", effect "+s.CloseEffect+", reader "+s.ReaderSize+", glass "+s.HomeGlass);
     Require(File.ReadAllText(Path.Combine(other.profile,"session.json")).Contains("#336699"),"PC 2 did not save the synced settings");
    });

    // And back: PC 2 changes something, PC 1 picks it up on its next pull (launch or sign-in).
    other.state.TreeTabs=false;other.state.ReaderSerif=true;other.state.FrameColor=null;other.Save();
    await WaitFor(()=>!other.SettingsPending,15000);
    PullSettings();await WaitFor(()=>!state.TreeTabs&&state.ReaderSerif,15000).ContinueWith(t=>{});
    RegressionCheck("PC 1 pulls PC 2's later change",delegate {Require(!state.TreeTabs&&state.ReaderSerif&&state.FrameColor==null&&state.Dark,"tree "+state.TreeTabs+", serif "+state.ReaderSerif+", frame "+state.FrameColor);});

    // A cloud copy can't set values Mica would never write.
    var bad=new Dictionary<string,object>{{"dark",true},{"textScale",9},{"frameColor","red;}"},{"closeEffect","boom!"},{"readerSize",500},{"treeTabs",true}};
    bool refused=false;try {await VaultRequest("/v1/settings","PUT",new {settings=bad,updatedAt=NowMs()+60000},account);}catch(WebException e){refused=ApiError(e)=="invalid_settings";}
    RegressionCheck("the sync service refuses values Mica would never write",delegate {Require(refused,"accepted");});
    // And if a server sent them anyway, Mica takes only the valid ones.
    ReceiveSettings(new RemoteSettings {settings=bad,updatedAt=NowMs()+60000},null);await Task.Delay(300);
    RegressionCheck("out-of-range synced values are ignored, valid ones still apply",delegate {Require(state.TreeTabs&&Math.Abs(UiScale-1.3)<0.001&&state.FrameColor==null&&state.CloseEffect=="fade"&&state.ReaderSize==22,"text "+state.TextScale+", frame "+state.FrameColor+", effect "+state.CloseEffect+", reader "+state.ReaderSize);});

    // Until the new Worker is deployed: a sync service that only knows the first three settings still gets those.
    await Task.Run(()=>FixtureHttp(api+"/fixture/legacy-settings",""));
    ToggleTheme();bool dark=state.Dark;state.ReaderSize=24;Save();
    await WaitFor(()=>!SettingsPending,15000).ContinueWith(t=>{});
    cloud=VaultCrypto.Json.Deserialize<RemoteSettings>(await VaultRequest("/v1/settings","GET",null,account));
    RegressionCheck("an older sync service still gets dark, ambient and strict tracking",delegate {Require(!SettingsPending&&cloud.settings.Count==3&&(bool)cloud.settings["dark"]==dark,VaultCrypto.Json.Serialize(cloud));});
   }catch(Exception e){log.Add("ERROR "+e);regressionFailures++;}
   finally {
    vaultTestApi=null;openSignInPage=null;
    if(other!=null){other.vaultTestApi=null;try {other.window.Close();}catch(Exception){}}
    if(fixture!=null){try {if(!fixture.HasExited)fixture.Kill();}catch(Exception){}fixture.Dispose();}
   }
   log.Add(regressionFailures==0?"ALL PASS":regressionFailures+" FAILURE(S)");
   File.WriteAllLines(Path.Combine(dir,"result.txt"),log);window.Close();
  }
  static string FixtureHttp(string url,string form) {
   var request=(HttpWebRequest)WebRequest.Create(url);request.AllowAutoRedirect=false;request.Timeout=15000;
   if(form!=null){request.Method="POST";request.ContentType="application/x-www-form-urlencoded";var bytes=Encoding.UTF8.GetBytes(form);request.ContentLength=bytes.Length;using(var s=request.GetRequestStream())s.Write(bytes,0,bytes.Length);}
   try {using(var response=request.GetResponse())using(var reader=new StreamReader(response.GetResponseStream(),Encoding.UTF8))return reader.ReadToEnd();}
   catch(WebException e){if(e.Response==null)throw;using(var reader=new StreamReader(e.Response.GetResponseStream(),Encoding.UTF8))return reader.ReadToEnd();}
  }
  static IEnumerable<T> DiscordTestFind<T>(object root) where T:DependencyObject {
   var node=root as DependencyObject;if(node==null)yield break;
   if(node is T)yield return (T)node;
   foreach(var child in LogicalTreeHelper.GetChildren(node))foreach(var found in DiscordTestFind<T>(child))yield return found;
  }
 }
}
