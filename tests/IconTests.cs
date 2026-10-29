using System;
using System.IO;
using System.Reflection;
using System.Windows.Media;
using System.Windows.Media.Imaging;

// Standalone executable: pass the built Mica.exe path. No browser window is started.
public static class IconTests {
 static Type cacheType;
 static object Call(object instance,string method,params object[] args) {return cacheType.GetMethod(method,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance).Invoke(instance,args);}
 static void Check(bool value,string message) {if(!value)throw new Exception(message);}
 [STAThread] public static int Main(string[] args) {
  string folder=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"mica-icon-tests-"+Guid.NewGuid().ToString("N"));
  try {
   cacheType=Assembly.LoadFrom(Path.GetFullPath(args[0])).GetType("Mica.FaviconCache");
   Check(cacheType!=null,"Favicon cache is not implemented");
   string key=(string)Call(null,"GetKey","https://EXAMPLE.com:443/a?q=1");
   Check(key==(string)Call(null,"GetKey","https://example.com/b"),"Same origin must share cached icon");
   Check(key!=(string)Call(null,"GetKey","http://example.com/"),"HTTP and HTTPS must have separate icons");
   Check(key!=(string)Call(null,"GetKey","https://example.com:444/"),"Different ports must have separate icons");
   Check(Call(null,"GetKey","mica://home")==null,"Internal pages must not use web cache");
   Check(key.Length==64&&System.Text.RegularExpressions.Regex.IsMatch(key,"^[a-f0-9]+$"),"Cache names must be path-safe hashes");
   var cache=Activator.CreateInstance(cacheType,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic,null,new object[]{folder},null);
   var pixels=new byte[64*128*4];for(int i=3;i<pixels.Length;i+=4)pixels[i]=255;
   var bitmap=BitmapSource.Create(64,128,96,96,PixelFormats.Bgra32,null,pixels,64*4);
   Check((bool)Call(cache,"Store",key,bitmap),"First icon should refresh UI");
   Check(!(bool)Call(cache,"Store",key,bitmap),"Unchanged icon should not refresh UI again");
   cache=Activator.CreateInstance(cacheType,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic,null,new object[]{folder},null);
   var restored=Call(cache,"Read",key) as BitmapSource;
   Check(restored!=null&&restored.IsFrozen,"Fresh cache must restore a frozen icon from disk (exists: "+File.Exists(Path.Combine(folder,key+".png"))+")");
   Check(restored.PixelWidth<=32&&restored.PixelHeight<=32,"Cached image must decode to at most 32px");
   Check(restored.PixelWidth*2==restored.PixelHeight,"Restored icon must preserve its aspect ratio");
   File.WriteAllText(Path.Combine(folder,key+".png"),"corrupt icon");
   cache=Activator.CreateInstance(cacheType,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic,null,new object[]{folder},null);
   Check(Call(cache,"Read",key)==null,"Corrupt cache must fall back without crashing");
   File.WriteAllText(Path.Combine(folder,"keep-me.txt"),"unrelated");
   for(int i=0;i<258;i++)Call(cache,"Store",Call(null,"GetKey","https://site"+i+".example/"),bitmap);
   Check(Directory.GetFiles(folder,"*.png").Length<=256,"Disk cache must remain bounded");
   Check(File.Exists(Path.Combine(folder,"keep-me.txt")),"Cache pruning must preserve unrelated files");
   Console.WriteLine("PASS: origin isolation, persistence, frozen images, size bound, corrupt fallback, stable refresh, safe pruning");return 0;
  } catch(Exception error) {Console.Error.WriteLine(error);return 1;}
  finally {if(Directory.Exists(folder)&&Path.GetFileName(folder).StartsWith("mica-icon-tests-",StringComparison.Ordinal))Directory.Delete(folder,true);}
 }
}
