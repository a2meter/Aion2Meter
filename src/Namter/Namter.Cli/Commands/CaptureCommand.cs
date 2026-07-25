using Namter.Core.Interop;

namespace Namter.Cli.Commands;

public static class CaptureCommand
{
    public static async Task<int> RunAsync(CommandArguments args,TextWriter output,TextWriter error,CancellationToken cancellationToken)
    {
        args.EnsureOnly("--backend","--data","--output","--packet-log"); string backend=args.Require("--backend"); NativeSourceKind kind=backend switch{"windivert"=>NativeSourceKind.WinDivert,"npcap"=>NativeSourceKind.Npcap,_=>throw new CommandLineException("--backend must be windivert or npcap; automatic fallback is forbidden.")};
        string data=CommandSupport.ExistingFile(args.Require("--data")); string destination=CommandSupport.OutputDirectory(args.Require("--output"));
        string? packetLog=args.Optional("--packet-log") is string raw&&!string.IsNullOrWhiteSpace(raw)?CommandSupport.OutputDirectory(raw):null;
        try{return await PipelineRunner.CaptureAsync(kind,data,destination,output,cancellationToken,packetLog);}
        catch(NativeCoreException ex) when(ex.StatusCode==5){return await WriteNpcapNotInstalledAsync(error);}
        catch(NativeCoreException ex) when(ex.StatusCode is 6 or 7){await error.WriteLineAsync($"Selected backend is unavailable: {ex.Message}");return (int)CliExitCode.BackendUnavailable;}
    }

    public static async Task<int> WriteNpcapNotInstalledAsync(TextWriter error)
    {
        await error.WriteLineAsync("Npcap status: NotInstalled (no compatible external Npcap x64 runtime detected).");
        await error.WriteLineAsync("Download Npcap manually from the official page: https://npcap.com/#download");
        await error.WriteLineAsync("Requirements: install official Npcap x64, then retry the same explicit --backend npcap command. No installer, browser, download, HTTP request, or backend fallback was started.");
        return (int)CliExitCode.NpcapNotInstalled;
    }
}
