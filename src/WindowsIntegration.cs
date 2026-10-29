using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

namespace Mica {
 internal static class WindowsIntegration {
  internal const string AppId="Mica.Browser.Desktop";
  [StructLayout(LayoutKind.Sequential)] internal struct PropertyKey {
   public Guid Format;public uint Id;
   public PropertyKey(uint id){Format=new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3");Id=id;}
  }
  [StructLayout(LayoutKind.Explicit,Size=24)] internal struct Value {
   [FieldOffset(0)] public ushort Type;
   [FieldOffset(8)] public IntPtr Text;
  }
  [ComImport,Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] internal interface PropertyStore {
   void GetCount(out uint count);void GetAt(uint index,out PropertyKey key);void GetValue(ref PropertyKey key,out Value value);void SetValue(ref PropertyKey key,ref Value value);void Commit();
  }
  [ComImport,Guid("00021401-0000-0000-C000-000000000046")] class ShellLinkObject {}
  [ComImport,Guid("000214F9-0000-0000-C000-000000000046"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] interface ShellLink {
   void GetPath([Out,MarshalAs(UnmanagedType.LPWStr)]StringBuilder path,int max,IntPtr data,uint flags);
   void GetIDList(out IntPtr list);void SetIDList(IntPtr list);
   void GetDescription([Out,MarshalAs(UnmanagedType.LPWStr)]StringBuilder value,int max);void SetDescription([MarshalAs(UnmanagedType.LPWStr)]string value);
   void GetWorkingDirectory([Out,MarshalAs(UnmanagedType.LPWStr)]StringBuilder value,int max);void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)]string value);
   void GetArguments([Out,MarshalAs(UnmanagedType.LPWStr)]StringBuilder value,int max);void SetArguments([MarshalAs(UnmanagedType.LPWStr)]string value);
   void GetHotkey(out short key);void SetHotkey(short key);void GetShowCmd(out int cmd);void SetShowCmd(int cmd);
   void GetIconLocation([Out,MarshalAs(UnmanagedType.LPWStr)]StringBuilder value,int max,out int index);void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)]string value,int index);
   void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)]string value,uint reserved);void Resolve(IntPtr hwnd,uint flags);void SetPath([MarshalAs(UnmanagedType.LPWStr)]string value);
  }
  [DllImport("shell32.dll")] static extern int SHGetPropertyStoreForWindow(IntPtr hwnd,ref Guid iid,[MarshalAs(UnmanagedType.Interface)]out PropertyStore store);
  [DllImport("ole32.dll")] static extern int PropVariantClear(ref Value value);
  static void Set(PropertyStore store,uint id,string text) {
   var key=new PropertyKey(id);var value=new Value {Type=31,Text=Marshal.StringToCoTaskMemUni(text)};
   try {store.SetValue(ref key,ref value);}finally {PropVariantClear(ref value);}
  }
  internal static void SetWindow(IntPtr hwnd,string exe) {
   var iid=typeof(PropertyStore).GUID;PropertyStore store;Marshal.ThrowExceptionForHR(SHGetPropertyStoreForWindow(hwnd,ref iid,out store));
   try {Set(store,2,"\""+exe+"\" --new-window");Set(store,3,exe+",0");Set(store,4,"Mica");Set(store,5,AppId);}finally {Marshal.ReleaseComObject(store);}
  }
  internal static string WindowIdentity(IntPtr hwnd) {
   var iid=typeof(PropertyStore).GUID;PropertyStore store;Marshal.ThrowExceptionForHR(SHGetPropertyStoreForWindow(hwnd,ref iid,out store));
   try {var key=new PropertyKey(5);Value value;store.GetValue(ref key,out value);try{return value.Type==31?Marshal.PtrToStringUni(value.Text):null;}finally{PropVariantClear(ref value);}}finally{Marshal.ReleaseComObject(store);}
  }
  internal static void Shortcut(string path,string exe,string arguments) {
   var instance=new ShellLinkObject();try {
    var link=(ShellLink)instance;link.SetPath(exe);link.SetArguments(arguments);link.SetWorkingDirectory(Path.GetDirectoryName(exe));link.SetDescription("Mica browser");link.SetIconLocation(exe,0);link.SetShowCmd(1);
    var store=(PropertyStore)instance;Set(store,5,AppId);store.Commit();((IPersistFile)instance).Save(path,true);
   }finally {Marshal.FinalReleaseComObject(instance);}
  }
  internal static string ShortcutIdentity(string path) {
   var instance=new ShellLinkObject();try {((IPersistFile)instance).Load(path,0);var key=new PropertyKey(5);Value value;((PropertyStore)instance).GetValue(ref key,out value);try{return value.Type==31?Marshal.PtrToStringUni(value.Text):null;}finally{PropVariantClear(ref value);}}finally{Marshal.FinalReleaseComObject(instance);}
  }
 }
}
