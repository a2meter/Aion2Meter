using System.Text.Json;
using Namter.GameData;

namespace Namter.Cli.Commands;

public static class DataCommand
{
    public static async Task<int> RunAsync(string[] args,TextWriter output,TextWriter error,CancellationToken cancellationToken)
    {
        if(args.Length<1)throw new CommandLineException("Missing data subcommand."); string action=args[0]; var options=CommandArguments.Parse(args[1..]); options.EnsureOnly("--data-dir"); string root=Path.GetFullPath(options.Require("--data-dir")); string active=Path.Combine(root,"aion.db"), backup=Path.Combine(root,"backup","aion.previous.db");
        if(action=="status") { CliConfiguration c=CliConfiguration.Load(); byte[] json=CommandSupport.Json(w=>{w.WriteStartObject();w.WriteString("localDatabase",File.Exists(active)?"Present":"Missing");w.WriteString("remoteUpdate",c.RemoteStatus);w.WriteString("path",active);w.WriteBoolean("rollbackAvailable",File.Exists(backup));w.WriteEndObject();});await output.WriteLineAsync(System.Text.Encoding.UTF8.GetString(json));return File.Exists(active)?0:(int)CliExitCode.InputNotFound; }
        if(action=="check") { await Load(active,cancellationToken);await output.WriteLineAsync("aion.db: valid");return 0; }
        if(action=="rollback") { if(!File.Exists(active)||!File.Exists(backup))throw new FileNotFoundException("Active database and backup are required for rollback."); await Load(backup,cancellationToken);string failed=Path.Combine(root,"backup","aion.failed.db");Directory.CreateDirectory(Path.GetDirectoryName(backup)!);File.Replace(backup,active,failed,true);try{await Load(active,cancellationToken);if(File.Exists(failed))File.Move(failed,backup,true);}catch{if(File.Exists(failed))File.Replace(failed,active,backup,true);throw;}await output.WriteLineAsync("aion.db: rollback complete");return 0; }
        throw new CommandLineException($"Unknown data subcommand '{action}'.");
    }
    private static Task<GameDataSnapshot> Load(string path,CancellationToken ct)=>new GameDataRepository(CommandSupport.ExistingFile(path),GameDataCacheLimits.Default).LoadAsync(ct);
}
