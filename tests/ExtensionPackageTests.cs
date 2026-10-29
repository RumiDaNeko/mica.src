using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Mica;
class ExtensionPackageTests {
 static int count;static string root=Path.Combine(Path.GetTempPath(),"mica-package-test-"+Guid.NewGuid().ToString("N"));
 static void Assert(bool pass,string label){if(!pass)throw new Exception(label);count++;}
 static void Reject(Action action,string label){try{action();}catch(Exception e){if(e is InvalidDataException||e is FormatException||e is IOException||e is ArgumentException){count++;return;}throw;}throw new Exception("Accepted "+label);}
 static byte[] Join(params byte[][] arrays){return arrays.SelectMany(a=>a).ToArray();}
 static byte[] Len(int n){if(n<128)return new[]{(byte)n};if(n<256)return new[]{(byte)129,(byte)n};return new[]{(byte)130,(byte)(n>>8),(byte)n};}
 static byte[] Der(int tag,byte[] data){return Join(new[]{(byte)tag},Len(data.Length),data);}
 static byte[] Integer(byte[] data){return Der(2,data[0]>=128?Join(new byte[]{0},data):data);}
 static byte[] Var(int value){using(var s=new MemoryStream()){do{byte b=(byte)(value&127);value>>=7;s.WriteByte((byte)(b|(value>0?128:0)));}while(value>0);return s.ToArray();}}
 static byte[] Field(int key,byte[] data){return Join(Var((key<<3)|2),Var(data.Length),data);}
 static byte[] Spki(RSACryptoServiceProvider rsa){var p=rsa.ExportParameters(false);return Der(48,Join(Der(48,Join(Der(6,new byte[]{42,134,72,134,247,13,1,1,1}),Der(5,new byte[0]))),Der(3,Join(new byte[]{0},Der(48,Join(Integer(p.Modulus),Integer(p.Exponent)))))));}
 static byte[] Zip(string extra,int entries,int bytes){using(var s=new MemoryStream()){using(var z=new ZipArchive(s,ZipArchiveMode.Create,true)){using(var w=new StreamWriter(z.CreateEntry("manifest.json").Open()))w.Write("{\"manifest_version\":3,\"name\":\"Fixture\",\"version\":\"1.2\",\"permissions\":[\"storage\"],\"host_permissions\":[\"https://example.com/*\"],\"content_scripts\":[{\"matches\":[\"https://example.org/*\"]}]}");if(extra!=null){var e=z.CreateEntry(extra);using(var w=e.Open()){if(bytes>0){byte[] b=new byte[65536];while(bytes>0){int n=Math.Min(bytes,b.Length);w.Write(b,0,n);bytes-=n;}}}}for(int i=0;i<entries;i++)z.CreateEntry("file"+i);}return s.ToArray();}}
 static byte[] Package(RSACryptoServiceProvider rsa,byte[] key,byte[] zip,byte[] declared){byte[] signed=Field(1,declared);byte[] message=Join(Encoding.ASCII.GetBytes("CRX3 SignedData\0"),BitConverter.GetBytes(signed.Length),signed,zip);byte[] signature=rsa.SignData(message,CryptoConfig.MapNameToOID("SHA256"));byte[] header=Join(Field(2,Join(Field(1,key),Field(2,signature))),Field(10000,signed));return Join(Encoding.ASCII.GetBytes("Cr24"),BitConverter.GetBytes(3),BitConverter.GetBytes(header.Length),header,zip);}
 static byte[] SpecialZip(string kind){using(var s=new MemoryStream()){byte[] initial=Zip(null,0,0);s.Write(initial,0,initial.Length);using(var z=new ZipArchive(s,ZipArchiveMode.Update,true)){if(kind=="symlink")z.CreateEntry("link").ExternalAttributes=unchecked((int)0xA1FF0000);if(kind=="case"){z.CreateEntry("A/one");z.CreateEntry("a/two");}if(kind=="key"){z.GetEntry("manifest.json").Delete();using(var w=new StreamWriter(z.CreateEntry("manifest.json").Open()))w.Write("{\"manifest_version\":3,\"name\":\"Fixture\",\"version\":\"1\",\"key\":\"AAAA\"}");}if(kind=="total"){byte[] buffer=new byte[65536];for(int i=0;i<5;i++){using(var output=z.CreateEntry("large"+i).Open())for(int j=0;j<960;j++)output.Write(buffer,0,buffer.Length);}}}return s.ToArray();}}
 static int Main(string[] args){try{Directory.CreateDirectory(root);if(args.Length==2){using(var prepared=ExtensionPackage.PrepareAsync(args[0],root,args[1]).GetAwaiter().GetResult()){Console.WriteLine("REAL STORE VERIFIED: "+prepared.Id+" "+prepared.Name+" "+prepared.Version);}return 0;}
  string raw=new string('a',32);Assert(ExtensionPackage.ParseStoreId(raw)==raw,"raw ID");Assert(ExtensionPackage.ParseStoreId("https://chromewebstore.google.com/detail/name/"+raw+"?hl=en")==raw,"modern URL");Assert(ExtensionPackage.ParseStoreId("https://chrome.google.com/webstore/detail/name/"+raw)==raw,"legacy URL");
  foreach(string url in new[]{"http://chromewebstore.google.com/detail/"+raw,"https://evil.test/detail/"+raw,"https://chromewebstore.google.com.evil.test/detail/"+raw,"https://user@chromewebstore.google.com/detail/"+raw,"https://chromewebstore.google.com:444/detail/"+raw,"https://chromewebstore.google.com:443/detail/"+raw,"https://chromewebstore.google.com/detail/a/b/"+raw,new string('z',32)})Reject(()=>ExtensionPackage.ParseStoreId(url),"URL");
  using(var rsa=new RSACryptoServiceProvider(2048)){rsa.PersistKeyInCsp=false;byte[] key=Spki(rsa),hash;using(var sha=SHA256.Create())hash=sha.ComputeHash(key);string id=ExtensionPackage.IdFromBytes(hash);byte[] declared=hash.Take(16).ToArray();byte[] valid=Package(rsa,key,Zip("_metadata/verified_contents.json",0,0),declared);
   string folder;using(var prepared=ExtensionPackage.PrepareBytes(valid,id,root)){folder=prepared.Folder;Assert(prepared.Name=="Fixture"&&prepared.Version=="1.2"&&prepared.ManifestVersion==3,"manifest");Assert(prepared.Permissions.Length==3,"permissions");Assert(!Directory.Exists(Path.Combine(folder,"_metadata")),"metadata omitted");Assert(File.ReadAllText(Path.Combine(folder,"manifest.json")).Contains(Convert.ToBase64String(key)),"stable manifest key");}Assert(!Directory.Exists(folder),"dispose cleanup");
   byte[] corrupt=(byte[])valid.Clone();corrupt[corrupt.Length-1]^=1;Reject(()=>ExtensionPackage.PrepareBytes(corrupt,id,root),"corrupt signature");Reject(()=>ExtensionPackage.PrepareBytes(valid,raw,root),"wrong expected ID");
   byte[] wrong=(byte[])declared.Clone();wrong[0]^=1;Reject(()=>ExtensionPackage.PrepareBytes(Package(rsa,key,Zip(null,0,0),wrong),id,root),"wrong signed ID");
   using(var other=new RSACryptoServiceProvider(2048)){other.PersistKeyInCsp=false;Reject(()=>ExtensionPackage.PrepareBytes(Package(other,key,Zip(null,0,0),declared),id,root),"wrong signing key");}
   foreach(string bad in new[]{"../escape","/absolute","a\\escape","a:stream","CON.txt","trailing. ","manifest.JSON","a/../../escape"})Reject(()=>ExtensionPackage.PrepareBytes(Package(rsa,key,Zip(bad,0,0),declared),id,root),"unsafe zip "+bad);
   Reject(()=>ExtensionPackage.PrepareBytes(Package(rsa,key,Zip(null,5000,0),declared),id,root),"entry limit");Reject(()=>ExtensionPackage.PrepareBytes(Package(rsa,key,Zip("large",0,64*1024*1024+1),declared),id,root),"expanded size limit");
   foreach(string special in new[]{"symlink","case","key","total"})Reject(()=>ExtensionPackage.PrepareBytes(Package(rsa,key,SpecialZip(special),declared),id,root),"archive "+special);
   Assert(Directory.GetFileSystemEntries(root).Length==0,"failure cleanup");
   using(var kept=ExtensionPackage.PrepareBytes(valid,id,root)){folder=kept.Folder;kept.Commit();}Assert(Directory.Exists(folder),"committed package retained");
  }
  Console.WriteLine("PASS: "+count+" extension package checks");return 0;
 }catch(Exception e){Console.Error.WriteLine(e);return 1;}finally{if(Directory.Exists(root))Directory.Delete(root,true);}}
}


