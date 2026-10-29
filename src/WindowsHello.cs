using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading.Tasks;

namespace Mica {
 interface IHelloVerifier {
  Task<bool> Available(Func<bool> live);
  Task<bool> Verify(IntPtr owner,Func<bool> live);
 }
 sealed class WindowsHello : IHelloVerifier {
  [DllImport("combase.dll")] static extern int RoInitialize(uint type);
  [DllImport("combase.dll")] static extern void RoUninitialize();
  [DllImport("combase.dll",CharSet=CharSet.Unicode)] static extern int WindowsCreateString(string text,int length,out IntPtr value);
  [DllImport("combase.dll")] static extern int WindowsDeleteString(IntPtr value);
  [DllImport("combase.dll")] static extern int RoGetActivationFactory(IntPtr name,ref Guid iid,out IntPtr factory);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern IntPtr FindWindow(string className,string title);
  [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr window);
  [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int Start(IntPtr self,out IntPtr operation);
  [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int Request(IntPtr self,IntPtr hwnd,IntPtr message,ref Guid iid,out IntPtr operation);
  [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int Read(IntPtr self,out int value);
  [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int ReadObject(IntPtr self,out IntPtr value);
  [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int Call(IntPtr self,IntPtr argument,out IntPtr result);
  [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int Create(IntPtr self,IntPtr name,int option,out IntPtr operation);
  [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int FromBytes(IntPtr self,int length,byte[] value,out IntPtr buffer);
  [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int ToBytes(IntPtr self,IntPtr buffer,out int length,out IntPtr value);
  [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int Command(IntPtr self);
  static T Method<T>(IntPtr instance,int slot) where T:class {return Marshal.GetDelegateForFunctionPointer(Marshal.ReadIntPtr(Marshal.ReadIntPtr(instance),slot*IntPtr.Size),typeof(T)) as T;}
  static IntPtr Factory(string id) {return Factory("Windows.Security.Credentials.UI.UserConsentVerifier",id);}
  static IntPtr Factory(string type,string id) {
   IntPtr name=IntPtr.Zero,result;
   try {Marshal.ThrowExceptionForHR(WindowsCreateString(type,type.Length,out name));var iid=new Guid(id);Marshal.ThrowExceptionForHR(RoGetActivationFactory(name,ref iid,out result));return result;}finally{if(name!=IntPtr.Zero)WindowsDeleteString(name);}
  }
  static IntPtr As(IntPtr instance,string id) {IntPtr result;var iid=new Guid(id);Marshal.ThrowExceptionForHR(Marshal.QueryInterface(instance,ref iid,out result));return result;}
  static async Task<T> Result<T>(IntPtr operation,Func<bool> live,Func<IntPtr,T> read,T failed) {
   IntPtr info=IntPtr.Zero;var iid=new Guid("00000036-0000-0000-c000-000000000046");
   try {
    Marshal.ThrowExceptionForHR(Marshal.QueryInterface(operation,ref iid,out info));
    var deadline=DateTime.UtcNow.AddMinutes(2);int status;
    while(true) {
     Marshal.ThrowExceptionForHR(Method<Read>(info,7)(info,out status));
     if(status!=0)break;
     if(!live()||DateTime.UtcNow>deadline){Method<Command>(info,9)(info);return failed;}
     await Task.Delay(100);
    }
    if(status!=1||!live())return failed;
    return read(operation);
   }finally {if(info!=IntPtr.Zero){Method<Command>(info,10)(info);Marshal.Release(info);}Marshal.Release(operation);}
  }
  static int Number(IntPtr instance) {int value;Marshal.ThrowExceptionForHR(Method<Read>(instance,8)(instance,out value));return value;}
  static IntPtr Pointer(IntPtr instance) {IntPtr value;Marshal.ThrowExceptionForHR(Method<ReadObject>(instance,8)(instance,out value));return value;}
  public async Task<bool> Available(Func<bool> live) {return await Invoke(IntPtr.Zero,false,live);}
  // Verifying signs a fixed challenge with this user's Windows Hello key. Only a confirmed PIN, face or fingerprint
  // yields the signature, and its hash is what unwraps the enrollment (HelloUnlockStore), so the prompt is not just a gate.
  public async Task<bool> Verify(IntPtr owner,Func<bool> live) {if(owner==IntPtr.Zero)return false;var signature=await Sign(live);if(signature==null)return false;Prove(signature);Array.Clear(signature,0,signature.Length);return true;}
  static byte[] proof;static DateTime proofTime;
  internal static void Prove(byte[] signature) {var old=proof;using(var sha=SHA256.Create())proof=sha.ComputeHash(signature);proofTime=DateTime.UtcNow;if(old!=null)Array.Clear(old,0,old.Length);}
  // One use, within a minute of the verification.
  internal static byte[] TakeProof() {var taken=proof;proof=null;if(taken!=null&&DateTime.UtcNow-proofTime>TimeSpan.FromMinutes(1)){Array.Clear(taken,0,taken.Length);taken=null;}return taken;}
  const string CredentialName="Mica password vault";
  static async Task<byte[]> Sign(Func<bool> live) {
   var owned=new List<IntPtr>();Func<IntPtr,IntPtr> keep=p=>{if(p!=IntPtr.Zero)owned.Add(p);return p;};IntPtr name=IntPtr.Zero;bool initialized=false;
   try {
    int hr=RoInitialize(0);if(hr>=0)initialized=true;else if(hr!=unchecked((int)0x80010106))Marshal.ThrowExceptionForHR(hr);
    var manager=keep(Factory("Windows.Security.Credentials.KeyCredentialManager","6aac468b-0ef1-4ce0-8290-4106da6a63b5"));
    Marshal.ThrowExceptionForHR(WindowsCreateString(CredentialName,CredentialName.Length,out name));
    IntPtr operation,item;Marshal.ThrowExceptionForHR(Method<Call>(manager,9)(manager,name,out operation)); // OpenAsync
    var found=keep(await Result(operation,live,Pointer,IntPtr.Zero));if(found==IntPtr.Zero)return null;
    var retrieval=keep(As(found,"58cd7703-8d87-4249-9b58-f6598cc9644e"));int status;Marshal.ThrowExceptionForHR(Method<Read>(retrieval,7)(retrieval,out status));
    if(status==2) { // NotFound: create this user's key once (FailIfExists); Windows asks for Hello here.
     Marshal.ThrowExceptionForHR(Method<Create>(manager,8)(manager,name,1,out operation));Foreground(live);
     found=keep(await Result(operation,live,Pointer,IntPtr.Zero));if(found==IntPtr.Zero)return null;
     retrieval=keep(As(found,"58cd7703-8d87-4249-9b58-f6598cc9644e"));Marshal.ThrowExceptionForHR(Method<Read>(retrieval,7)(retrieval,out status));
    }
    if(status!=0||!live())return null;
    Marshal.ThrowExceptionForHR(Method<ReadObject>(retrieval,6)(retrieval,out item));keep(item);var credential=keep(As(item,"9585ef8d-457b-4847-b11a-fa960bbdb138"));
    var buffers=keep(Factory("Windows.Security.Cryptography.CryptographicBuffer","320b7e22-3cb0-4cdf-8663-1d28910065eb"));
    var challenge=Encoding.UTF8.GetBytes("Mica password vault unlock v2");Marshal.ThrowExceptionForHR(Method<FromBytes>(buffers,9)(buffers,challenge.Length,challenge,out item));keep(item);
    Marshal.ThrowExceptionForHR(Method<Call>(credential,9)(credential,item,out operation));Foreground(live); // RequestSignAsync
    var signed=keep(await Result(operation,live,Pointer,IntPtr.Zero));if(signed==IntPtr.Zero)return null;
    var outcome=keep(As(signed,"f53786c1-5261-4cdd-976d-cc909ac71620"));Marshal.ThrowExceptionForHR(Method<Read>(outcome,7)(outcome,out status));
    if(status!=0||!live())return null;
    Marshal.ThrowExceptionForHR(Method<ReadObject>(outcome,6)(outcome,out item));keep(item);
    int length;IntPtr bytes;Marshal.ThrowExceptionForHR(Method<ToBytes>(buffers,10)(buffers,item,out length,out bytes));
    try {if(length<64)return null;var signature=new byte[length];Marshal.Copy(bytes,signature,0,length);return signature;}finally {if(bytes!=IntPtr.Zero)Marshal.FreeCoTaskMem(bytes);}
   }catch(COMException){return null;}catch(DllNotFoundException){return null;}catch(EntryPointNotFoundException){return null;}catch(InvalidCastException){return null;}catch(ArgumentException){return null;}
   finally {owned.Reverse();foreach(var p in owned)Marshal.Release(p);if(name!=IntPtr.Zero)WindowsDeleteString(name);if(initialized)RoUninitialize();}
  }
  // The key credential prompt takes no owner window and can open behind Mica; bring it forward once it appears.
  static async void Foreground(Func<bool> live) {for(int i=0;i<30&&live();i++){await Task.Delay(100);var prompt=FindWindow("Credential Dialog Xaml Host",null);if(prompt!=IntPtr.Zero){SetForegroundWindow(prompt);return;}}}
  static async Task<bool> Invoke(IntPtr owner,bool verify,Func<bool> live) {
   IntPtr factory=IntPtr.Zero,message=IntPtr.Zero;bool initialized=false;
   try {
    int hr=RoInitialize(0);if(hr>=0)initialized=true;else if(hr!=unchecked((int)0x80010106))Marshal.ThrowExceptionForHR(hr);
    IntPtr operation;
    if(verify) {
     factory=Factory("39e050c3-4e74-441a-8dc0-b81104df949c");
     const string text="Unlock your Mica password vault";Marshal.ThrowExceptionForHR(WindowsCreateString(text,text.Length,out message));
     var iid=new Guid("fd596ffd-2318-558f-9dbe-d21df43764a5");Marshal.ThrowExceptionForHR(Method<Request>(factory,6)(factory,owner,message,ref iid,out operation));
    } else {var interop=Factory("39e050c3-4e74-441a-8dc0-b81104df949c");Marshal.Release(interop);factory=Factory("af4f3f91-564c-4ddc-b8b5-973447627c65");Marshal.ThrowExceptionForHR(Method<Start>(factory,6)(factory,out operation));}
    return await Result(operation,live,Number,-1)==0;
   }catch(COMException){return false;}catch(DllNotFoundException){return false;}catch(EntryPointNotFoundException){return false;}
   finally {if(factory!=IntPtr.Zero)Marshal.Release(factory);if(message!=IntPtr.Zero)WindowsDeleteString(message);if(initialized)RoUninitialize();}
  }
 }
 // Holds the vault key (never the master password) for Windows Hello unlock. Format 2 is DPAPI with entropy from the
 // Windows Hello signature, so only a successful verification opens it. Format 1 (older, path-only entropy) is upgraded on use.
 static class HelloUnlockStore {
  static readonly byte[] Header={(byte)'M',(byte)'H',2,0};
  static byte[] Entropy(string profile,byte[] proof) {
   var label=Encoding.UTF8.GetBytes((proof==null?"Mica Hello unlock v1|":"Mica Hello unlock v2|")+Path.GetFullPath(profile).ToUpperInvariant());
   if(proof==null)using(var sha=SHA256.Create())return sha.ComputeHash(label);
   using(var h=new HMACSHA256(proof))return h.ComputeHash(label);
  }
  internal static string PathFor(string profile) {return Path.Combine(profile,"hello-unlock.bin");}
  static byte[] Proof() {var proof=WindowsHello.TakeProof();if(proof==null)throw new CryptographicException("Windows Hello didn't confirm. Use your master password.");return proof;}
  static string VaultKey(string profile,string secret) {return VaultCrypto.IsKey(secret)?secret:VaultCrypto.KeyFor(VaultCrypto.Json.Deserialize<VaultEnvelope>(File.ReadAllText(Path.Combine(profile,"vault.enc.json"))),secret);}
  // Call right after a successful IHelloVerifier.Verify.
  internal static void Save(string profile,string secret) {var proof=Proof();try {Write(profile,VaultKey(profile,secret),proof);}finally {Array.Clear(proof,0,proof.Length);}}
  static void Write(string profile,string key,byte[] proof) {
   if(!VaultCrypto.IsKey(key))throw new ArgumentException("Only a vault key is kept for Windows Hello.");
   byte[] plain=Encoding.UTF8.GetBytes(key),encrypted=null;
   try {encrypted=ProtectedData.Protect(plain,Entropy(profile,proof),DataProtectionScope.CurrentUser);string path=PathFor(profile),temp=path+".tmp";File.WriteAllBytes(temp,Header.Concat(encrypted).ToArray());if(File.Exists(path))File.Replace(temp,path,null);else File.Move(temp,path);}
   finally{Array.Clear(plain,0,plain.Length);if(encrypted!=null)Array.Clear(encrypted,0,encrypted.Length);}
  }
  // Returns the vault key. Call right after a successful IHelloVerifier.Verify.
  internal static string Load(string profile) {
   var path=PathFor(profile);if(new FileInfo(path).Length>65536)throw new CryptographicException("Invalid Windows Hello enrollment. Use your master password.");
   var proof=Proof();
   try {
    var data=File.ReadAllBytes(path);bool bound=data.Length>Header.Length&&data.Take(Header.Length).SequenceEqual(Header);
    byte[] plain=ProtectedData.Unprotect(bound?data.Skip(Header.Length).ToArray():data,Entropy(profile,bound?proof:null),DataProtectionScope.CurrentUser);string secret;
    try{secret=Encoding.UTF8.GetString(plain);}finally{Array.Clear(plain,0,plain.Length);}
    if(bound)return secret;
    // Format 1 held the master password: keep only the vault key, now behind this Hello signature.
    var key=VaultKey(profile,secret);secret=null;Write(profile,key,proof);return key;
   }finally {Array.Clear(proof,0,proof.Length);}
  }
  internal static void Forget(string profile) {if(File.Exists(PathFor(profile)))File.Delete(PathFor(profile));}
 }
}
