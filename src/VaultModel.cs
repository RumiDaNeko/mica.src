using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;
namespace Mica {
 public sealed class VaultEnvelope {
  public int format {get;set;} public string kdf {get;set;} public int iterations {get;set;} public string salt {get;set;} public string iv {get;set;} public string ciphertext {get;set;} public string mac {get;set;}
  // Logins added while the vault is locked: sealed to the vault's public key, so saving needs no
  // master password but reading them does. Outside the MAC; merged and cleared on the next unlock.
  public string inboxKey {get;set;} public List<string> inbox {get;set;}
 }
 public sealed class VaultEntry {
  public string Id {get;set;} public string Origin {get;set;} public string Username {get;set;} public string Password {get;set;}
  // The site's 2FA setup key (an otpauth link, see Totp), or null. A login may hold only this, with an empty password.
  public string Totp {get;set;}
  // When this login was last saved here or on another device (ms since 1970; 0 for logins saved before sync merged).
  public long Saved {get;set;}
  public override string ToString() {return Origin+"  ·  "+Username;}
 }
 public sealed class VaultPayload {
  public int Format {get;set;} public string AccountId {get;set;} public List<VaultEntry> Entries {get;set;} public BrowserState Snapshot {get;set;} public string InboxPrivate {get;set;}
  // Raised on every upload and covered by the MAC (it is inside the ciphertext), so a sync server can't pass off an older copy as current.
  public long Revision {get;set;}
  // Logins deleted on some device, as "origin\nusername\tms", so a merge doesn't bring them back from another copy.
  public List<string> Removed {get;set;}
  public VaultPayload() {Format=1;Entries=new List<VaultEntry>();}
 }
 public static class VaultCrypto {
  public const int Iterations=600000;
  public static readonly JavaScriptSerializer Json=new JavaScriptSerializer {MaxJsonLength=8*1024*1024};
  public static string Origin(string value) {Uri u;if(!Uri.TryCreate(value,UriKind.Absolute,out u)||u.Scheme!="https"||string.IsNullOrEmpty(u.Host)||!string.IsNullOrEmpty(u.UserInfo))throw new ArgumentException("Enter an HTTPS website address without user information.");return u.GetLeftPart(UriPartial.Authority).ToLowerInvariant();}
  public static bool Matches(string saved,string current) {try{return Origin(saved)==Origin(current);}catch(ArgumentException){return false;}}
  static byte[] Random(int size) {var bytes=new byte[size];using(var rng=RandomNumberGenerator.Create())rng.GetBytes(bytes);return bytes;}
  public static string RandomVerifier() {return Convert.ToBase64String(Random(32)).TrimEnd('=').Replace('+','-').Replace('/','_');}
  public static string Challenge(string verifier) {using(var h=SHA256.Create())return Convert.ToBase64String(h.ComputeHash(Encoding.ASCII.GetBytes(verifier))).TrimEnd('=').Replace('+','-').Replace('/','_');}
  // PBKDF2 through Windows CNG: the same bytes as Rfc2898DeriveBytes, roughly 20x faster than the
  // managed version (which took ~7 s for 600,000 rounds and made every unlock and autofill wait).
  [System.Runtime.InteropServices.DllImport("bcrypt.dll",CharSet=System.Runtime.InteropServices.CharSet.Unicode)] static extern int BCryptOpenAlgorithmProvider(out IntPtr handle,string algorithm,string implementation,int flags);
  [System.Runtime.InteropServices.DllImport("bcrypt.dll")] static extern int BCryptCloseAlgorithmProvider(IntPtr handle,int flags);
  [System.Runtime.InteropServices.DllImport("bcrypt.dll")] static extern int BCryptDeriveKeyPBKDF2(IntPtr prf,byte[] password,int passwordLength,byte[] salt,int saltLength,ulong iterations,byte[] key,int keyLength,int flags);
  internal static byte[] NativePbkdf2(byte[] password,byte[] salt,int iterations,int length) {
   IntPtr prf;if(BCryptOpenAlgorithmProvider(out prf,"SHA256",null,8)!=0)return null; // 8 = BCRYPT_ALG_HANDLE_HMAC_FLAG
   try {var key=new byte[length];return BCryptDeriveKeyPBKDF2(prf,password,password.Length,salt,salt.Length,(ulong)iterations,key,length,0)==0?key:null;}
   finally {BCryptCloseAlgorithmProvider(prf,0);}
  }
  static byte[] Derive(string password,byte[] salt) {if(string.IsNullOrEmpty(password))throw new ArgumentException("Enter your vault master password.");byte[] secret=Encoding.UTF8.GetBytes(password);try {var fast=NativePbkdf2(secret,salt,Iterations,64);if(fast!=null)return fast;}catch(Exception){}finally {Array.Clear(secret,0,secret.Length);}using(var k=new Rfc2898DeriveBytes(password,salt,Iterations,HashAlgorithmName.SHA256))return k.GetBytes(64);}
  // A vault key stands in for the master password on this device: the vault's salt and the key derived from
  // the password (or a random key for a vault with no passphrase yet), never the password itself.
  const string KeyPrefix="\u0001MicaVaultKey1:";
  public static bool IsKey(string secret) {return secret!=null&&secret.StartsWith(KeyPrefix,StringComparison.Ordinal);}
  public static long Now() {return (long)(DateTime.UtcNow-new DateTime(1970,1,1,0,0,0,DateTimeKind.Utc)).TotalMilliseconds;}
  static string LoginKey(VaultEntry e) {return (e.Origin??"").ToLowerInvariant()+"\n"+(e.Username??"");}
  static VaultEntry SameLogin(VaultPayload p,VaultEntry e) {string key=LoginKey(e);return p.Entries.FirstOrDefault(x=>LoginKey(x)==key);}
  static VaultEntry Copy(VaultEntry e,string id) {return new VaultEntry {Id=id,Origin=e.Origin,Username=e.Username,Password=e.Password??"",Totp=e.Totp,Saved=e.Saved};}
  static bool Empty(VaultEntry e) {return e==null||(string.IsNullOrEmpty(e.Password)&&string.IsNullOrEmpty(e.Totp));}
  // A login was saved (now): it no longer counts as deleted.
  public static void Touch(VaultPayload p,VaultEntry e) {e.Saved=Now();if(p.Removed!=null){string key=LoginKey(e)+"\t";p.Removed.RemoveAll(r=>r.StartsWith(key,StringComparison.Ordinal));}}
  // A login was deleted (now): other copies of the vault drop it too when they merge with this one.
  public static void Forget(VaultPayload p,VaultEntry e) {p.Entries.Remove(e);if(p.Removed==null)p.Removed=new List<string>();string key=LoginKey(e);p.Removed.RemoveAll(r=>r.StartsWith(key+"\t",StringComparison.Ordinal));p.Removed.Add(key+"\t"+Now());if(p.Removed.Count>2000)p.Removed.RemoveRange(0,p.Removed.Count-2000);}
  // Logins in `from` that `into` lacks are added, as fresh saves (a restore). A login both have keeps `into`'s
  // password and gains `from`'s 2FA key if it had none. Nothing is removed. Returns how many were added.
  public static int MergeEntries(VaultPayload into,IEnumerable<VaultEntry> from) {
   if(into.Entries==null)into.Entries=new List<VaultEntry>();int added=0;
   foreach(var e in from??new VaultEntry[0]) {
    if(Empty(e))continue;var same=SameLogin(into,e);
    if(same!=null){if(string.IsNullOrEmpty(same.Totp)&&!string.IsNullOrEmpty(e.Totp))same.Totp=e.Totp;if(string.IsNullOrEmpty(same.Password)&&!string.IsNullOrEmpty(e.Password))same.Password=e.Password;continue;}
    var copy=Copy(e,string.IsNullOrEmpty(e.Id)||into.Entries.Any(x=>x.Id==e.Id)?Guid.NewGuid().ToString("N"):e.Id);into.Entries.Add(copy);Touch(into,copy);added++;
   }
   return added;
  }
  // Two copies of one vault (this device's and the cloud's) become one: every login either has, the newest
  // save of a login both have, minus logins deleted after they were last saved. Returns how many `into` gained.
  public static int MergePayload(VaultPayload into,VaultPayload from) {
   if(into.Entries==null)into.Entries=new List<VaultEntry>();if(from==null)return 0;int before=into.Entries.Count,replaced=0;
   var removed=new Dictionary<string,long>();
   foreach(var r in (into.Removed??new List<string>()).Concat(from.Removed??new List<string>())){int tab=r.LastIndexOf('\t');long at;if(tab>0&&long.TryParse(r.Substring(tab+1),out at)){string key=r.Substring(0,tab);long seen;if(!removed.TryGetValue(key,out seen)||at>seen)removed[key]=at;}}
   foreach(var e in from.Entries??new List<VaultEntry>()) {
    if(Empty(e))continue;var same=SameLogin(into,e);
    if(same==null){into.Entries.Add(Copy(e,into.Entries.Any(x=>x.Id==e.Id)||string.IsNullOrEmpty(e.Id)?Guid.NewGuid().ToString("N"):e.Id));continue;}
    if(e.Saved>same.Saved){same.Password=e.Password??"";same.Totp=e.Totp;same.Saved=e.Saved;replaced++;}
    else if(string.IsNullOrEmpty(same.Totp)&&!string.IsNullOrEmpty(e.Totp))same.Totp=e.Totp;
   }
   into.Entries.RemoveAll(x=>{long at;return removed.TryGetValue(LoginKey(x),out at)&&at>=x.Saved;});
   into.Removed=removed.OrderBy(p=>p.Value).Skip(Math.Max(0,removed.Count-2000)).Select(p=>p.Key+"\t"+p.Value).ToList();
   if(into.Snapshot==null)into.Snapshot=from.Snapshot;
   return Math.Max(0,into.Entries.Count-before)+replaced;
  }
  static string KeyText(byte[] salt,byte[] key) {return KeyPrefix+Convert.ToBase64String(salt)+":"+Convert.ToBase64String(key);}
  static byte[] KeyPart(string secret,int index,int size) {var parts=secret.Substring(KeyPrefix.Length).Split(':');if(parts.Length!=2)throw new CryptographicException("Invalid vault key.");return Decode(parts[index],size);}
  static byte[] Key(string secret,byte[] salt) {if(!IsKey(secret))return Derive(secret,salt);if(Convert.ToBase64String(KeyPart(secret,0,16))!=Convert.ToBase64String(salt))throw new CryptographicException("Wrong master password or damaged vault.");return KeyPart(secret,1,64);}
  public static string NewKey(string password) {var salt=Random(16);var key=Derive(password,salt);try {return KeyText(salt,key);}finally {Array.Clear(key,0,key.Length);}}
  public static string NewDeviceKey() {var key=Random(64);try {return KeyText(Random(16),key);}finally {Array.Clear(key,0,key.Length);}}
  // Checks a password (or key) against the vault and returns its vault key. Encrypting with that key keeps the salt, so the password still opens the vault.
  public static string KeyFor(VaultEnvelope e,string secret) {Validate(e);var salt=Decode(e.salt,16);var key=Key(secret,salt);try {Authenticate(e,key);return IsKey(secret)?secret:KeyText(salt,key);}finally {Array.Clear(key,0,key.Length);}}
  static void Authenticate(VaultEnvelope e,byte[] key) {var auth=Part(key,32);try {byte[] expected;using(var h=new HMACSHA256(auth))expected=h.ComputeHash(Canonical(e));var actual=Decode(e.mac,32);int diff=0;for(int i=0;i<32;i++)diff|=expected[i]^actual[i];if(diff!=0)throw new CryptographicException("Wrong master password or damaged vault.");}finally {Array.Clear(auth,0,auth.Length);}}
  // MAC canonical bytes: UTF8("MicaVault\\n1\\nPBKDF2-SHA256\\n600000\\n" + salt-base64 + "\\n" + iv-base64 + "\\n" + ciphertext-base64). No terminal newline.
  static byte[] Canonical(VaultEnvelope e) {return Encoding.UTF8.GetBytes("MicaVault\n"+e.format+"\n"+e.kdf+"\n"+e.iterations+"\n"+e.salt+"\n"+e.iv+"\n"+e.ciphertext);}
  static byte[] Part(byte[] bytes,int start) {var r=new byte[32];Buffer.BlockCopy(bytes,start,r,0,32);return r;}
  static byte[] Decode(string s,int size) {byte[] b;try{b=Convert.FromBase64String(s??"");}catch(FormatException){throw new CryptographicException("Invalid vault format.");}if((size>0&&b.Length!=size)||Convert.ToBase64String(b)!=s)throw new CryptographicException("Invalid vault format.");return b;}
  public static void Validate(VaultEnvelope e) {if(e==null||e.format!=1||e.kdf!="PBKDF2-SHA256"||e.iterations!=Iterations)throw new CryptographicException("Unsupported vault format.");Decode(e.salt,16);Decode(e.iv,16);Decode(e.mac,32);var c=Decode(e.ciphertext,0);if(c.Length==0||c.Length%16!=0||c.Length>4*1024*1024)throw new CryptographicException("Invalid vault size.");}
  public static VaultEnvelope Encrypt(VaultPayload payload,string password) {
   ValidatePayload(payload);var inboxEnvelope=new VaultEnvelope();GiveInboxKey(payload,inboxEnvelope);var salt=IsKey(password)?KeyPart(password,0,16):Random(16);var iv=Random(16);var key=Key(password,salt);byte[] plain=Encoding.UTF8.GetBytes(Json.Serialize(payload)),enc=null,auth=null;
   try {enc=Part(key,0);auth=Part(key,32);var e=new VaultEnvelope {format=1,kdf="PBKDF2-SHA256",iterations=Iterations,salt=Convert.ToBase64String(salt),iv=Convert.ToBase64String(iv)};using(var aes=Aes.Create()){aes.KeySize=256;aes.Mode=CipherMode.CBC;aes.Padding=PaddingMode.PKCS7;aes.Key=enc;aes.IV=iv;using(var c=aes.CreateEncryptor())e.ciphertext=Convert.ToBase64String(c.TransformFinalBlock(plain,0,plain.Length));}using(var h=new HMACSHA256(auth))e.mac=Convert.ToBase64String(h.ComputeHash(Canonical(e)));e.inboxKey=inboxEnvelope.inboxKey;return e;}
   finally {Array.Clear(key,0,key.Length);Array.Clear(plain,0,plain.Length);if(enc!=null)Array.Clear(enc,0,enc.Length);if(auth!=null)Array.Clear(auth,0,auth.Length);}
  }
  public static VaultPayload Decrypt(VaultEnvelope e,string password) {
   Validate(e);var key=Key(password,Decode(e.salt,16));byte[] enc=null,plain=null;
   try {Authenticate(e,key);enc=Part(key,0);using(var aes=Aes.Create()){aes.KeySize=256;aes.Mode=CipherMode.CBC;aes.Padding=PaddingMode.PKCS7;aes.Key=enc;aes.IV=Decode(e.iv,16);var cipher=Decode(e.ciphertext,0);using(var c=aes.CreateDecryptor())plain=c.TransformFinalBlock(cipher,0,cipher.Length);}var p=Json.Deserialize<VaultPayload>(Encoding.UTF8.GetString(plain));ValidatePayload(p);MergeInbox(e,p);return p;}
   finally {Array.Clear(key,0,key.Length);if(enc!=null)Array.Clear(enc,0,enc.Length);if(plain!=null)Array.Clear(plain,0,plain.Length);}
  }
  public static void ValidatePayload(VaultPayload p) {if(p==null||p.Format!=1||p.Entries==null||p.Entries.Count>10000)throw new ArgumentException("Invalid vault payload.");foreach(var e in p.Entries)if(e==null||e.Origin!=Origin(e.Origin)||e.Username==null||e.Password==null||e.Username.Length>4096||e.Password.Length>65536||(e.Totp!=null&&e.Totp.Length>1024))throw new ArgumentException("Invalid credential.");if(p.Snapshot!=null){var s=p.Snapshot;if(s.Tabs==null||s.Bookmarks==null||s.History==null||s.Tabs.Count>1000||s.Bookmarks.Count>10000||s.History.Count>500)throw new ArgumentException("Invalid browser snapshot.");if(s.Tabs.Any(t=>t==null||(!Navigation.IsWeb(t.Url)&&!Navigation.IsInternal(t.Url)))||s.Bookmarks.Concat(s.History).Any(t=>t==null||!Navigation.IsWeb(t.Url)))throw new ArgumentException("Unsafe URL in snapshot.");}}
  // --- Inbox: add to a locked vault without its master password. ---
  // The inbox and its key sit outside the MAC and are never uploaded, so a vault from the sync server can't
  // carry them: a server could otherwise slip in logins of its choosing, or its own key for later captures.
  // Call before decrypting what the server sent; after, Rekey gives it back this vault's own key.
  public static void FromServer(VaultEnvelope e) {e.inbox=null;e.inboxKey=null;}
  public static void Rekey(VaultPayload payload,VaultEnvelope e) {if(!string.IsNullOrEmpty(payload.InboxPrivate))GiveInboxKey(payload,e);}
  static void GiveInboxKey(VaultPayload payload,VaultEnvelope e) {
   if(string.IsNullOrEmpty(payload.InboxPrivate))using(var rsa=new RSACryptoServiceProvider(3072)){rsa.PersistKeyInCsp=false;payload.InboxPrivate=rsa.ToXmlString(true);}
   using(var rsa=new RSACryptoServiceProvider()){rsa.PersistKeyInCsp=false;rsa.FromXmlString(payload.InboxPrivate);e.inboxKey=rsa.ToXmlString(false);}
  }
  public static string Seal(string publicKey,VaultEntry entry) {
   byte[] keys=Random(64),iv=Random(16),plain=Encoding.UTF8.GetBytes(Json.Serialize(entry)),wrapped,cipher,mac;
   try {
    using(var rsa=new RSACryptoServiceProvider()){rsa.PersistKeyInCsp=false;rsa.FromXmlString(publicKey);wrapped=rsa.Encrypt(keys,true);}
    using(var aes=Aes.Create()){aes.KeySize=256;aes.Mode=CipherMode.CBC;aes.Padding=PaddingMode.PKCS7;aes.Key=Part(keys,0);aes.IV=iv;using(var c=aes.CreateEncryptor())cipher=c.TransformFinalBlock(plain,0,plain.Length);}
    using(var h=new HMACSHA256(Part(keys,32)))mac=h.ComputeHash(iv.Concat(cipher).ToArray());
    return Convert.ToBase64String(wrapped)+"."+Convert.ToBase64String(iv)+"."+Convert.ToBase64String(cipher)+"."+Convert.ToBase64String(mac);
   }finally {Array.Clear(keys,0,keys.Length);Array.Clear(plain,0,plain.Length);}
  }
  static VaultEntry Open(string privateKey,string item) {
   var parts=(item??"").Split('.');if(parts.Length!=4)throw new CryptographicException("Invalid inbox item.");
   byte[] keys;using(var rsa=new RSACryptoServiceProvider()){rsa.PersistKeyInCsp=false;rsa.FromXmlString(privateKey);keys=rsa.Decrypt(Convert.FromBase64String(parts[0]),true);}
   try {
    if(keys.Length!=64)throw new CryptographicException("Invalid inbox item.");
    byte[] iv=Convert.FromBase64String(parts[1]),cipher=Convert.FromBase64String(parts[2]),mac=Convert.FromBase64String(parts[3]),expected;
    using(var h=new HMACSHA256(Part(keys,32)))expected=h.ComputeHash(iv.Concat(cipher).ToArray());
    int diff=mac.Length^32;for(int i=0;i<Math.Min(32,mac.Length);i++)diff|=expected[i]^mac[i];if(diff!=0)throw new CryptographicException("Damaged inbox item.");
    byte[] plain;using(var aes=Aes.Create()){aes.KeySize=256;aes.Mode=CipherMode.CBC;aes.Padding=PaddingMode.PKCS7;aes.Key=Part(keys,0);aes.IV=iv;using(var c=aes.CreateDecryptor())plain=c.TransformFinalBlock(cipher,0,cipher.Length);}
    try {return Json.Deserialize<VaultEntry>(Encoding.UTF8.GetString(plain));}finally {Array.Clear(plain,0,plain.Length);}
   }finally {Array.Clear(keys,0,keys.Length);}
  }
  static void MergeInbox(VaultEnvelope e,VaultPayload p) {
   if(e.inbox==null||e.inbox.Count==0||string.IsNullOrEmpty(p.InboxPrivate))return;
   foreach(var item in e.inbox) {
    VaultEntry login;try {login=Open(p.InboxPrivate,item);if(login==null||login.Origin!=Origin(login.Origin)||login.Username==null||login.Password==null)continue;}catch(Exception){continue;}
    var entry=p.Entries.FirstOrDefault(x=>x.Origin==login.Origin&&x.Username==login.Username);
    if(entry==null){entry=new VaultEntry {Id=Guid.NewGuid().ToString("N"),Origin=login.Origin,Username=login.Username};p.Entries.Add(entry);}
    entry.Password=login.Password;
   }
  }
  public static bool HasInbox(VaultEnvelope e) {return e!=null&&e.inbox!=null&&e.inbox.Count>0;}
  // Adds a login to the vault file without unlocking it. False when the vault has no inbox key yet.
  public static bool AddToInbox(string path,VaultEntry entry) {
   var e=Json.Deserialize<VaultEnvelope>(File.ReadAllText(path));Validate(e);
   if(string.IsNullOrEmpty(e.inboxKey))return false;if(e.inbox==null)e.inbox=new List<string>();if(e.inbox.Count>=500)return false;
   e.inbox.Add(Seal(e.inboxKey,entry));Save(path,e);return true;
  }
  // What the sync service stores: the authenticated vault only, never the local inbox.
  public static Dictionary<string,object> ForSync(VaultEnvelope e) {return new Dictionary<string,object> {{"format",e.format},{"kdf",e.kdf},{"iterations",e.iterations},{"salt",e.salt},{"iv",e.iv},{"ciphertext",e.ciphertext},{"mac",e.mac}};}
  public static void Save(string path,VaultEnvelope envelope) {Validate(envelope);var temp=path+".tmp";File.WriteAllText(temp,Json.Serialize(envelope));if(File.Exists(path))File.Replace(temp,path,null);else File.Move(temp,path);}
 }
}
