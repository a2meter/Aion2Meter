using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Namter.Cli.Commands;

internal static class CommandSupport
{
    internal static string ExistingFile(string path) { string full=Path.GetFullPath(path); if(!File.Exists(full))throw new FileNotFoundException("Input file was not found.",full); return full; }
    internal static string ExistingFileOrDirectory(string path) { string full=Path.GetFullPath(path); if(!File.Exists(full)&&!Directory.Exists(full))throw new FileNotFoundException("Input file or directory was not found.",full); return full; }
    internal static string OutputDirectory(string path) { string full=Path.GetFullPath(path); if(File.Exists(full))throw new InvalidDataException("Output path is a file."); Directory.CreateDirectory(full); return full; }
    internal static void AtomicWrite(string path, ReadOnlySpan<byte> bytes)
    {
        string full=Path.GetFullPath(path); string? dir=Path.GetDirectoryName(full); if(string.IsNullOrEmpty(dir))throw new InvalidDataException("Output has no directory."); Directory.CreateDirectory(dir); string temp=Path.Combine(dir,$".{Path.GetFileName(full)}.{Guid.NewGuid():N}.tmp");
        try { using(var stream=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None,4096,FileOptions.WriteThrough)){stream.Write(bytes);stream.Flush(true);} File.Move(temp,full,true); }
        finally { if(File.Exists(temp))File.Delete(temp); }
    }
    internal static byte[] Json(Action<Utf8JsonWriter> write){using var ms=new MemoryStream();using(var w=new Utf8JsonWriter(ms)){write(w);}return ms.ToArray();}
    internal static string Sha256(ReadOnlySpan<byte> bytes)=>Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    internal static Guid StableGuid(string text){byte[] h=SHA256.HashData(Encoding.UTF8.GetBytes(text));return new Guid(h.AsSpan(0,16));}
}
