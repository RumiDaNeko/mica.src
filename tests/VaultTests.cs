using System;
using System.Security.Cryptography;
using Mica;
class VaultTests {
 static int checks;
 static void Check(bool b,string name){if(!b)throw new Exception(name);checks++;}
 static void Reject(Action action,string name){try{action();}catch(Exception){checks++;return;}throw new Exception("Expected rejection: "+name);}
 static void Main(){
  Check(VaultCrypto.Origin("https://EXAMPLE.com/path?q=1")=="https://example.com","canonical origin");
  Check(VaultCrypto.Matches("https://example.com","https://example.com/a"),"same origin");
  Check(!VaultCrypto.Matches("https://example.com","https://evil.example.com"),"subdomain blocked");
  Check(!VaultCrypto.Matches("https://example.com","http://example.com"),"http blocked");
  Check(!VaultCrypto.Matches("https://example.com","https://example.com:444"),"port blocked");
  Reject(delegate{VaultCrypto.Origin("https://user:pw@example.com");},"userinfo");
  var p=new VaultPayload();p.Entries.Add(new VaultEntry {Id="1",Origin="https://example.com",Username="demo",Password="secret-123"});
  var e=VaultCrypto.Encrypt(p,"test master password");var d=VaultCrypto.Decrypt(e,"test master password");Check(d.Entries[0].Password=="secret-123","roundtrip");
  Reject(delegate{VaultCrypto.Decrypt(e,"wrong password");},"wrong password");
  var tampered=VaultCrypto.Json.Deserialize<VaultEnvelope>(VaultCrypto.Json.Serialize(e));var c=Convert.FromBase64String(tampered.ciphertext);c[0]^=1;tampered.ciphertext=Convert.ToBase64String(c);Reject(delegate{VaultCrypto.Decrypt(tampered,"test master password");},"cipher tamper");
  tampered=VaultCrypto.Json.Deserialize<VaultEnvelope>(VaultCrypto.Json.Serialize(e));tampered.iterations=1;Reject(delegate{VaultCrypto.Decrypt(tampered,"test master password");},"metadata tamper");
  tampered=VaultCrypto.Json.Deserialize<VaultEnvelope>(VaultCrypto.Json.Serialize(e));c=Convert.FromBase64String(tampered.iv);c[0]^=1;tampered.iv=Convert.ToBase64String(c);Reject(delegate{VaultCrypto.Decrypt(tampered,"test master password");},"iv tamper");
  p.Snapshot=new BrowserState();p.Snapshot.Tabs.Add(new PageEntry {Url="file:///C:/secret"});Reject(delegate{VaultCrypto.ValidatePayload(p);},"snapshot unsafe url");
  p.Snapshot.Tabs.Clear();p.Snapshot.Tabs.Add(new PageEntry {Url="mica://home"});VaultCrypto.ValidatePayload(p);checks++;
  p.Entries[0].Origin="https://example.com/path";Reject(delegate{VaultCrypto.ValidatePayload(p);},"noncanonical saved origin");
  Console.WriteLine("Vault checks passed: "+checks);
 }
}
