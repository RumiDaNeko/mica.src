using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Web.WebView2.Core;
namespace Mica {
 partial class BrowserApp {
  async Task RunVaultUiTest() {
   var log=new System.Collections.Generic.List<string>();int failures=0;Process fixture=null;
   try {
    await RunHelloChecks(log);
    const string master="test-only-vault-password-42",secret="fixture-secret-not-real";
    ShowVault();await Task.Delay(350);CaptureDialog(vaultWindow,"mica-vault-locked-page.png");vaultOpenUnlock();await Task.Delay(350);CaptureDialog(vaultUnlockWindow,"mica-vault-locked.png");
    var root=vaultBody;
    var passwords=new System.Collections.Generic.List<PasswordBox>{vaultMaster,VaultElements<PasswordBox>(root).Single()};var fields=VaultElements<TextBox>(root).ToList();var list=VaultElements<ListBox>(root).Single();
    Func<string,Button> command=title=>title=="Unlock / create local vault"?vaultUnlockButton:VaultElements<Button>(root).Single(b=>(string)b.Tag==title);
    passwords[0].Password=master;command("Unlock / create local vault").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    await WaitFor(()=>root.IsEnabled&&File.Exists(Path.Combine(profile,"vault.enc.json")),30000);
    fields[0].Text="https://vault-fixture.test";fields[1].Text="fixture-user";passwords[1].Password=secret;
    command("Save credential").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await WaitFor(()=>root.IsEnabled&&list.Items.Count==1,30000);
    string saved=File.ReadAllText(Path.Combine(profile,"vault.enc.json"));Require(!saved.Contains(secret)&&!saved.Contains("fixture-user"),"Vault saved plaintext credential");
    log.Add("PASS native vault creates and saves credentials encrypted on disk");
    list.SelectedIndex=0;vaultWindow.UpdateLayout();var shot=new RenderTargetBitmap((int)vaultWindow.ActualWidth,(int)vaultWindow.ActualHeight,96,96,PixelFormats.Pbgra32);shot.Render(vaultWindow);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(shot));using(var stream=File.Create(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"..","artifacts","mica-vault.png")))png.Save(stream);
    state.Dark=true;ApplyTheme();CaptureDialog(vaultWindow,"mica-vault-dark.png");
    VaultElements<Button>(root).Single(b=>b.Content as string=="Account & sync").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Task.Delay(250);CaptureDialog(vaultWindow,"mica-vault-sync.png");
    CloseVault();ShowVault();root=vaultBody;passwords=new System.Collections.Generic.List<PasswordBox>{vaultMaster,VaultElements<PasswordBox>(root).Single()};list=VaultElements<ListBox>(root).Single();
    Require(passwords[0].Password.Length==0&&passwords[1].Password.Length==0&&list.Items.Count==0,"Reopened vault remained unlocked");
    passwords[0].Password=master;command("Unlock / create local vault").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await WaitFor(()=>root.IsEnabled&&list.Items.Count==1,30000);list.SelectedIndex=0;
    log.Add("PASS closing locks vault; reopening decrypts saved entries with master password");
    vaultLockButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Task.Delay(100);
    Require(!vaultUnlocked&&list.Items.Count==0&&passwords[0].Password.Length==0,"The lock button did not lock the vault");
    passwords[0].Password=master;command("Unlock / create local vault").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await WaitFor(()=>root.IsEnabled&&list.Items.Count==1,30000);list.SelectedIndex=0;
    log.Add("PASS the lock button locks the open vault and it unlocks again");
    using(var server=new RegressionServer()) {
     Navigate(server.Url+"ok",false);await WaitFor(()=>CurrentView()!=null&&CurrentView().CoreWebView2.DocumentTitle=="Regression OK",15000);
     var view=CurrentView();view.CoreWebView2.AddWebResourceRequestedFilter("https://vault-*.test/*",CoreWebView2WebResourceContext.All);
     view.CoreWebView2.WebResourceRequested+=delegate(object sender,CoreWebView2WebResourceRequestedEventArgs e) {if(new Uri(e.Request.Uri).Host=="vault-fixture.test"||new Uri(e.Request.Uri).Host=="vault-other.test")e.Response=environment.CreateWebResourceResponse(new MemoryStream(Encoding.UTF8.GetBytes("<!doctype html><title>Vault form fixture</title><form onsubmit='window.submitted=true;return false'><input autocomplete='username'><input type='password'><button>Sign in</button></form>")),200,"OK","Content-Type: text/html");};
     Navigate("https://vault-fixture.test/",false);await WaitFor(()=>view.CoreWebView2.DocumentTitle=="Vault form fixture",10000);
     command("Fill selected credential on current website").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await WaitFor(()=>root.IsEnabled,5000);
     string form=await view.ExecuteScriptAsync("JSON.stringify({username:document.querySelector('input').value,password:document.querySelector('input[type=password]').value,submitted:!!window.submitted})");Require(form.Contains(secret)&&form.Contains("fixture-user")&&form.Contains("false"),"Explicit fill failed or submitted form");
     Navigate("https://vault-other.test/",false);await WaitFor(()=>view.CoreWebView2.Source=="https://vault-other.test/"&&view.CoreWebView2.DocumentTitle=="Vault form fixture",10000);await Task.Delay(200);
     command("Fill selected credential on current website").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await WaitFor(()=>root.IsEnabled,5000);Require((await view.ExecuteScriptAsync("document.querySelector('input[type=password]').value"))=="\"\"","Credential filled on mismatched origin");
     log.Add("PASS explicit fill checks HTTPS origin and never submits; other origins remain empty");
    }
    CloseVault();
    string endpoint=Path.Combine(profile,"fixture-endpoint.txt"),db=Path.Combine(profile,"fixture.sqlite");
    string fixtureScript=Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"..","server","test","desktop-fixture.mjs"));
    fixture=Process.Start(new ProcessStartInfo {FileName="node.exe",Arguments="--no-warnings \""+fixtureScript+"\" \""+db+"\" \""+endpoint+"\"",UseShellExecute=false,CreateNoWindow=true});
    await WaitFor(()=>File.Exists(endpoint)||fixture.HasExited,10000);Require(File.Exists(endpoint),"Node API fixture failed to start");vaultTestApi=File.ReadAllText(endpoint);
    string verifier=VaultCrypto.RandomVerifier();var start=VaultCrypto.Json.Deserialize<LoginStart>(await VaultRequest("/v1/auth/discord/start","POST",new {challenge=VaultCrypto.Challenge(verifier)},null));
    string oauthState=new Uri(start.authorizationUrl).Query.TrimStart('?').Split('&').Single(s=>s.StartsWith("state=",StringComparison.Ordinal)).Substring(6);
    await VaultRequest("/v1/auth/discord/callback?state="+oauthState+"&code=fixture","GET",null,null);
    var poll=VaultCrypto.Json.Deserialize<LoginPoll>(await VaultRequest("/v1/auth/discord/poll","POST",new {requestId=start.requestId,verifier=verifier},null));Require(poll.status=="ready","Desktop auth polling did not complete");
    var account=new VaultAccount {token=poll.token,id=poll.user.id,username=poll.user.username};
    ShowVault();await Task.Delay(100);root=vaultBody;vaultMaster.Password=master;vaultUnlockButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await WaitFor(()=>vaultUnlockWindow==null,30000);
    string beforePull=File.ReadAllText(Path.Combine(profile,"vault.enc.json"));
    await vaultCompleteSignIn(poll);
    bool wasDark=state.Dark;ToggleTheme();
    await WaitFor(()=>!SettingsPending,15000);
    var cloud=VaultCrypto.Json.Deserialize<RemoteSettings>(await VaultRequest("/v1/settings","GET",null,account));
    Require(cloud.settings!=null&&(bool)cloud.settings["dark"]==!wasDark,"Changed setting was not pushed: "+VaultCrypto.Json.Serialize(cloud));
    await VaultRequest("/v1/settings","PUT",new {settings=new System.Collections.Generic.Dictionary<string,object>{{"dark",wasDark},{"ambientYoutube",state.AmbientYoutube},{"strictTracking",true}},updatedAt=NowMs()+60000},account);
    PullSettings();await WaitFor(()=>state.Dark==wasDark&&state.StrictTracking,15000);state.StrictTracking=false;Save();
    log.Add("PASS settings push on change and newer cloud settings apply without unlocking the vault");
    // Signing in with an empty cloud keeps this device's logins (and uploads them on its own).
    var kept=await Task.Run(()=>VaultCrypto.Decrypt(VaultCrypto.Json.Deserialize<VaultEnvelope>(File.ReadAllText(Path.Combine(profile,"vault.enc.json"))),master));
    Require(kept.Entries.Any(e=>e.Username=="fixture-user"&&e.Password==secret),"Empty remote vault changed local data");
    log.Add("PASS sign-in automatically checks empty cloud vault and preserves local data");
    for(int probe=0;probe<4;probe++){await Task.Delay(1500);var seen=VaultCrypto.Json.Deserialize<RemoteVault>(await VaultRequest("/v1/vault","GET",null,account));log.Add("INFO cloud version after sign-in: "+seen.version+", pending "+File.Exists(Path.Combine(profile,"vault-sync-pending"))+", syncing "+vaultSyncing);}
    var payload=new VaultPayload {AccountId=account.id,Revision=SeenRevision(account.id)+5,Snapshot=new BrowserState {Dark=true}};payload.Entries.Add(new VaultEntry {Id="fixture",Origin="https://vault-fixture.test",Username="cloud-user",Password=secret});
    var envelope=await Task.Run(()=>VaultCrypto.Encrypt(payload,master));
    var current=VaultCrypto.Json.Deserialize<RemoteVault>(await VaultRequest("/v1/vault","GET",null,account));var put=VaultCrypto.Json.Deserialize<RemoteVault>(await VaultRequest("/v1/vault","PUT",new {version=current.version,envelope=VaultCrypto.ForSync(envelope)},account));Require(put.version==current.version+1,"Server version did not advance");
    var pulled=VaultCrypto.Json.Deserialize<RemoteVault>(await VaultRequest("/v1/vault","GET",null,account));var decoded=await Task.Run(()=>VaultCrypto.Decrypt(pulled.envelope,master));Require(decoded.Snapshot!=null&&decoded.Snapshot.Dark&&decoded.AccountId==account.id&&decoded.Entries.Single().Password==secret,"Desktop/Node encrypted roundtrip corrupted payload");
    log.Add("PASS desktop HTTP transport completes mocked Discord authorization and encrypted Node SQLite roundtrip");
    string beforeSecond=File.ReadAllText(Path.Combine(profile,"vault.enc.json"));
    await vaultCompleteSignIn(poll);
    var downloaded=VaultElements<ListBox>(root).Single();
    var names=downloaded.Items.Cast<VaultEntry>().Select(e=>e.Username).ToList();Require(names.Contains("cloud-user")&&names.Contains("fixture-user")&&names.Count==2,"Sign-in did not merge downloaded credentials with this device's: "+string.Join(",",names));
    Require(Directory.GetFiles(profile,"vault-before-pull-*.enc.json").Any(f=>File.ReadAllText(f)==beforeSecond),"Auto pull failed to preserve previous encrypted vault");
    Require(File.ReadAllText(Path.Combine(profile,"vault.enc.json"))!=beforeSecond,"Auto pull did not save downloaded encrypted vault");
    // Pulling then adds this device's inbox key in the background; let that save land first.
    string afterPull=File.ReadAllText(Path.Combine(profile,"vault.enc.json"));for(int i=0;i<20;i++){await Task.Delay(250);string now=File.ReadAllText(Path.Combine(profile,"vault.enc.json"));if(now==afterPull&&i>=4)break;afterPull=now;}
    // Saving a login while signed in sends it to the account on its own, a moment later.
    downloaded.SelectedItem=null;var editFields=VaultElements<TextBox>(root).ToList();editFields[0].Text="https://vault-auto.test";editFields[1].Text="auto-user";VaultElements<PasswordBox>(root).Single().Password="auto-secret-1";
    command("Save credential").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));bool synced=false;
    for(int i=0;i<40&&!synced;i++){await Task.Delay(400);var cloudNow=VaultCrypto.Json.Deserialize<RemoteVault>(await VaultRequest("/v1/vault","GET",null,account));if(cloudNow.envelope==null)continue;VaultCrypto.FromServer(cloudNow.envelope);var opened=await Task.Run(()=>VaultCrypto.Decrypt(cloudNow.envelope,master));synced=opened.Entries.Any(e=>e.Username=="auto-user"&&e.Password=="auto-secret-1")&&opened.Entries.Any(e=>e.Username=="fixture-user");}
    Require(synced,"Saving a login did not sync it to the account");
    Require(!File.Exists(Path.Combine(profile,"vault-sync-pending")),"Sync stayed pending after it went through");
    log.Add("PASS saving a login syncs it, and this device's other logins, to the account on its own");
    afterPull=File.ReadAllText(Path.Combine(profile,"vault.enc.json"));for(int i=0;i<20;i++){await Task.Delay(250);string now=File.ReadAllText(Path.Combine(profile,"vault.enc.json"));if(now==afterPull&&i>=4)break;afterPull=now;}
    await vaultCompleteSignIn(new LoginPoll {token="invalid-test-token",user=poll.user});
    Require(File.ReadAllText(Path.Combine(profile,"vault.enc.json"))==afterPull,"Failed auto pull changed local vault");
    log.Add("PASS sign-in downloads and displays cloud credentials, preserves encrypted backup; failed download leaves data intact");
    CloseVault();

    bool conflict=false;try{await VaultRequest("/v1/vault","PUT",new {version=0,envelope=VaultCrypto.ForSync(envelope)},account);}catch(WebException e){using(var response=(HttpWebResponse)e.Response)conflict=response.StatusCode==HttpStatusCode.Conflict;}Require(conflict,"Stale desktop upload overwrote remote vault");
    await VaultRequest("/v1/auth/logout","POST",new {},account);bool revoked=false;try{await VaultRequest("/v1/vault","GET",null,account);}catch(WebException e){using(var response=(HttpWebResponse)e.Response)revoked=response.StatusCode==HttpStatusCode.Unauthorized;}Require(revoked,"Signed-out desktop token still works");
    log.Add("PASS stale upload is rejected and signing out revokes server session");
   }catch(Exception e){failures++;log.Add("FAIL "+e);}
   finally {vaultTestApi=null;CloseVault();if(fixture!=null){if(!fixture.HasExited)fixture.Kill();fixture.Dispose();}}
   log.Add(failures==0?"PASS":"FAILURES: "+failures);File.WriteAllLines(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"..","artifacts","vault-ui-result.txt"),log);Application.Current.Shutdown(failures==0?0:1);
  }
 }
}
