using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Security.Cryptography;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
namespace Mica {
 partial class BrowserApp {
  string vaultTestApi;
  Func<LoginPoll,Task> vaultCompleteSignIn;
  string VaultApi {get {return smoke&&vaultTestApi!=null?vaultTestApi:"https://the-api.lessnoise.online";}}
  Window vaultWindow; static Window activeVaultWindow;
  sealed class VaultAccount { public string token {get;set;} public string id {get;set;} public string username {get;set;} public string avatar {get;set;} public string banner {get;set;} public int? accent_color {get;set;} }
  sealed class VaultUser { public string id {get;set;} public string username {get;set;} public string avatar {get;set;} public string banner {get;set;} public int? accent_color {get;set;} }
  sealed class AccountResult { public VaultUser user {get;set;} }
  sealed class LoginStart { public string requestId {get;set;} public string authorizationUrl {get;set;} public string confirmationCode {get;set;} public string expiresAt {get;set;} }
  sealed class LoginPoll { public string status {get;set;} public string token {get;set;} public VaultUser user {get;set;} }
  sealed class RemoteVault { public int version {get;set;} public VaultEnvelope envelope {get;set;} }
  Action vaultLockNow;
  void CloseVault() {if(vaultWindow!=null)vaultWindow.Close();}
  [System.Runtime.InteropServices.DllImport("user32.dll")] static extern uint GetClipboardSequenceNumber();
  static MemoryStream ClipboardDword(int value) {return new MemoryStream(BitConverter.GetBytes(value));}
  // Kept out of Win+V history, Cloud Clipboard and clipboard monitors; cleared after 30 s unless something else was copied since.
  void CopyPassword(string secret) {
   var data=new DataObject();data.SetText(secret);data.SetData("ExcludeClipboardContentFromMonitorProcessing",ClipboardDword(0));data.SetData("CanIncludeInClipboardHistory",ClipboardDword(0));data.SetData("CanUploadToCloudClipboard",ClipboardDword(0));
   Clipboard.SetDataObject(data,true);ClearClipboardLater(GetClipboardSequenceNumber());
  }
  static async void ClearClipboardLater(uint copied) {await Task.Delay(30000);try {if(GetClipboardSequenceNumber()==copied)Clipboard.Clear();}catch(Exception){}}
  async Task<T> VaultTimed<T>(Task<T> operation,HttpWebRequest request) {if(await Task.WhenAny(operation,Task.Delay(15000))!=operation){request.Abort();operation.ContinueWith(t=>{var ignored=t.Exception;},TaskContinuationOptions.OnlyOnFaulted);throw new InvalidOperationException("The sync service timed out. Try again.");}return await operation;}
  async Task<string> VaultRequest(string route,string method,object body,VaultAccount account) {
   if(privateMode||closing)throw new InvalidOperationException("Vault is unavailable in private browsing.");
   var r=(HttpWebRequest)WebRequest.Create(VaultApi+route);r.Method=method;r.AllowAutoRedirect=false;r.Timeout=15000;r.ReadWriteTimeout=15000;r.Accept="application/json";
   if(account!=null)r.Headers[HttpRequestHeader.Authorization]="Bearer "+account.token;
   if(body!=null){r.ContentType="application/json";var b=Encoding.UTF8.GetBytes(VaultCrypto.Json.Serialize(body));if(b.Length>2*1024*1024)throw new InvalidOperationException("Vault is too large to sync (2 MB limit). Reduce saved history or credentials.");r.ContentLength=b.Length;using(var stream=await VaultTimed(r.GetRequestStreamAsync(),r)){await VaultTimed(stream.WriteAsync(b,0,b.Length).ContinueWith(t=>{t.GetAwaiter().GetResult();return true;}),r);}}
   using(var response=(HttpWebResponse)await VaultTimed(r.GetResponseAsync(),r)){if((int)response.StatusCode<200||(int)response.StatusCode>=300)throw new InvalidOperationException("Sync service rejected the request.");using(var stream=response.GetResponseStream())using(var output=new MemoryStream()){var buffer=new byte[8192];int count;while((count=await VaultTimed(stream.ReadAsync(buffer,0,buffer.Length),r))>0){if(output.Length+count>2*1024*1024)throw new InvalidOperationException("Sync response too large.");output.Write(buffer,0,count);}return Encoding.UTF8.GetString(output.ToArray());}}
  }
  // The newest vault revision this device has uploaded or accepted, per account: a cloud copy older than
  // that is one the server kept back, so it is only taken after asking.
  string SeenRevisionsPath {get {return Path.Combine(profile,"vault-revisions.txt");}}
  long SeenRevision(string accountId) {
   try {if(File.Exists(SeenRevisionsPath))foreach(var line in File.ReadAllLines(SeenRevisionsPath)){var parts=line.Split(' ');long value;if(parts.Length==2&&parts[0]==accountId&&long.TryParse(parts[1],out value))return value;}}catch(Exception){}
   return 0;
  }
  void RememberRevision(string accountId,long revision) {
   if(revision<=SeenRevision(accountId))return;
   var lines=new System.Collections.Generic.List<string>();try {if(File.Exists(SeenRevisionsPath))lines.AddRange(File.ReadAllLines(SeenRevisionsPath).Where(l=>!l.StartsWith(accountId+" ",StringComparison.Ordinal)));}catch(Exception){}
   lines.Add(accountId+" "+revision);File.WriteAllLines(SeenRevisionsPath,lines);
  }
  async Task<RemoteVault> FetchVerifiedVault(VaultAccount account) {
   var verified=VaultCrypto.Json.Deserialize<AccountResult>(await VaultRequest("/v1/account","GET",null,account));
   if(verified==null||verified.user==null||verified.user.id!=account.id)throw new InvalidOperationException("Account verification failed. Sign out and sign in again.");
   var remote=VaultCrypto.Json.Deserialize<RemoteVault>(await VaultRequest("/v1/vault","GET",null,account));
   if(remote==null||remote.version<0)throw new InvalidOperationException("Invalid remote vault.");return remote;
  }
  string PromptRemoteMaster(Window owner) {
   StackPanel body,actions;var popup=CreateMicaDialog(owner,"Unlock cloud data",500,340,out body,out actions);
   body.Resources=vaultBody.Resources;
   body.Children.Add(DialogText("Your cloud vault uses a different master password. Enter it to download your data. Local data stays unchanged if you cancel.",13,"Muted"));
   var input=new PasswordBox {Margin=new Thickness(0,18,0,0)};body.Children.Add(input);
   var cancel=new Button {Content="Cancel",Padding=new Thickness(16,10,16,10),Margin=new Thickness(0,0,8,0)};cancel.Click+=delegate {popup.DialogResult=false;};actions.Children.Add(cancel);
   var unlock=new Button {Content="Unlock cloud data",Padding=new Thickness(16,10,16,10),IsDefault=true};unlock.Click+=delegate {popup.DialogResult=true;};actions.Children.Add(unlock);
   popup.Loaded+=delegate {input.Focus();};bool accepted=popup.ShowDialog()==true;string result=accepted?input.Password:null;input.Clear();return result;
  }
  void ShowVault() {
   if(privateMode){Status("Password vault and sync are disabled in private browsing.");return;}if(activeVaultWindow!=null){activeVaultWindow.Activate();return;}
   StackPanel root,footer;var dialog=CreateMicaDialog(window,"Passwords & sync",840,790,out root,out footer);vaultWindow=dialog;activeVaultWindow=dialog;
   Action<string> label=delegate(string text){root.Children.Add(new TextBlock {Text=text,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,6)});};
   label("PASSWORD VAULT & ENCRYPTED SYNC");label("Your master password stays on this device while this window is open. It cannot be recovered. Discord signs you in; it does not unlock your vault.");
   var accountLabel=new TextBlock();root.Children.Add(accountLabel);var message=new TextBlock {TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,10,0,8)};if(File.Exists(PassphrasePendingPath))message.Text="Mica is already saving your logins. Choose a master password to view them; it works like a passphrase.";
   var password=new PasswordBox {Margin=new Thickness(0,4,0,4),Padding=new Thickness(8)};
   VaultPayload payload=null;VaultAccount account=null;bool live=true,busy=false;int remoteVersion=-1;string knownAccount=null,vaultKey=null; // vaultKey: VaultCrypto.IsKey, set whenever payload is
   string local=Path.Combine(profile,"vault.enc.json"),authPath=Path.Combine(profile,"vault-session.bin");
   try{if(File.Exists(authPath))account=VaultCrypto.Json.Deserialize<VaultAccount>(Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(authPath),null,DataProtectionScope.CurrentUser)));}catch(Exception){account=null;}
   Action refreshAccount=delegate {accountLabel.Text=account==null?"Not signed in": "Discord · "+account.username;};refreshAccount();
   Func<Task> pullRemote=null;
   vaultCompleteSignIn=async poll=>{
    if(!live)return;
    if(poll==null||poll.user==null||string.IsNullOrEmpty(poll.user.id)||string.IsNullOrEmpty(poll.token))throw new InvalidOperationException("Invalid sign-in response.");
    account=new VaultAccount {token=poll.token,id=poll.user.id,username=poll.user.username,avatar=poll.user.avatar,banner=poll.user.banner,accent_color=poll.user.accent_color};
    File.WriteAllBytes(authPath,ProtectedData.Protect(Encoding.UTF8.GetBytes(VaultCrypto.Json.Serialize(account)),null,DataProtectionScope.CurrentUser));
    remoteVersion=-1;knownAccount=null;refreshAccount();RefreshAvatars();PullSettings();
    if(payload==null){message.Text="Signed in. Your settings sync now; unlock the vault to download your passwords.";return;}
    message.Text="Signed in. Downloading your encrypted data…";
    try {await pullRemote();}catch(Exception){if(live)message.Text="Signed in, but download did not finish. Your local data is unchanged. Use Pull to retry.";}
   };
   Action<Func<Task>,string> add=delegate(Func<Task> action,string title){var button=new Button {Content=title,Padding=new Thickness(10,7,10,7),Margin=new Thickness(0,3,0,3),HorizontalContentAlignment=HorizontalAlignment.Left};root.Children.Add(button);button.Click+=async delegate {if(busy||!live)return;busy=true;root.IsEnabled=false;SetVaultUnlockBusy(true);try{if(privateMode||closing)throw new InvalidOperationException("Vault unavailable.");await action();}catch(WebException e){var response=e.Response as HttpWebResponse;message.Text=response!=null&&response.StatusCode==HttpStatusCode.Conflict?"Remote vault changed. Pull and review it before pushing again.":"Could not contact the sync service. Check deployment and connectivity.";if(response!=null&&response.StatusCode==HttpStatusCode.Conflict){remoteVersion=-1;knownAccount=null;}if(response!=null)response.Close();}catch(Exception e){message.Text=e is CryptographicException||e is ArgumentException||e is InvalidOperationException?e.Message:"The operation could not be completed.";}finally{busy=false;if(live){root.IsEnabled=true;SetVaultUnlockBusy(false);}}};};
   add(delegate {return SignInWithDiscord(()=>live,dialog,message);},"Sign in with Discord");
   add(async delegate {var previous=account;account=null;remoteVersion=-1;knownAccount=null;if(File.Exists(authPath))File.Delete(authPath);refreshAccount();RefreshAvatars();message.Text="Signed out on this device.";if(previous!=null)await VaultRequest("/v1/auth/logout","POST",new {},previous);},"Sign out");
   label("Master password");root.Children.Add(password);
   var list=new ListBox {Height=125,Margin=new Thickness(0,8,0,6)};
   Action refresh=delegate{list.ItemsSource=null;if(payload!=null)list.ItemsSource=payload.Entries;};
   Action require=delegate{if(payload==null)throw new InvalidOperationException("Unlock your vault first.");};
   Func<Task> save=async delegate {require();var data=VaultCrypto.Json.Deserialize<VaultPayload>(VaultCrypto.Json.Serialize(payload));var key=vaultKey;var encrypted=await Task.Run(()=>VaultCrypto.Encrypt(data,key));if(live&&!privateMode&&!closing){VaultCrypto.Save(local,encrypted);NoteVaultChange();}};
   add(async delegate {if(File.Exists(local)&&File.Exists(PassphrasePendingPath)&&AutofillKeyStore.Exists(profile)){// Mica made this vault when it first saved a login; the passphrase is chosen now.
if(password.Password.Length<12)throw new ArgumentException("Choose a master password (12+ characters). You'll use it to view your saved passwords.");var pending=VaultCrypto.Json.Deserialize<VaultEnvelope>(File.ReadAllText(local));var chosen=password.Password;var opened=await Task.Run(()=>VaultCrypto.Decrypt(pending,AutofillKeyStore.Load(profile)));if(!live)return;var fresh=await Task.Run(()=>VaultCrypto.NewKey(chosen));chosen=null;if(!live)return;payload=opened;vaultKey=fresh;await save();File.Delete(PassphrasePendingPath);}
   else if(File.Exists(local)){var envelope=VaultCrypto.Json.Deserialize<VaultEnvelope>(File.ReadAllText(local));var master=password.Password;string key=null;var opened=await Task.Run(()=>VaultCrypto.Decrypt(envelope,key=VaultCrypto.KeyFor(envelope,master)));master=null;if(!live)return;payload=opened;vaultKey=key;}else{if(password.Password.Length<12)throw new ArgumentException("Use at least 12 characters for a new master password.");var master=password.Password;var key=await Task.Run(()=>VaultCrypto.NewKey(master));master=null;if(!live)return;payload=new VaultPayload();vaultKey=key;await save();}if(!live)return;ShareVault(payload,vaultKey);password.IsEnabled=false;refresh();UpdateVaultLock(true);message.Text="Vault unlocked. Use the lock to lock it again.";
    if(vaultHelloOptIn!=null&&vaultHelloOptIn.IsChecked==true) {
     bool verified=await helloVerifier.Verify(new System.Windows.Interop.WindowInteropHelper(vaultUnlockWindow).Handle,()=>live&&vaultUnlockWindow!=null);
     if(!live)return;
     if(verified){try{HelloUnlockStore.Save(profile,vaultKey);message.Text="Vault unlocked. Windows Hello enabled on this device.";}catch(Exception){message.Text="Vault unlocked. Could not save Windows Hello enrollment on this device.";}}
     else message.Text="Vault unlocked with master password. Windows Hello was not enabled.";
    }
    if(live)FinishVaultUnlock();},"Unlock / create local vault");root.Children.Add(list);
   label("Website (HTTPS), username and password");var origin=new TextBox {Margin=new Thickness(0,3,0,3),Padding=new Thickness(6)};var username=new TextBox {Margin=new Thickness(0,3,0,3),Padding=new Thickness(6)};var secret=new PasswordBox {Margin=new Thickness(0,3,0,3),Padding=new Thickness(6)};var totp=new TextBox {Margin=new Thickness(0,3,0,3),Padding=new Thickness(6),Tag="vault-totp"};root.Children.Add(origin);root.Children.Add(username);root.Children.Add(secret);
   list.SelectionChanged+=delegate {var e=list.SelectedItem as VaultEntry;if(e!=null){origin.Text=e.Origin;username.Text=e.Username;secret.Password=e.Password;totp.Text=e.Totp??"";}};
   vaultLockNow=delegate {
    if(payload!=null&&payload!=sessionVault){foreach(var e in payload.Entries){e.Password=null;e.Username=null;}payload.Entries.Clear();payload.Snapshot=null;}
    // Locking also turns off silent autofill: the device key goes, so the next fill asks to unlock.
    payload=null;vaultKey=null;sessionVault=null;sessionMaster=null;AutofillKeyStore.Forget(profile);list.SelectedItem=null;refresh();password.Clear();password.IsEnabled=true;secret.Clear();username.Clear();origin.Clear();totp.Clear();
    UpdateVaultLock(false);message.Text="Vault locked. Your account and sync stay available.";
   };
   add(delegate {require();list.SelectedItem=null;origin.Clear();username.Clear();secret.Clear();totp.Clear();return Task.FromResult(0);},"New credential");
   add(async delegate {require();var e=list.SelectedItem as VaultEntry;var o=VaultCrypto.Origin(origin.Text);string key=string.IsNullOrWhiteSpace(totp.Text)?null:Totp.Normalize(Totp.Parse(totp.Text));if(e==null){e=new VaultEntry {Id=Guid.NewGuid().ToString("N")};payload.Entries.Add(e);}e.Origin=o;e.Username=username.Text;e.Password=secret.Password;e.Totp=key;VaultCrypto.Touch(payload,e);await save();if(!live)return;refresh();message.Text="Credential saved encrypted on this device.";},"Save credential");
   add(async delegate {require();var e=list.SelectedItem as VaultEntry;if(e==null)throw new InvalidOperationException("Select a credential.");VaultCrypto.Forget(payload,e);await save();if(!live)return;refresh();secret.Clear();message.Text="Credential deleted.";},"Delete selected credential");
   add(delegate {require();var e=list.SelectedItem as VaultEntry;if(e==null)throw new InvalidOperationException("Select a credential.");CopyPassword(e.Password);message.Text="Password copied. It clears from the clipboard in 30 seconds and stays out of clipboard history.";return Task.FromResult(0);},"Copy selected password");
   add(async delegate {require();var e=list.SelectedItem as VaultEntry;var v=CurrentView();if(e==null||v==null||v.CoreWebView2==null||!VaultCrypto.Matches(e.Origin,v.CoreWebView2.Source))throw new InvalidOperationException("Select a credential matching the current HTTPS website.");var j=VaultCrypto.Json;var result=await v.ExecuteScriptAsync("(()=>{if(window.top!==window||location.origin!=="+j.Serialize(e.Origin)+")return false;const p=document.querySelector('input[type=password]');if(!p)return false;const f=p.form||document;const u=f.querySelector('input[autocomplete=username],input[type=email],input[type=text]');const set=(e,v)=>{Object.getOwnPropertyDescriptor(HTMLInputElement.prototype,'value').set.call(e,v);e.dispatchEvent(new Event('input',{bubbles:true}));e.dispatchEvent(new Event('change',{bubbles:true}));};if(u)set(u,"+j.Serialize(e.Username)+");set(p,"+j.Serialize(e.Password)+");return true;})()");if(!live)return;message.Text=result=="true"?"Filled on the current website. Review before submitting.":"No supported login form found. Nothing was submitted.";},"Fill selected credential on current website");
   add(async delegate {require();payload.Snapshot=VaultCrypto.Json.Deserialize<BrowserState>(VaultCrypto.Json.Serialize(state));await save();if(!live)return;message.Text="Bookmarks, history, tabs and preferences captured inside the encrypted vault.";},"Capture browser snapshot");
   add(async delegate {require();if(payload.Snapshot==null)throw new InvalidOperationException("This vault has no browser snapshot.");VaultCrypto.ValidatePayload(payload);if(!ConfirmMica(dialog,"Restore browser snapshot","Replace the current tabs, bookmarks, history and preferences with this snapshot?","Restore snapshot")||!live)return;await ImportVaultSnapshot(payload.Snapshot);if(!live)return;message.Text="Browser snapshot restored.";},"Restore captured browser snapshot");
   pullRemote=async delegate {
    require();if(account==null)throw new InvalidOperationException("Sign in first.");
    remoteVersion=-1;knownAccount=null;var a=account;
    var remote=await FetchVerifiedVault(a);if(!live)return;
    if(remote.envelope!=null){VaultCrypto.FromServer(remote.envelope);
     // This device's key first, then the typed master password (the cloud copy may use a new salt), then ask.
     string master=vaultKey,pulledKey=null;VaultPayload pulled=null;Func<string,Task<VaultPayload>> open=given=>Task.Run(()=>VaultCrypto.Decrypt(remote.envelope,pulledKey=VaultCrypto.KeyFor(remote.envelope,given)));
     if(master!=null)try {pulled=await open(master);}catch(CryptographicException){}
     if(pulled==null&&!string.IsNullOrEmpty(password.Password)&&!VaultCrypto.IsKey(password.Password)){master=password.Password;try {pulled=await open(master);}catch(CryptographicException){}}
     if(pulled==null){
      if(!live)return;
      master=PromptRemoteMaster(dialog);if(master==null){message.Text="Signed in. Download cancelled; local data unchanged. Use Pull to retry.";return;}
      pulled=await open(master);
     }
     if(!live)return;
     if(pulled.AccountId!=a.id)throw new InvalidOperationException("This vault belongs to another account.");
     long seen=SeenRevision(a.id);
     if(pulled.Revision<seen&&!ConfirmMica(dialog,"Older cloud copy","The cloud has an older copy of your vault than this device has already seen (version "+pulled.Revision+", this device saw "+seen+"). Passwords you changed since could go back to old ones. Replace your local vault with it anyway?","Use older copy")){message.Text="Kept your local vault. The cloud copy is older than one this device has seen.";return;}
     if(!live)return;
     // Preserve the previous encrypted vault before replacing it; never upload this backup. Only the newest one is kept.
     string backup=null;
     if(File.Exists(local)){backup=Path.Combine(profile,"vault-before-pull-"+Guid.NewGuid().ToString("N")+".enc.json");File.Copy(local,backup);}
     foreach(var old in Directory.GetFiles(profile,"vault-before-pull-*.enc.json").OrderByDescending(f=>string.Equals(f,backup,StringComparison.OrdinalIgnoreCase)).ThenByDescending(File.GetCreationTimeUtc).Skip(1))try {File.Delete(old);}catch(Exception){}
     if(pulledKey!=vaultKey)HelloUnlockStore.Forget(profile); // Its key no longer opens the vault.
     VaultCrypto.Rekey(pulled,remote.envelope);VaultCrypto.Save(local,remote.envelope);if(!VaultCrypto.IsKey(master))password.Password=master;master=null;
     // Logins only this device had stay: the cloud copy is merged in, never put in their place.
     var mine=payload;
     RememberRevision(a.id,pulled.Revision);payload=pulled;vaultKey=pulledKey;int kept=VaultCrypto.MergePayload(payload,mine);if(kept>0)await save();if(!live)return;ShareVault(payload,vaultKey);refresh();
     message.Text="Your passwords and saved browser snapshot are downloaded."+(kept>0?" "+Localizer.Translate("Kept {0} logins only this device had; they upload in a moment.").Replace("{0}",kept.ToString()):"")+(backup!=null?" Previous local vault backed up on this device.":"")+" Restore snapshot when you want to apply its tabs and preferences.";
    }else message.Text="Signed in. No cloud data yet; your local data is unchanged and ready to upload.";
    remoteVersion=remote.version;knownAccount=a.id;
   };
   add(async delegate {await pullRemote();},"Pull remote vault (merge into local)");
   // The copy kept from before the last pull: opened with this device's key or a master password, and merged back in.
   add(async delegate {
    require();var saved=Directory.GetFiles(profile,"vault-before-pull-*.enc.json").OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
    if(saved==null)throw new InvalidOperationException("There's no copy from before a sync on this device.");
    var envelope=VaultCrypto.Json.Deserialize<VaultEnvelope>(File.ReadAllText(saved));VaultPayload old=null;
    var tries=new System.Collections.Generic.List<string>();if(vaultKey!=null)tries.Add(vaultKey);if(AutofillKeyStore.Exists(profile))try {tries.Add(AutofillKeyStore.Load(profile));}catch(Exception){}
    if(!string.IsNullOrEmpty(password.Password))tries.Add(password.Password);
    foreach(var given in tries){try {old=await Task.Run(()=>VaultCrypto.Decrypt(envelope,given));break;}catch(CryptographicException){}}
    if(old==null){if(!live)return;var typed=PromptRemoteMaster(dialog);if(typed==null){message.Text="Restore cancelled; nothing changed.";return;}old=await Task.Run(()=>VaultCrypto.Decrypt(envelope,typed));typed=null;}
    if(!live)return;int added=VaultCrypto.MergeEntries(payload,old.Entries);if(added>0)await save();if(!live)return;refresh();
    message.Text=added>0?Localizer.Translate("Brought back {0} logins from before the last sync.").Replace("{0}",added.ToString()):"Every login from before the last sync is already here.";
   },"Restore logins from before the last sync");
   // Sync: asks once, then fetches, merges and uploads in one go; no Pull first.
   add(async delegate {
    require();if(account==null)throw new InvalidOperationException("Sign in first.");
    if(!ConfirmMica(dialog,"Sync passwords","Merge the passwords on this device with your Mica account's and upload the result? Logins you deleted stay deleted; nothing else is removed.","Sync")||!live)return;
    var a=account;var mine=CopyVault(payload);var key=vaultKey;var outcome=await Task.Run(()=>SyncVaultCore(a,mine,key,15000));if(!live)return;
    // The cloud copy uses another master password: Pull asks for it once and joins the two, then sync again.
    if(outcome.Problem=="other-master"){await pullRemote();if(!live||payload==null||account==null)return;key=vaultKey;mine=CopyVault(payload);outcome=await Task.Run(()=>SyncVaultCore(a,mine,key,15000));if(!live)return;}
    if(outcome.Problem!=null)throw new InvalidOperationException(VaultSyncProblem(outcome.Problem));
    int changes=vaultChanges;vaultSyncApplying=true;try {TakeVaultSync(payload,outcome,a.id);await save();}finally {vaultSyncApplying=false;}
    if(!live)return;if(vaultChanges==changes)try {File.Delete(VaultSyncPendingPath);}catch(Exception){}
    remoteVersion=-1;refresh();message.Text="Passwords synced with your Mica account.";
   },"Sync passwords now");
   label("Windows manages supported passkeys. Mica does not store or sync passkeys in this password vault.");add(delegate {Process.Start(new ProcessStartInfo("ms-settings:passkeys"){UseShellExecute=true});return Task.FromResult(0);},"Open Windows passkey settings");root.Children.Add(message);
   dialog.Closed+=delegate {live=false;vaultCompleteSignIn=null;CloseVaultUnlock();password.Clear();secret.Clear();username.Clear();origin.Clear();totp.Clear();list.ItemsSource=null;if(payload!=null&&payload!=sessionVault){foreach(var e in payload.Entries){e.Password=null;e.Username=null;}payload.Entries.Clear();payload.Snapshot=null;}payload=null;vaultKey=null;if(account!=null)account.token=null;account=null;vaultWindow=null;activeVaultWindow=null;vaultLockNow=null;vaultOpenUnlock=null;vaultAdoptSession=null;};vaultAdoptSession=delegate {if(live&&payload!=null&&payload==sessionVault)refresh();};ArrangeVault(root,footer,message,accountLabel,password,list,origin,username,secret,totp);dialog.Show();
  }
 }
}





