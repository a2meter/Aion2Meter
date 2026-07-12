using System.Text.Json;

namespace Namter.Cli.Commands;

public static class ArtifactSetPublisher
{
    private const string MarkerName=".namter-artifacts.json";
    public static void Publish(string destination,IReadOnlyDictionary<string,byte[]> files,Action? beforePromote=null)
    {
        string target=Path.GetFullPath(destination);string? parent=Path.GetDirectoryName(target);if(parent is null||string.Equals(target,Path.GetPathRoot(target),StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Artifact output cannot be a filesystem root.");EnsureSafeAncestors(parent);Directory.CreateDirectory(parent);EnsureDirectorySafe(parent);
        string name=Path.GetFileName(target),stage=Path.Combine(parent,$".{name}.namter-stage"),backup=Path.Combine(parent,$".{name}.namter-backup");Recover(target,stage,backup);
        ValidateReplaceable(target);Directory.CreateDirectory(stage);
        try
        {
            foreach(var pair in files.OrderBy(x=>x.Key,StringComparer.Ordinal)){string relative=ValidateRelative(pair.Key);CommandSupport.AtomicWrite(Path.Combine(stage,relative),pair.Value);}
            byte[] marker=CommandSupport.Json(w=>{w.WriteStartObject();w.WriteString("format","namter-artifact-set-v1");w.WriteStartArray("files");foreach(string file in files.Keys.Order(StringComparer.Ordinal))w.WriteStringValue(file.Replace('\\','/'));w.WriteEndArray();w.WriteEndObject();});CommandSupport.AtomicWrite(Path.Combine(stage,MarkerName),marker);
            beforePromote?.Invoke();
            if(Directory.Exists(target))Directory.Move(target,backup);
            try{Directory.Move(stage,target);}catch{if(!Directory.Exists(target)&&Directory.Exists(backup))Directory.Move(backup,target);throw;}
            if(Directory.Exists(backup))DeleteReplaceable(backup);
        }
        catch
        {
            if(Directory.Exists(stage)&&IsOwned(stage))DeleteOwned(stage);
            throw;
        }
    }

    private static void Recover(string target,string stage,string backup)
    {
        if(Directory.Exists(backup)){if(!Directory.Exists(target)){if(!IsOwned(backup)&&Directory.EnumerateFileSystemEntries(backup).Any())throw new InvalidDataException("Artifact backup is not Namter-owned.");Directory.Move(backup,target);}else DeleteReplaceable(backup);}
        if(Directory.Exists(stage)){if(!IsOwned(stage))throw new InvalidDataException("Unmarked interrupted artifact staging requires manual recovery.");DeleteOwned(stage);}
    }
    private static void ValidateReplaceable(string target){if(!Directory.Exists(target))return;EnsureDirectorySafe(target);if(!Directory.EnumerateFileSystemEntries(target).Any())return;if(!IsOwned(target))throw new InvalidDataException("Output directory is non-empty and is not a Namter artifact set.");}
    private static bool IsOwned(string path){string marker=Path.Combine(path,MarkerName);if(!File.Exists(marker)||new FileInfo(marker).Length>1024*1024||(File.GetAttributes(marker)&FileAttributes.ReparsePoint)!=0)return false;try{using JsonDocument doc=JsonDocument.Parse(File.ReadAllBytes(marker));return doc.RootElement.GetProperty("format").GetString()=="namter-artifact-set-v1"&&doc.RootElement.GetProperty("files").ValueKind==JsonValueKind.Array;}catch{return false;}}
    private static void DeleteOwned(string path){EnsureDirectorySafe(path);if(!IsOwned(path))throw new InvalidDataException("Refusing to delete an unowned artifact directory.");Directory.Delete(path,true);}
    private static void DeleteReplaceable(string path){EnsureDirectorySafe(path);if(Directory.EnumerateFileSystemEntries(path).Any())DeleteOwned(path);else Directory.Delete(path);}
    private static string ValidateRelative(string path){string normalized=path.Replace('/','\\');if(Path.IsPathRooted(normalized)||normalized.Split('\\').Any(x=>x is "" or "." or ".."))throw new InvalidDataException("Artifact path is unsafe.");return normalized;}
    private static void EnsureSafeAncestors(string path){for(DirectoryInfo? d=new(path);d is not null;d=d.Parent)if(d.Exists&&(d.Attributes&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("Artifact path contains a reparse point.");}
    private static void EnsureDirectorySafe(string path){if((new DirectoryInfo(path).Attributes&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("Artifact directory is a reparse point.");}
}
