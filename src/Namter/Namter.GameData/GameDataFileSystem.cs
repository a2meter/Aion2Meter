namespace Namter.GameData;

internal interface IGameDataFileSystem
{
    bool FileExists(string path);
    bool DirectoryExists(string path);
    FileAttributes GetAttributes(string path);
    void CreateDirectory(string path);
    void DeleteFile(string path);
    void MoveFile(string source, string destination, bool overwrite);
    void ReplaceFile(string source, string destination, string? backup);
}

internal sealed class PhysicalGameDataFileSystem : IGameDataFileSystem
{
    public static PhysicalGameDataFileSystem Instance { get; } = new();
    private PhysicalGameDataFileSystem() { }
    public bool FileExists(string path) => File.Exists(path);
    public bool DirectoryExists(string path) => Directory.Exists(path);
    public FileAttributes GetAttributes(string path) => File.GetAttributes(path);
    public void CreateDirectory(string path) => Directory.CreateDirectory(path);
    public void DeleteFile(string path) => File.Delete(path);
    public void MoveFile(string source, string destination, bool overwrite) => File.Move(source, destination, overwrite);
    public void ReplaceFile(string source, string destination, string? backup)
        => File.Replace(source, destination, backup, ignoreMetadataErrors: true);
}
