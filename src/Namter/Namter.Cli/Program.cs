using Namter.Cli;

var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true; // Stop the capture cooperatively so artifacts are flushed instead of hard-killing the process.
    cancellation.Cancel();
};
return await CliApplication.RunAsync(args, Console.Out, Console.Error, cancellation.Token);
