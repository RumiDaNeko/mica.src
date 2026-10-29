using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
namespace Mica {
 sealed class FakeHelloVerifier:IHelloVerifier {
  internal bool Allowed=true,Ready=true;
  internal TaskCompletionSource<bool> Pending;
  public Task<bool> Available(Func<bool> live){return Task.FromResult(Ready&&live());}
  // Stands in for the Windows Hello key's signature, which the real verifier hands to HelloUnlockStore.
  public Task<bool> Verify(IntPtr owner,Func<bool> live){if(Pending!=null)return Pending.Task;bool ok=Allowed&&live();if(ok)WindowsHello.Prove(System.Text.Encoding.UTF8.GetBytes("test-only Hello signature"));return Task.FromResult(ok);}
 }
 partial class BrowserApp {
  async Task RunHelloChecks(List<string> log) {
   const string master="hello-test-only-password-42";
   var fake=new FakeHelloVerifier();helloVerifier=fake;
   try {
    ShowVault();await Task.Delay(250);
    Require(vaultUnlockWindow==null&&vaultWindow.IsEnabled&&!vaultUnlocked,"The vault asked for the master password just to open");
    vaultLockButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Task.Delay(150);
    Require(vaultUnlockWindow!=null&&!vaultWindow.IsEnabled&&VaultElements<PasswordBox>(vaultBody).Count()==1,"The lock did not present unlock as an exclusive popup");
    vaultUnlockWindow.Close();await Task.Delay(100);Require(vaultWindow!=null&&vaultWindow.IsEnabled&&!vaultUnlocked,"Cancelling unlock closed the vault or unlocked it");CloseVault();
    log.Add("PASS the vault opens without unlocking; the lock asks only when needed; cancel keeps it open and locked");
    ShowVault();await Task.Delay(100);vaultOpenUnlock();vaultMaster.Password=master;vaultHelloOptIn.IsChecked=true;
    vaultUnlockButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    await WaitFor(()=>vaultUnlockWindow==null&&File.Exists(HelloUnlockStore.PathFor(profile)),30000);
    var vault=VaultCrypto.Json.Deserialize<VaultEnvelope>(File.ReadAllText(Path.Combine(profile,"vault.enc.json")));
    Func<bool> refused=()=>{try {HelloUnlockStore.Load(profile);return false;}catch(System.Security.Cryptography.CryptographicException){return true;}};
    Require(refused(),"Enrollment opened without a Windows Hello signature");
    WindowsHello.Prove(System.Text.Encoding.UTF8.GetBytes("some other signature"));Require(refused(),"Enrollment opened with the wrong Windows Hello signature");
    await fake.Verify(IntPtr.Zero,()=>true);var enrolled=HelloUnlockStore.Load(profile);
    Require(VaultCrypto.IsKey(enrolled)&&!enrolled.Contains(master)&&VaultCrypto.Decrypt(vault,enrolled)!=null,"Enrollment did not keep a vault key that opens the vault");
    Require(!System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(HelloUnlockStore.PathFor(profile))).Contains(master),"Enrollment stored plaintext");
    // An older enrollment (master password behind path-only entropy) becomes a Hello-bound vault key on first use.
    byte[] legacyEntropy;using(var sha=System.Security.Cryptography.SHA256.Create())legacyEntropy=sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes("Mica Hello unlock v1|"+Path.GetFullPath(profile).ToUpperInvariant()));
    File.WriteAllBytes(HelloUnlockStore.PathFor(profile),System.Security.Cryptography.ProtectedData.Protect(System.Text.Encoding.UTF8.GetBytes(master),legacyEntropy,System.Security.Cryptography.DataProtectionScope.CurrentUser));
    await fake.Verify(IntPtr.Zero,()=>true);var migrated=HelloUnlockStore.Load(profile);var upgraded=File.ReadAllBytes(HelloUnlockStore.PathFor(profile));
    Require(VaultCrypto.IsKey(migrated)&&VaultCrypto.Decrypt(vault,migrated)!=null&&upgraded[0]=='M'&&upgraded[1]=='H'&&upgraded[2]==2,"Older enrollment was not upgraded to a Hello-bound vault key");
    bool legacyReadable=true;try {System.Security.Cryptography.ProtectedData.Unprotect(upgraded,legacyEntropy,System.Security.Cryptography.DataProtectionScope.CurrentUser);}catch(System.Security.Cryptography.CryptographicException){legacyReadable=false;}
    Require(!legacyReadable&&refused(),"Upgraded enrollment still opens without Windows Hello");
    log.Add("PASS Hello enrollment keeps a vault key, not the password, and opens only with the Hello signature; older enrollments upgrade");
    CloseVault();ShowVault();await Task.Delay(100);vaultOpenUnlock();var popup=vaultUnlockWindow;
    vaultMaster.Password="incorrect-password";vaultUnlockButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await WaitFor(()=>vaultUnlockBody.IsEnabled,30000);
    Require(vaultUnlockWindow==popup&&!vaultWindow.IsEnabled,"Wrong password dismissed unlock");vaultMaster.Clear();
    fake.Allowed=false;await UnlockVaultWithHello(vaultWindow,popup,vaultMaster,vaultUnlockButton,new TextBlock());
    Require(vaultUnlockWindow==popup&&vaultMaster.Password.Length==0,"Denied Hello read the master password");
    log.Add("PASS opt-in enrollment uses DPAPI; wrong password and denied Hello remain locked");
    fake.Allowed=true;await UnlockVaultWithHello(vaultWindow,popup,vaultMaster,vaultUnlockButton,new TextBlock());await WaitFor(()=>vaultUnlockWindow==null,30000);
    Require(vaultWindow.IsEnabled,"Successful Hello did not unlock vault");
    log.Add("PASS verified Hello decrypts the saved vault and dismisses popup (test verifier)");
    CloseVault();ShowVault();await Task.Delay(100);vaultOpenUnlock();popup=vaultUnlockWindow;var oldMaster=vaultMaster;
    fake.Pending=new TaskCompletionSource<bool>();var pending=UnlockVaultWithHello(vaultWindow,popup,oldMaster,vaultUnlockButton,new TextBlock());popup.Close();fake.Pending.SetResult(true);await pending;
    Require(!vaultUnlocked&&oldMaster.Password.Length==0,"Late Hello completion unlocked a cancelled prompt");fake.Pending=null;
    log.Add("PASS closing during Hello prevents a late result from reading enrollment");
    fake.Ready=false;ShowVault();await Task.Delay(100);vaultOpenUnlock();Require(!vaultHelloButton.IsEnabled,"Unavailable Hello remained enabled");
    VaultElements<Button>(vaultUnlockBody).Single(b=>b.Content as string=="Forget Windows Hello on this device").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    Require(!File.Exists(HelloUnlockStore.PathFor(profile)),"Forget retained device enrollment");vaultMaster.Password=master;vaultUnlockButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await WaitFor(()=>vaultUnlockWindow==null,30000);
    log.Add("PASS unavailable Hello preserves master fallback and enrollment can be forgotten");
    CloseVault();
    bool available=await new WindowsHello().Available(()=>true);log.Add("INFO native Windows Hello availability: "+available+"; biometric/PIN interaction not automated");
   } finally {CloseVault();helloVerifier=new WindowsHello();HelloUnlockStore.Forget(profile);var local=Path.Combine(profile,"vault.enc.json");if(File.Exists(local))File.Delete(local);}
  }
 }
}
