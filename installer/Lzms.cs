using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace Mica {
 // Windows' own LZMS compressor (cabinet.dll, Windows 8+): about 15% smaller than ZIP's Deflate on the engine.
 // A packed file is a run of independent chunks, each [raw length][packed length][bytes], so chunks can be
 // packed in parallel and unpacked one at a time without holding the whole file in memory.
 public static class Lzms {
  // System32 only: Setup runs from Downloads, where a planted cabinet.dll must never load.
  const uint Algorithm=5;public const int Chunk=32*1024*1024;
  [DefaultDllImportSearchPaths(DllImportSearchPath.System32),DllImport("cabinet.dll",SetLastError=true)] static extern bool CreateCompressor(uint algorithm,IntPtr routines,out IntPtr handle);
  [DefaultDllImportSearchPaths(DllImportSearchPath.System32),DllImport("cabinet.dll",SetLastError=true)] static extern bool Compress(IntPtr handle,byte[] source,UIntPtr sourceSize,byte[] target,UIntPtr targetSize,out UIntPtr written);
  [DefaultDllImportSearchPaths(DllImportSearchPath.System32),DllImport("cabinet.dll")] static extern bool CloseCompressor(IntPtr handle);
  [DefaultDllImportSearchPaths(DllImportSearchPath.System32),DllImport("cabinet.dll",SetLastError=true)] static extern bool CreateDecompressor(uint algorithm,IntPtr routines,out IntPtr handle);
  [DefaultDllImportSearchPaths(DllImportSearchPath.System32),DllImport("cabinet.dll",SetLastError=true)] static extern bool Decompress(IntPtr handle,byte[] source,UIntPtr sourceSize,byte[] target,UIntPtr targetSize,out UIntPtr written);
  [DefaultDllImportSearchPaths(DllImportSearchPath.System32),DllImport("cabinet.dll")] static extern bool CloseDecompressor(IntPtr handle);

  static byte[] PackChunk(byte[] data,int offset,int count) {
   var slice=new byte[count];Buffer.BlockCopy(data,offset,slice,0,count);
   IntPtr handle;if(!CreateCompressor(Algorithm,IntPtr.Zero,out handle))throw new IOException("LZMS unavailable ("+Marshal.GetLastWin32Error()+").");
   try {
    var target=new byte[count+count/8+65536];UIntPtr written;
    if(!Compress(handle,slice,(UIntPtr)count,target,(UIntPtr)target.Length,out written))throw new IOException("LZMS compression failed ("+Marshal.GetLastWin32Error()+").");
    var result=new byte[(int)written.ToUInt64()];Buffer.BlockCopy(target,0,result,0,result.Length);return result;
   }finally {CloseCompressor(handle);}
  }
  public static void Pack(string source,Stream output) {
   byte[] data=File.ReadAllBytes(source);if(data.Length==0)return;int chunks=Math.Max(1,(data.Length+Chunk-1)/Chunk);var packed=new byte[chunks][];
   Parallel.For(0,chunks,delegate(int i) {int offset=i*Chunk;packed[i]=PackChunk(data,offset,Math.Min(Chunk,data.Length-offset));});
   var writer=new BinaryWriter(output);
   for(int i=0;i<chunks;i++){writer.Write(Math.Min(Chunk,data.Length-i*Chunk));writer.Write(packed[i].Length);writer.Write(packed[i]);}
   writer.Flush();
  }
  public static void Unpack(Stream input,Stream output) {
   IntPtr handle;if(!CreateDecompressor(Algorithm,IntPtr.Zero,out handle))throw new IOException("LZMS unavailable ("+Marshal.GetLastWin32Error()+").");
   try {
    var reader=new BinaryReader(input);var head=new byte[8];
    while(true) {
     int got=0;while(got<8){int n=input.Read(head,got,8-got);if(n==0)break;got+=n;}
     if(got==0)return;if(got<8)throw new InvalidDataException("Installer payload is truncated.");
     int raw=BitConverter.ToInt32(head,0),size=BitConverter.ToInt32(head,4);
     if(raw<0||raw>Chunk||size<=0||size>Chunk+Chunk/8+65536)throw new InvalidDataException("Installer payload invalid.");
     byte[] packed=reader.ReadBytes(size);if(packed.Length!=size)throw new InvalidDataException("Installer payload is truncated.");
     var target=new byte[raw];UIntPtr written;
     if(raw>0&&(!Decompress(handle,packed,(UIntPtr)size,target,(UIntPtr)raw,out written)||(int)written.ToUInt64()!=raw))throw new InvalidDataException("Installer payload is damaged.");
     output.Write(target,0,raw);
    }
   }finally {CloseDecompressor(handle);}
  }
 }
}
