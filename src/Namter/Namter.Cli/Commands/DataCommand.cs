using System.Text.Json;
using Namter.GameData;

namespace Namter.Cli.Commands;

public static class DataCommand
{
    public static async Task<int> RunAsync(string[] args,TextWriter output,TextWriter error,CancellationToken cancellationToken)
    {
        if(args.Length<1)throw new CommandLineException("Missing data subcommand."); string action=args[0]; var options=CommandArguments.Parse(args[1..]); options.EnsureOnly("--data-dir"); string root=Path.GetFullPath(options.Require("--data-dir")); var updater=GameDataUpdater.CreateLocal(root,typeof(DataCommand).Assembly.GetName().Version??new Version(1,0),1);
        if(action=="status") { GameDataLocalResult local=await updater.InspectLocalAsync(cancellationToken);CliConfiguration c=CliConfiguration.Load();byte[] json=CommandSupport.Json(w=>{w.WriteStartObject();w.WriteString("localDatabase",local.Status.ToString());w.WriteString("remoteUpdate",c.RemoteStatus);w.WriteString("path",Path.Combine(root,"aion.db"));w.WriteBoolean("rollbackAvailable",local.BackupAvailable);if(local.Detail is null)w.WriteNull("detail");else w.WriteString("detail",local.Detail);w.WriteEndObject();});await output.WriteLineAsync(System.Text.Encoding.UTF8.GetString(json));return LocalExit(local.Status); }
        if(action=="check") { GameDataLocalResult local=await updater.InspectLocalAsync(cancellationToken);await output.WriteLineAsync($"aion.db: {local.Status}");return LocalExit(local.Status); }
        if(action=="rollback") { GameDataRollbackResult result=await updater.RollbackAsync(cancellationToken);await output.WriteLineAsync($"aion.db rollback: {result.Status}");return result.Status==GameDataRollbackStatus.RolledBack?0:result.Status==GameDataRollbackStatus.NoBackup?(int)CliExitCode.InputNotFound:(int)CliExitCode.InvalidData; }
        throw new CommandLineException($"Unknown data subcommand '{action}'.");
    }
    private static int LocalExit(GameDataLocalStatus status)=>status switch{GameDataLocalStatus.Valid=>0,GameDataLocalStatus.Missing=>(int)CliExitCode.InputNotFound,GameDataLocalStatus.Cancelled=>(int)CliExitCode.Cancelled,_=>(int)CliExitCode.InvalidData};
}
