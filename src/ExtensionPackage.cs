using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace Mica {
 public sealed class PreparedExtension : IDisposable {
  public string Id {get;internal set;} public string Name {get;internal set;} public string Version {get;internal set;}
  public string Folder {get;internal set;} public string[] Permissions {get;internal set;} public int ManifestVersion {get;internal set;}
  bool committed;
  public void Commit(){committed=true;}
  public void Dispose(){if(!committed && Folder!=null){ExtensionPackage.DeleteStage(Folder);Folder=null;}}
 }
 public static class ExtensionPackage {
  const int MaxDownload=64*1024*1024, MaxHeader=1024*1024, MaxEntries=5000;
  const long MaxExpanded=256L*1024*1024;
  static readonly Regex IdPattern=new Regex("^[a-p]{32}$",RegexOptions.CultureInvariant);
  public static string ParseStoreId(string input) {
   input=(input??"").Trim(); if(IdPattern.IsMatch(input))return input;
   string originalPath=input.Split('?','#')[0];if(originalPath.Contains("\\")||originalPath.Contains("/../")||originalPath.Contains("/./")||originalPath.Contains("%")||input.Any(Char.IsControl))throw new FormatException("Invalid store URL path.");
   Uri uri; if(!Regex.IsMatch(input,@"\Ahttps://(?:chromewebstore\.google\.com|chrome\.google\.com)/",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant)||!Uri.TryCreate(input,UriKind.Absolute,out uri)||uri.Scheme!="https"||!uri.IsDefaultPort||uri.Authority!=uri.Host||uri.UserInfo.Length!=0)throw new FormatException("Enter an HTTPS Chrome Web Store detail link or a 32-character extension ID.");
   string prefix=uri.Host=="chromewebstore.google.com"?"/detail/":uri.Host=="chrome.google.com"?"/webstore/detail/":null;
   if(prefix==null||!uri.AbsolutePath.StartsWith(prefix,StringComparison.Ordinal)||uri.AbsolutePath.Contains("%")||uri.AbsolutePath.Contains("\\"))throw new FormatException("This is not a Chrome Web Store detail link.");
   string[] parts=uri.AbsolutePath.Substring(prefix.Length).TrimEnd('/').Split('/');
   if(parts.Length<1||parts.Length>2||parts.Any(p=>p.Length==0)||!IdPattern.IsMatch(parts[parts.Length-1]))throw new FormatException("The link does not contain a valid extension ID.");
   return parts[parts.Length-1];
  }
  public static async Task<PreparedExtension> PrepareAsync(string input,string managedRoot,string browserVersion) {
   string id=ParseStoreId(input); if(!Regex.IsMatch(browserVersion??"","^\\d+\\.\\d+\\.\\d+\\.\\d+$"))throw new ArgumentException("A valid browser version is required.");
   string query=Uri.EscapeDataString("id="+id+"&installsource=ondemand&uc");
   var uri=new Uri("https://clients2.google.com/service/update2/crx?response=redirect&prodversion="+browserVersion+"&acceptformat=crx3&x="+query);
   byte[] data=await DownloadAsync(uri).ConfigureAwait(false);
   return await Task.Run(()=>PrepareBytes(data,id,managedRoot)).ConfigureAwait(false);
  }
  static bool AllowedDownload(Uri u){return u.Scheme=="https"&&u.IsDefaultPort&&u.UserInfo.Length==0&&(u.Host=="clients2.google.com"||u.Host.EndsWith(".googleusercontent.com",StringComparison.OrdinalIgnoreCase));}
  static async Task<byte[]> DownloadAsync(Uri uri) {
   using(var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(30))) {
    for(int redirect=0;redirect<=5;redirect++) {
     if(!AllowedDownload(uri))throw new InvalidDataException("The store redirected to an untrusted download host.");
     var request=(HttpWebRequest)WebRequest.Create(uri);request.AllowAutoRedirect=false;request.Timeout=30000;request.ReadWriteTimeout=30000;request.AutomaticDecompression=DecompressionMethods.None;
     using(deadline.Token.Register(()=>request.Abort()))
     using(var response=(HttpWebResponse)await request.GetResponseAsync().ConfigureAwait(false)) {
      int status=(int)response.StatusCode;
      if(status>=300&&status<=399){string location=response.Headers[HttpResponseHeader.Location];if(String.IsNullOrEmpty(location))throw new InvalidDataException("Missing store redirect.");uri=new Uri(uri,location);continue;}
      if(status!=200||response.ContentLength>MaxDownload)throw new InvalidDataException("The store package is unavailable or exceeds 64 MB.");
      using(var source=response.GetResponseStream())using(var output=new MemoryStream()) {
       byte[] buffer=new byte[65536];int read;
       while((read=await source.ReadAsync(buffer,0,buffer.Length,deadline.Token).ConfigureAwait(false))>0){if(output.Length+read>MaxDownload)throw new InvalidDataException("The store package exceeds 64 MB.");output.Write(buffer,0,read);}
       return output.ToArray();
      }
     }
    }
   }
   throw new InvalidDataException("Too many store redirects.");
  }
  internal static PreparedExtension PrepareBytes(byte[] data,string id,string managedRoot) {
   id=ParseStoreId(id);int zipOffset;byte[] key=Verify(data,id,out zipOffset);
   string root=Path.GetFullPath(managedRoot);CheckParents(root);Directory.CreateDirectory(root);CheckParents(root);
   string stage=Path.Combine(root,"package-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(stage);
   try {
    Extract(data,zipOffset,stage);
    var serializer=new JavaScriptSerializer{MaxJsonLength=2*1024*1024,RecursionLimit=64};
    string manifestPath=Path.Combine(stage,"manifest.json");
    if(!File.Exists(manifestPath)||new FileInfo(manifestPath).Length>2*1024*1024)throw new InvalidDataException("The package has no valid manifest.");
    var manifest=serializer.DeserializeObject(File.ReadAllText(manifestPath,Encoding.UTF8)) as Dictionary<string,object>;
    if(manifest==null)throw new InvalidDataException("The manifest must be an object.");
    object mv;int version;if(!manifest.TryGetValue("manifest_version",out mv)||!Int32.TryParse(Convert.ToString(mv),out version)||(version!=2&&version!=3))throw new InvalidDataException("Unsupported manifest version.");
    string name=RequiredString(manifest,"name"),ver=RequiredString(manifest,"version");
    if(!Regex.IsMatch(ver,"^\\d{1,5}(\\.\\d{1,5}){0,3}$")||ver.Split('.').Any(p=>Int32.Parse(p)>65535))throw new InvalidDataException("Invalid extension version.");
    string encoded=Convert.ToBase64String(key);object oldKey;
    if(manifest.TryGetValue("key",out oldKey)&&(!(oldKey is string)||!Convert.FromBase64String((string)oldKey).SequenceEqual(key)))throw new InvalidDataException("The manifest key disagrees with the verified developer key.");
    manifest["key"]=encoded;
    if(name.StartsWith("__MSG_",StringComparison.Ordinal)&&name.EndsWith("__",StringComparison.Ordinal)) {
     string locale=RequiredString(manifest,"default_locale");if(!Regex.IsMatch(locale,"^[A-Za-z0-9_@-]{1,64}$"))throw new InvalidDataException("Invalid default locale.");
     string messagesPath=Path.Combine(stage,"_locales",locale,"messages.json");
     if(!File.Exists(messagesPath)||new FileInfo(messagesPath).Length>2*1024*1024)throw new InvalidDataException("Missing localized extension name.");
     var messages=serializer.DeserializeObject(File.ReadAllText(messagesPath,Encoding.UTF8)) as Dictionary<string,object>;object message;
     if(messages==null||!messages.TryGetValue(name.Substring(6,name.Length-8),out message)||!(message is Dictionary<string,object>))throw new InvalidDataException("Missing localized extension name.");
     name=RequiredString((Dictionary<string,object>)message,"message");
    }
    var permissions=new HashSet<string>(StringComparer.Ordinal);AddStrings(manifest,"permissions",permissions);AddStrings(manifest,"host_permissions",permissions);
    object scripts;if(manifest.TryGetValue("content_scripts",out scripts)) {var entries=scripts as object[];if(entries==null)throw new InvalidDataException("Invalid content scripts.");foreach(object script in entries){var item=script as Dictionary<string,object>;if(item==null)throw new InvalidDataException("Invalid content script.");AddStrings(item,"matches",permissions);}}
    File.WriteAllText(manifestPath,serializer.Serialize(manifest),new UTF8Encoding(false));
    var ai=BrowserApp.AiExtensionById(id);if(ai!=null)ClaudeSidePanelPatch.Apply(stage,ai.Pages);
    return new PreparedExtension{Id=id,Name=name,Version=ver,ManifestVersion=version,Folder=stage,Permissions=permissions.OrderBy(p=>p,StringComparer.Ordinal).ToArray()};
   } catch {DeleteStage(stage);throw;}
  }
  static string RequiredString(Dictionary<string,object> map,string key){object v;if(!map.TryGetValue(key,out v)||!(v is string)||String.IsNullOrWhiteSpace((string)v))throw new InvalidDataException("Missing manifest "+key+".");return (string)v;}
  static void AddStrings(Dictionary<string,object> map,string key,HashSet<string> values){object v;if(!map.TryGetValue(key,out v))return;var list=v as object[];if(list==null)throw new InvalidDataException("Invalid manifest "+key+".");foreach(object item in list){if(!(item is string))throw new InvalidDataException("Invalid permission.");values.Add((string)item);}}
  internal static void CheckParents(string path){for(var d=new DirectoryInfo(path);d!=null;d=d.Parent)if(d.Exists&&(d.Attributes&FileAttributes.ReparsePoint)!=0)throw new IOException("Extension storage cannot pass through a directory link.");}
  internal static void DeleteStage(string path){if(!Directory.Exists(path))return;CheckParents(path);foreach(string child in Directory.EnumerateFileSystemEntries(path)){if((File.GetAttributes(child)&FileAttributes.ReparsePoint)!=0)throw new IOException("Refusing to delete a linked extension path.");if(Directory.Exists(child))DeleteStage(child);else File.Delete(child);}Directory.Delete(path);}
  static void Extract(byte[] data,int offset,string stage) {
   using(var stream=new MemoryStream(data,offset,data.Length-offset,false))using(var archive=new ZipArchive(stream,ZipArchiveMode.Read)) {
    if(archive.Entries.Count>MaxEntries)throw new InvalidDataException("The extension contains too many files.");
    long total=0;var paths=new HashSet<string>(StringComparer.OrdinalIgnoreCase);var spelling=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
    foreach(var entry in archive.Entries) {
     string path=entry.FullName;bool directory=path.EndsWith("/",StringComparison.Ordinal);string clean=directory?path.Substring(0,path.Length-1):path;
     if(String.IsNullOrEmpty(clean)||clean.Length>220||path.Contains("\\")||path.Contains(":" )||path.StartsWith("/",StringComparison.Ordinal))throw new InvalidDataException("Unsafe archive path.");
     foreach(string segment in clean.Split('/'))if(segment.Length==0||segment=="."||segment==".."||segment.EndsWith(".")||segment.EndsWith(" ")||segment.IndexOfAny(Path.GetInvalidFileNameChars())>=0||Regex.IsMatch(segment,"^(CON|PRN|AUX|NUL|COM[0-9¹²³]|LPT[0-9¹²³])(?:\\.|$)",RegexOptions.IgnoreCase))throw new InvalidDataException("Unsafe archive filename.");
     string ancestor="";foreach(string segment in clean.Split('/')){ancestor=ancestor.Length==0?segment:ancestor+"/"+segment;string prior;if(spelling.TryGetValue(ancestor,out prior)&&prior!=ancestor)throw new InvalidDataException("Archive paths differ only in letter case.");spelling[ancestor]=ancestor;}
     if(!paths.Add(clean)||((entry.ExternalAttributes>>16)&0xF000)==0xA000||(entry.ExternalAttributes&0x400)!=0)throw new InvalidDataException("Duplicate or linked archive path.");
     total=checked(total+entry.Length);if(total>MaxExpanded||entry.Length>MaxDownload)throw new InvalidDataException("The expanded extension exceeds the size limit.");
     // WebView2 rejects Chrome's reserved store metadata; it is not runtime extension content.
     if(clean=="_metadata"||clean.StartsWith("_metadata/",StringComparison.Ordinal))continue;
     string target=Path.GetFullPath(Path.Combine(stage,clean.Replace('/',Path.DirectorySeparatorChar)));
     if(!target.StartsWith(stage+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Archive path escapes storage.");
     if(directory){Directory.CreateDirectory(target);continue;}Directory.CreateDirectory(Path.GetDirectoryName(target));
     using(var source=entry.Open())using(var output=new FileStream(target,FileMode.CreateNew,FileAccess.Write,FileShare.None)) {byte[] buffer=new byte[65536];int read;long written=0;while((read=source.Read(buffer,0,buffer.Length))>0){written+=read;if(written>entry.Length||written>MaxDownload)throw new InvalidDataException("Invalid expanded file size.");output.Write(buffer,0,read);}if(written!=entry.Length)throw new InvalidDataException("Truncated archive file.");}
    }
   }
  }
  // Format and algorithm match Chromium components/crx_file/{crx3.proto,crx_verifier.cc}.
  // This verifies the developer proof against the requested ID, not a Google publisher attestation.
  internal static byte[] Verify(byte[] data,string id,out int offset) {
   offset=0;if(data==null||data.Length<12||data.Length>MaxDownload||Encoding.ASCII.GetString(data,0,4)!="Cr24"||BitConverter.ToUInt32(data,4)!=3)throw new InvalidDataException("Only signed CRX3 store packages are supported.");
   uint length=BitConverter.ToUInt32(data,8);if(length>MaxHeader||length>data.Length-12)throw new InvalidDataException("Invalid CRX header size.");offset=12+(int)length;
   var fields=Proto(Slice(data,12,(int)length));byte[] signed=Single(fields,10000);byte[] crxid=Single(Proto(signed),1);
   if(crxid.Length!=16||IdFromBytes(crxid)!=id)throw new InvalidDataException("The signed package ID does not match the store link.");
   byte[] hash;using(var sha=SHA256.Create()){byte[] prefix=Encoding.ASCII.GetBytes("CRX3 SignedData\0"),len=BitConverter.GetBytes(signed.Length);sha.TransformBlock(prefix,0,prefix.Length,null,0);sha.TransformBlock(len,0,len.Length,null,0);sha.TransformBlock(signed,0,signed.Length,null,0);sha.TransformFinalBlock(data,offset,data.Length-offset);hash=sha.Hash;}
   foreach(var proof in fields.Where(f=>f.Key==2)) {
    var p=Proto(proof.Value);byte[] key=Single(p,1),signature=Single(p,2);byte[] keyHash;using(var sha=SHA256.Create())keyHash=sha.ComputeHash(key);
    if(IdFromBytes(keyHash)!=id)continue;
    using(var rsa=new RSACryptoServiceProvider()){rsa.PersistKeyInCsp=false;rsa.ImportParameters(ReadRsa(key));if(!rsa.VerifyHash(hash,CryptoConfig.MapNameToOID("SHA256"),signature))throw new InvalidDataException("The extension signature is invalid.");}return key;
   }
   throw new InvalidDataException("No supported RSA developer proof matches the extension ID. ECDSA-only packages are not supported.");
  }
  internal static string IdFromBytes(byte[] bytes){var s=new StringBuilder(32);for(int i=0;i<16;i++){s.Append((char)('a'+(bytes[i]>>4)));s.Append((char)('a'+(bytes[i]&15)));}return s.ToString();}
  static byte[] Slice(byte[] bytes,int start,int length){var result=new byte[length];Buffer.BlockCopy(bytes,start,result,0,length);return result;}
  static List<KeyValuePair<int,byte[]>> Proto(byte[] bytes) {
   var fields=new List<KeyValuePair<int,byte[]>>();int pos=0;
   while(pos<bytes.Length){ulong tag=Varint(bytes,ref pos);if(tag==0||tag>>3>536870911)throw new InvalidDataException("Invalid protobuf tag.");int field=(int)(tag>>3),wire=(int)(tag&7);if(wire==2){ulong count=Varint(bytes,ref pos);if(count>(ulong)(bytes.Length-pos))throw new InvalidDataException("Truncated protobuf.");fields.Add(new KeyValuePair<int,byte[]>(field,Slice(bytes,pos,(int)count)));pos+=(int)count;}else if(wire==0)Varint(bytes,ref pos);else if(wire==1||wire==5){pos+=wire==1?8:4;if(pos>bytes.Length)throw new InvalidDataException("Truncated protobuf.");}else throw new InvalidDataException("Unsupported protobuf wire type.");if(fields.Count>128)throw new InvalidDataException("Too many CRX header fields.");}return fields;
  }
  static ulong Varint(byte[] b,ref int p){ulong v=0;for(int i=0;i<10;i++){if(p>=b.Length)throw new InvalidDataException("Truncated protobuf integer.");byte n=b[p++];if(i==9&&n>1)throw new InvalidDataException("Oversized protobuf integer.");v|=(ulong)(n&127)<<(i*7);if(n<128)return v;}throw new InvalidDataException("Invalid protobuf integer.");}
  static byte[] Single(List<KeyValuePair<int,byte[]>> f,int key){var matches=f.Where(x=>x.Key==key).ToArray();if(matches.Length!=1)throw new InvalidDataException("Missing or duplicate CRX field.");return matches[0].Value;}
  sealed class Der {readonly byte[] b;int p;internal Der(byte[] bytes){b=bytes;}internal bool End{get{return p==b.Length;}}internal byte[] Read(int tag){if(p>=b.Length||b[p++]!=tag||p>=b.Length)throw new InvalidDataException("Invalid public key.");int n=b[p++];if(n>=128){int count=n&127;if(count<1||count>4||p+count>b.Length)throw new InvalidDataException("Invalid DER length.");n=0;for(int i=0;i<count;i++)n=checked(n*256+b[p++]);}if(n<0||n>b.Length-p)throw new InvalidDataException("Truncated public key.");byte[] value=Slice(b,p,n);p+=n;return value;}}
  static RSAParameters ReadRsa(byte[] key){var outer=new Der(key);var sequence=new Der(outer.Read(48));if(!outer.End)throw new InvalidDataException("Trailing public key bytes.");var alg=new Der(sequence.Read(48));if(!alg.Read(6).SequenceEqual(new byte[]{42,134,72,134,247,13,1,1,1}))throw new InvalidDataException("Not an RSA public key.");if(!alg.End&&alg.Read(5).Length!=0)throw new InvalidDataException("Invalid RSA algorithm.");if(!alg.End)throw new InvalidDataException("Invalid RSA algorithm.");byte[] bits=sequence.Read(3);if(!sequence.End||bits.Length<2||bits[0]!=0)throw new InvalidDataException("Invalid key bit string.");var bitReader=new Der(Slice(bits,1,bits.Length-1));var rsa=new Der(bitReader.Read(48));if(!bitReader.End)throw new InvalidDataException("Invalid RSA key.");byte[] modulus=Integer(rsa.Read(2)),exponent=Integer(rsa.Read(2));if(!rsa.End||modulus.Length<256||modulus.Length>1024||exponent.Length>8)throw new InvalidDataException("Unsupported RSA key size.");return new RSAParameters{Modulus=modulus,Exponent=exponent};}
  static byte[] Integer(byte[] value){if(value.Length==0||(value[0]&128)!=0)throw new InvalidDataException("Invalid RSA integer.");return value.Length>1&&value[0]==0?Slice(value,1,value.Length-1):value;}
 }
}



