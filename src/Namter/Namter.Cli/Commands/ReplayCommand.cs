namespace Namter.Cli.Commands;

public static class ReplayCommand
{
    public static async Task<int> RunAsync(CommandArguments args,TextWriter output,TextWriter error,CancellationToken cancellationToken)
    {
        args.EnsureOnly("--input","--data","--output","--speed"); string inputArg=args.Require("--input"),dataArg=args.Require("--data"),outputArg=args.Require("--output"); string speedText=args.Optional("--speed")??"0"; if(!int.TryParse(speedText,out int speed)||speed is not(0 or 1 or 10))throw new CommandLineException("--speed must be 0, 1, or 10."); string input=CommandSupport.ExistingFileOrDirectory(inputArg); string data=CommandSupport.ExistingFile(dataArg); string destination=CommandSupport.OutputDirectory(outputArg);
        string[] files=File.Exists(input)?[input]:Directory.GetFiles(input,"*.pcap",SearchOption.AllDirectories).OrderBy(x=>x,StringComparer.Ordinal).ToArray(); if(files.Length==0)throw new InvalidDataException("Replay input contains no .pcap files.");if(files.Length>1024)throw new InvalidDataException("Replay directory exceeds the 1024-file bound.");
        return await PipelineRunner.ReplayAsync(files,data,destination,speed,output,cancellationToken);
    }
}
