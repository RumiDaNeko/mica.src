using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace Mica {
 // Passwords follow the Mica account on their own while you're signed in: a moment after a login is saved,
 // edited or deleted, once when the vault first opens, and when the last window closes if that didn't get
 // through. A sync merges both copies (newest save of each login wins, deletions travel too) and uploads the
 // result; it never asks for the master password, using the key the open vault already has. A cloud copy
 // under another master password waits for Passwords & sync, which asks for it once.
 partial class BrowserApp {
  sealed class VaultSyncOutcome {public VaultPayload Merged; public long Revision; public string Problem;}
  static DispatcherTimer vaultSyncTimer;
  static bool vaultSyncing,vaultSyncApplying,vaultSyncedOnce;
  static int vaultChanges;
  string VaultSyncPendingPath {get {return Path.Combine(profile,"vault-sync-pending");}}
  bool VaultSyncEnabled {get {return !privateMode&&(!smoke||vaultTestApi!=null);}}
  static VaultPayload CopyVault(VaultPayload p) {return VaultCrypto.Json.Deserialize<VaultPayload>(VaultCrypto.Json.Serialize(p));}
  // Called whenever the vault file is written with a change of yours.
  void NoteVaultChange() {
   if(privateMode||vaultSyncApplying)return;vaultChanges++;
   try {File.WriteAllText(VaultSyncPendingPath,"");}catch(Exception){}
   if(!VaultSyncEnabled)return;
   if(vaultSyncTimer==null){vaultSyncTimer=new DispatcherTimer(DispatcherPriority.Background,window.Dispatcher) {Interval=TimeSpan.FromSeconds(2)};vaultSyncTimer.Tick+=delegate {vaultSyncTimer.Stop();SyncVaultInBackground();};}
   vaultSyncTimer.Stop();vaultSyncTimer.Start();
  }
  // The first time the vault opens in this run, it catches up with the cloud.
  void SyncVaultOnceOpened() {if(vaultSyncedOnce||!VaultSyncEnabled)return;vaultSyncedOnce=true;SyncVaultInBackground();}
  string VaultHttp(string route,string method,object body,VaultAccount account,int timeout) {
   var request=(HttpWebRequest)WebRequest.Create(VaultApi+route);request.Method=method;request.AllowAutoRedirect=false;request.Timeout=timeout;request.ReadWriteTimeout=timeout;request.Accept="application/json";
   request.Headers[HttpRequestHeader.Authorization]="Bearer "+account.token;
   if(body!=null){var bytes=Encoding.UTF8.GetBytes(VaultCrypto.Json.Serialize(body));if(bytes.Length>2*1024*1024)throw new InvalidOperationException("Vault is too large to sync (2 MB limit).");request.ContentType="application/json";request.ContentLength=bytes.Length;using(var stream=request.GetRequestStream())stream.Write(bytes,0,bytes.Length);}
   using(var response=(HttpWebResponse)request.GetResponse())using(var reader=new StreamReader(response.GetResponseStream(),Encoding.UTF8)){var text=reader.ReadToEnd();if(text.Length>4*1024*1024)throw new InvalidOperationException("Sync response too large.");return text;}
  }
  // Off the UI thread: fetch the cloud copy, merge it with `mine` (a copy), upload the result. A copy that
  // changed in between (409) is fetched and merged once more.
  VaultSyncOutcome SyncVaultCore(VaultAccount account,VaultPayload mine,string key,int timeout) {
   for(int attempt=0;attempt<2;attempt++) {
    var verified=VaultCrypto.Json.Deserialize<AccountResult>(VaultHttp("/v1/account","GET",null,account,timeout));
    if(verified==null||verified.user==null||verified.user.id!=account.id)return new VaultSyncOutcome {Problem="account"};
    var remote=VaultCrypto.Json.Deserialize<RemoteVault>(VaultHttp("/v1/vault","GET",null,account,timeout));
    if(remote==null||remote.version<0)return new VaultSyncOutcome {Problem="invalid"};
    var merged=CopyVault(mine);
    if(remote.envelope!=null) {
     VaultCrypto.FromServer(remote.envelope);VaultPayload cloud;
     try {cloud=VaultCrypto.Decrypt(remote.envelope,key);}catch(CryptographicException){return new VaultSyncOutcome {Problem="other-master"};}
     if(!string.IsNullOrEmpty(cloud.AccountId)&&cloud.AccountId!=account.id)return new VaultSyncOutcome {Problem="other-account"};
     VaultCrypto.MergePayload(merged,cloud);merged.Revision=Math.Max(merged.Revision,cloud.Revision);
    }
    if(!string.IsNullOrEmpty(merged.AccountId)&&merged.AccountId!=account.id)return new VaultSyncOutcome {Problem="other-account"};
    merged.AccountId=account.id;merged.Revision=Math.Max(merged.Revision,SeenRevision(account.id))+1;
    var envelope=VaultCrypto.Encrypt(CopyVault(merged),key);
    try {
     var reply=VaultCrypto.Json.Deserialize<RemoteVault>(VaultHttp("/v1/vault","PUT",new {version=remote.version,envelope=VaultCrypto.ForSync(envelope)},account,timeout));
     if(reply==null||reply.version<=remote.version)return new VaultSyncOutcome {Problem="unverified"};
     return new VaultSyncOutcome {Merged=merged,Revision=merged.Revision};
    }catch(WebException e){var response=e.Response as HttpWebResponse;bool conflict=response!=null&&response.StatusCode==HttpStatusCode.Conflict;if(response!=null)response.Close();if(!conflict||attempt>0)throw;}
   }
   return new VaultSyncOutcome {Problem="busy"};
  }
  // What the cloud now holds goes into the open vault, keeping anything saved here while the sync ran.
  void TakeVaultSync(VaultPayload live,VaultSyncOutcome outcome,string accountId) {
   VaultCrypto.MergePayload(live,outcome.Merged);live.AccountId=accountId;live.Revision=Math.Max(live.Revision,outcome.Revision);RememberRevision(accountId,outcome.Revision);
  }
  internal static string VaultSyncProblem(string problem) {
   if(problem=="other-master")return "Passwords didn't sync: your Mica account's vault uses another master password. Open Passwords & sync once to join them.";
   if(problem=="other-account")return "Passwords didn't sync: this vault belongs to another Mica account.";
   return "Passwords didn't sync. Mica tries again next time.";
  }
  async void SyncVaultInBackground() {
   if(vaultSyncing||privateMode||closing||!VaultSyncEnabled)return;
   var account=ReadVaultAccount();var live=sessionVault;string key=sessionMaster;
   if(account==null||string.IsNullOrEmpty(account.token)||live==null||key==null)return;
   vaultSyncing=true;int changes=vaultChanges;
   try {
    var mine=CopyVault(live);var outcome=await Task.Run(()=>SyncVaultCore(account,mine,key,15000));if(closing)return;
    if(outcome.Problem!=null){if(outcome.Problem=="other-master"||outcome.Problem=="other-account")Status(VaultSyncProblem(outcome.Problem));return;}
    if(sessionVault!=live)return;
    vaultSyncApplying=true;try {TakeVaultSync(live,outcome,account.id);await SaveSessionVault(false);}finally {vaultSyncApplying=false;}
    if(vaultChanges==changes)try {File.Delete(VaultSyncPendingPath);}catch(Exception){}
    foreach(var browser in Program.Browsers.Where(b=>!b.closing&&!b.privateMode&&b.profile==profile))if(browser.vaultAdoptSession!=null)browser.vaultAdoptSession();
   }catch(Exception){} // Offline: it stays pending and goes out on the next save or when Mica closes.
   finally {vaultSyncing=false;}
  }
  // On close there is no "later": the last window sends what is pending, but never holds itself open long.
  void SyncVaultOnClose() {
   if(!VaultSyncEnabled||vaultSyncing||!File.Exists(VaultSyncPendingPath))return;
   if(Program.Browsers.Any(b=>b!=this&&!b.closing&&!b.privateMode&&b.profile==profile))return;
   var account=ReadVaultAccount();var live=sessionVault;string key=sessionMaster;
   if(account==null||string.IsNullOrEmpty(account.token)||live==null||key==null)return;
   try {
    var outcome=SyncVaultCore(account,CopyVault(live),key,3000);if(outcome.Problem!=null)return;
    TakeVaultSync(live,outcome,account.id);VaultCrypto.Save(VaultFile,VaultCrypto.Encrypt(CopyVault(live),key));File.Delete(VaultSyncPendingPath);
   }catch(Exception){}
  }
 }
}
