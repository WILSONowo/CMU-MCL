using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Runtime.InteropServices;

namespace System.Runtime.CompilerServices { internal static class IsExternalInit {} }
internal static class Compat
{
    public static byte[] RandomBytes(int count) {var bytes=new byte[count];using(var rng=RandomNumberGenerator.Create())rng.GetBytes(bytes);return bytes;}
    private static readonly Random ShuffleRandom=new Random();
    public static void Shuffle<T>(System.Collections.Generic.IList<T> list) {lock(ShuffleRandom)for(int i=list.Count-1;i>0;i--){int j=ShuffleRandom.Next(i+1);T temp=list[i];list[i]=list[j];list[j]=temp;}}
    public static int Clamp(int v,int lo,int hi)=>Math.Min(hi,Math.Max(lo,v));
    public static long Clamp(long v,long lo,long hi)=>Math.Min(hi,Math.Max(lo,v));
    public static double Clamp(double v,double lo,double hi)=>Math.Min(hi,Math.Max(lo,v));
    public static bool IsFinite(double v)=>!double.IsNaN(v)&&!double.IsInfinity(v);
    public static byte[] ExportSpki(ECDsaCng key) { var blob=key.Key.Export(CngKeyBlobFormat.EccPublicBlob);var prefix=Convert.FromBase64String("MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAE");return prefix.Concat(blob.Skip(8)).ToArray(); }
    public static string ExportPem(ECDsaCng key)=>"-----BEGIN PUBLIC KEY-----\n"+Convert.ToBase64String(ExportSpki(key))+"\n-----END PUBLIC KEY-----";
    public static string Hex(byte[] b) => BitConverter.ToString(b).Replace("-", "");
    public static byte[] Sha256(byte[] b) { using(var h=SHA256.Create())return h.ComputeHash(b); }
    public static byte[] Sha256(Stream b) { using(var h=SHA256.Create())return h.ComputeHash(b); }
    public static byte[] Sha512(Stream b) { using(var h=SHA512.Create())return h.ComputeHash(b); }
    public static byte[] Sha1(Stream b) { using(var h=SHA1.Create())return h.ComputeHash(b); }
    public static byte[] Sha512(byte[] b) { using(var h=SHA512.Create())return h.ComputeHash(b); }
    public static byte[] Sha1(byte[] b) { using(var h=SHA1.Create())return h.ComputeHash(b); }
    public static bool IsAsciiDigit(char c)=>c>='0'&&c<='9';
    public static bool IsAsciiLetter(char c)=>(c>='a'&&c<='z')||(c>='A'&&c<='Z');
    public static bool IsAsciiHexDigit(char c)=>IsAsciiDigit(c)||(c>='a'&&c<='f')||(c>='A'&&c<='F');
    public static bool IsPathFullyQualified(string p)=> !string.IsNullOrEmpty(p) && (p.StartsWith(@"\\") || (p.Length>=3&&char.IsLetter(p[0])&&p[1]==':'&&(p[2]=='\\'||p[2]=='/')));
    public static string GetRelativePath(string root,string path) {
        root=Path.GetFullPath(root).TrimEnd('\\','/')+Path.DirectorySeparatorChar;
        var a=new Uri(root);var b=new Uri(Path.GetFullPath(path));
        if(a.Scheme!=b.Scheme || !string.Equals(Path.GetPathRoot(root),Path.GetPathRoot(path),StringComparison.OrdinalIgnoreCase))return Path.GetFullPath(path);
        return Uri.UnescapeDataString(a.MakeRelativeUri(b).ToString()).Replace('/',Path.DirectorySeparatorChar);
    }
    public static void Move(string a,string b,bool overwrite=false) { if(overwrite&&File.Exists(b))File.Replace(a,b,null);else File.Move(a,b); }
    public static Task<string> ReadAllTextAsync(string p,CancellationToken ct=default)=>Task.Run(()=>{ct.ThrowIfCancellationRequested();return File.ReadAllText(p);},ct);
    public static Task<byte[]> ReadAllBytesAsync(string p,CancellationToken ct=default)=>Task.Run(()=>{ct.ThrowIfCancellationRequested();return File.ReadAllBytes(p);},ct);
    public static Task WriteAllTextAsync(string p,string value,CancellationToken ct=default)=>Task.Run(()=>{ct.ThrowIfCancellationRequested();File.WriteAllText(p,value);},ct);
    public static Task WriteAllBytesAsync(string p,byte[] value,CancellationToken ct=default)=>Task.Run(()=>{ct.ThrowIfCancellationRequested();File.WriteAllBytes(p,value);},ct);
    public static string Quote(string s) {
        var b=new System.Text.StringBuilder("\"");int slashes=0;
        foreach(char c in s){if(c=='\\'){slashes++;continue;}if(c=='"'){b.Append('\\',slashes*2+1).Append(c);slashes=0;continue;}b.Append('\\',slashes).Append(c);slashes=0;}
        return b.Append('\\',slashes*2).Append('"').ToString();
    }
    public static void AddArgument(ProcessStartInfo p,string s)=>p.Arguments+=(p.Arguments.Length==0?"":" ")+Quote(s);
    // Accept only the exact P-256 SubjectPublicKeyInfo DER layout used by the pinned keys.
    public static ECDsaCng CreateEcdsa(string pem) {
        var b=Convert.FromBase64String(pem.Replace("-----BEGIN PUBLIC KEY-----","").Replace("-----END PUBLIC KEY-----","").Replace("\r","").Replace("\n","").Trim());
        byte[] prefix={0x30,0x59,0x30,0x13,0x06,0x07,0x2a,0x86,0x48,0xce,0x3d,0x02,0x01,0x06,0x08,0x2a,0x86,0x48,0xce,0x3d,0x03,0x01,0x07,0x03,0x42,0x00,0x04};
        if(b.Length!=91||!b.Take(prefix.Length).SequenceEqual(prefix))throw new CryptographicException("Expected P-256 SPKI public key");
        var blob=new byte[72];Array.Copy(BitConverter.GetBytes(0x31534345),0,blob,0,4);Array.Copy(BitConverter.GetBytes(32),0,blob,4,4);Array.Copy(b,27,blob,8,64);
        using(var k=CngKey.Import(blob,CngKeyBlobFormat.EccPublicBlob))return new ECDsaCng(k);
    }
}
internal static class CompatExtensions
{
    public static System.Collections.Generic.IEnumerable<T> DistinctBy<T,K>(this System.Collections.Generic.IEnumerable<T> values,Func<T,K> key,System.Collections.Generic.IEqualityComparer<K> comparer=null) { var seen=new System.Collections.Generic.HashSet<K>(comparer);foreach(var v in values)if(seen.Add(key(v)))yield return v; }
    public static bool Contains(this string s,string value,StringComparison comparison)=>s.IndexOf(value,comparison)>=0;
    public static Task<byte[]> ReadAsByteArrayAsync(this HttpContent c,CancellationToken ct)=>WithCancellation(c.ReadAsByteArrayAsync(),ct);
    public static Task AsTask(this Task t)=>t;
    public static Task<T> WaitAsync<T>(this Task<T> t,CancellationToken ct)=>WithCancellation(t,ct);
    public static Task<System.Net.Sockets.TcpClient> AcceptTcpClientAsync(this System.Net.Sockets.TcpListener l,CancellationToken ct)=>WithCancellation(l.AcceptTcpClientAsync(),ct);
    public static async Task ReadExactlyAsync(this Stream s,byte[] b,CancellationToken ct) { int n=0;while(n<b.Length){int k=await s.ReadAsync(b,n,b.Length-n,ct).ConfigureAwait(false);if(k==0)throw new EndOfStreamException();n+=k;} }
    public static async Task<string> GetStringAsync(this HttpClient c,string url,CancellationToken ct){using(var response=await c.GetAsync(url,ct).ConfigureAwait(false)){response.EnsureSuccessStatusCode();return await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);}}
    public static Task<Stream> GetStreamAsync(this HttpClient c,string url,CancellationToken ct)=>WithCancellation(c.GetStreamAsync(url),ct);
    public static bool EndsWith(this string s,char c)=>s.Length>0&&s[s.Length-1]==c;
    public static bool StartsWith(this string s,char c)=>s.Length>0&&s[0]==c;
    public static async Task<T> WithCancellation<T>(Task<T> task,CancellationToken ct) { var cancel=new TaskCompletionSource<bool>(); using(ct.Register(()=>cancel.TrySetResult(true))){if(await Task.WhenAny(task,cancel.Task).ConfigureAwait(false)!=task)throw new OperationCanceledException(ct);return await task.ConfigureAwait(false);} }
    public static Task<string> ReadAsStringAsync(this HttpContent c,CancellationToken ct)=>WithCancellation(c.ReadAsStringAsync(),ct);
    public static Task<Stream> ReadAsStreamAsync(this HttpContent c,CancellationToken ct)=>WithCancellation(c.ReadAsStreamAsync(),ct);
    public static async Task<byte[]> GetByteArrayAsync(this HttpClient c,string url,CancellationToken ct){using(var r=await c.GetAsync(url,ct).ConfigureAwait(false)){r.EnsureSuccessStatusCode();return await WithCancellation(r.Content.ReadAsByteArrayAsync(),ct).ConfigureAwait(false);}}
    public static Task<int> ReadAsync(this Stream s,byte[] b,CancellationToken ct)=>s.ReadAsync(b,0,b.Length,ct);
    public static Task WriteAsync(this Stream s,Memory<byte> b,CancellationToken ct){var a=b.ToArray();return s.WriteAsync(a,0,a.Length,ct);}
    public static Task<string> ReadToEndAsync(this StreamReader s,CancellationToken ct)=>WithCancellation(s.ReadToEndAsync(),ct);
    public static async Task WaitForExitAsync(this Process p,CancellationToken ct){while(!p.WaitForExit(0))await Task.Delay(100,ct).ConfigureAwait(false);p.WaitForExit();}
    public static void Kill(this Process p,bool entireProcessTree){if(!entireProcessTree){p.Kill();return;}if(p.HasExited)return;using(var k=Process.Start(new ProcessStartInfo("taskkill.exe","/PID "+p.Id+" /T /F"){UseShellExecute=false,CreateNoWindow=true})){k.WaitForExit();}}
}
namespace Microsoft.Win32 {
 public sealed class OpenFolderDialog {
  public string Title {get;set;}="";
  public string InitialDirectory {get;set;}="";
  public string FolderName {get;private set;}="";
  public bool? ShowDialog(System.Windows.Window owner) {
   var dialog=(IFileOpenDialog)new NativeDialog(); IShellItem folder=null, result=null;
   try {
    dialog.SetOptions(0x20|0x40|0x800|0x8);dialog.SetTitle(Title);
    var initial=string.IsNullOrEmpty(InitialDirectory)?System.AppContext.BaseDirectory:InitialDirectory;
    if(System.IO.Directory.Exists(initial)){var iid=typeof(IShellItem).GUID;SHCreateItemFromParsingName(initial,System.IntPtr.Zero,ref iid,out folder);dialog.SetFolder(folder);}
    int hr=dialog.Show(new System.Windows.Interop.WindowInteropHelper(owner).Handle);
    if(hr==unchecked((int)0x800704C7))return false;
    System.Runtime.InteropServices.Marshal.ThrowExceptionForHR(hr);
    dialog.GetResult(out result);result.GetDisplayName(0x80058000,out var value);
    try{FolderName=System.Runtime.InteropServices.Marshal.PtrToStringUni(value);}finally{System.Runtime.InteropServices.Marshal.FreeCoTaskMem(value);}
    return true;
   }finally{if(result!=null)System.Runtime.InteropServices.Marshal.FinalReleaseComObject(result);if(folder!=null)System.Runtime.InteropServices.Marshal.FinalReleaseComObject(folder);System.Runtime.InteropServices.Marshal.FinalReleaseComObject(dialog);}
  }
  [System.Runtime.InteropServices.DllImport("shell32.dll",CharSet=System.Runtime.InteropServices.CharSet.Unicode,PreserveSig=false)]
  private static extern void SHCreateItemFromParsingName(string path,System.IntPtr bind,ref System.Guid iid,out IShellItem item);
  [System.Runtime.InteropServices.ComImport,System.Runtime.InteropServices.Guid("DC1C5A9C-E88A-4DDE-A5A1-60F82A20AEF7")]private class NativeDialog{}
  [System.Runtime.InteropServices.ComImport,System.Runtime.InteropServices.Guid("D57C7288-D4AD-4768-BE02-9D969532D960"),System.Runtime.InteropServices.InterfaceType(System.Runtime.InteropServices.ComInterfaceType.InterfaceIsIUnknown)]
  private interface IFileOpenDialog {
   [System.Runtime.InteropServices.PreserveSig]int Show(System.IntPtr owner);
   void SetFileTypes(uint count,System.IntPtr types);void SetFileTypeIndex(uint index);void GetFileTypeIndex(out uint index);
   void Advise(System.IntPtr events,out uint cookie);void Unadvise(uint cookie);void SetOptions(uint flags);void GetOptions(out uint flags);
   void SetDefaultFolder(IShellItem folder);void SetFolder(IShellItem folder);void GetFolder(out IShellItem folder);void GetCurrentSelection(out IShellItem item);
   void SetFileName([System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPWStr)]string name);
   void GetFileName(out System.IntPtr name);
   void SetTitle([System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPWStr)]string title);
   void SetOkButtonLabel([System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPWStr)]string text);
   void SetFileNameLabel([System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPWStr)]string text);
   void GetResult(out IShellItem item);void AddPlace(IShellItem item,uint location);void SetDefaultExtension(System.IntPtr extension);void Close(int hr);
   void SetClientGuid(ref System.Guid guid);void ClearClientData();void SetFilter(System.IntPtr filter);void GetResults(out System.IntPtr results);void GetSelectedItems(out System.IntPtr items);
  }
  [System.Runtime.InteropServices.ComImport,System.Runtime.InteropServices.Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"),System.Runtime.InteropServices.InterfaceType(System.Runtime.InteropServices.ComInterfaceType.InterfaceIsIUnknown)]
  private interface IShellItem {
   void BindToHandler(System.IntPtr bind,ref System.Guid handler,ref System.Guid iid,out System.IntPtr result);
   void GetParent(out IShellItem item);void GetDisplayName(uint kind,out System.IntPtr name);void GetAttributes(uint mask,out uint attributes);void Compare(IShellItem other,uint hint,out int order);
  }
 }
}
