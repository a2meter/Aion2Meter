using Namter.Cli.Comparison;

namespace Namter.Cli.Commands;

public static class CompareCommand
{
    public static Task<int> RunAsync(CommandArguments args,TextWriter output,TextWriter error,CancellationToken cancellationToken)
    {
        args.EnsureOnly("--actual","--expected","--report"); string actualArg=args.Require("--actual"),expectedArg=args.Require("--expected"),reportArg=args.Require("--report"); string actual=CommandSupport.ExistingFile(actualArg); string expected=Path.GetFullPath(expectedArg); string report=Path.GetFullPath(reportArg); cancellationToken.ThrowIfCancellationRequested();
        ComparisonReport result=GoldenComparator.Compare(actual,ReadableFixtureLoader.Load(expected)); byte[] bytes=result.WriteStableJson(); CommandSupport.AtomicWrite(report,bytes); output.WriteLine(result.IsMatch?"MATCH":"MISMATCH");
        if(!result.IsMatch)error.WriteLine($"Comparison mismatch: {result.Discrepancies.Length} discrepancies, {result.Missing.Length} missing, {result.Extra.Length} extra. See {report}"); return Task.FromResult(result.IsMatch?0:(int)CliExitCode.ComparisonMismatch);
    }
}
