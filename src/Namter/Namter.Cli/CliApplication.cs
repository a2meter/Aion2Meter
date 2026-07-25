using Namter.Cli.Commands;

namespace Namter.Cli;

public enum CliExitCode { Success = 0, Usage = 2, InputNotFound = 3, InvalidData = 4, NpcapNotInstalled = 5, BackendUnavailable = 6, ComparisonMismatch = 7, Cancelled = 8, InternalError = 10 }

public static class CliApplication
{
    public const string HelpText = """
        Namter capture correctness CLI
        namter replay --input <file-or-dir> --data <aion.db> --output <dir> [--speed 0|1|10]
        namter capture --backend windivert|npcap --data <aion.db> --output <dir> [--packet-log <dir>]
        namter compare --actual <record.json> --expected <readable-dir> --report <report.json>
        namter data status|check|rollback --data-dir <dir>
        """;

    public static async Task<int> RunAsync(string[] args, TextWriter output, TextWriter error, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(args); ArgumentNullException.ThrowIfNull(output); ArgumentNullException.ThrowIfNull(error);
        if (args.Length == 0 || args is ["--help"] or ["-h"] or ["help"]) { await output.WriteLineAsync(HelpText); return 0; }
        try
        {
            return args[0] switch
            {
                "replay" => await ReplayCommand.RunAsync(CommandArguments.Parse(args[1..]), output, error, cancellationToken),
                "capture" => await CaptureCommand.RunAsync(CommandArguments.Parse(args[1..]), output, error, cancellationToken),
                "compare" => await CompareCommand.RunAsync(CommandArguments.Parse(args[1..]), output, error, cancellationToken),
                "data" => await DataCommand.RunAsync(args[1..], output, error, cancellationToken),
                _ => await UsageAsync(error, $"Unknown command '{args[0]}'."),
            };
        }
        catch (CommandLineException ex) { return await UsageAsync(error, ex.Message); }
        catch (OperationCanceledException) { await error.WriteLineAsync("Operation cancelled."); return (int)CliExitCode.Cancelled; }
        catch (FileNotFoundException ex) { await error.WriteLineAsync(ex.Message); return (int)CliExitCode.InputNotFound; }
        catch (DirectoryNotFoundException ex) { await error.WriteLineAsync(ex.Message); return (int)CliExitCode.InputNotFound; }
        catch (InvalidDataException ex) { await error.WriteLineAsync(ex.Message); return (int)CliExitCode.InvalidData; }
        catch (Microsoft.Data.Sqlite.SqliteException ex) { await error.WriteLineAsync($"Invalid aion.db: {ex.Message}"); return (int)CliExitCode.InvalidData; }
        catch (Exception ex) { await error.WriteLineAsync($"Internal error: {ex.Message}"); return (int)CliExitCode.InternalError; }
    }

    private static async Task<int> UsageAsync(TextWriter error, string message) { await error.WriteLineAsync(message); await error.WriteLineAsync(HelpText); return (int)CliExitCode.Usage; }
}

public sealed class CommandLineException(string message) : Exception(message);

public sealed class CommandArguments
{
    private readonly Dictionary<string, string> values;
    private CommandArguments(Dictionary<string, string> values) => this.values = values;
    public static CommandArguments Parse(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int i = 0; i < args.Length; i += 2)
        {
            if (!args[i].StartsWith("--", StringComparison.Ordinal) || i + 1 >= args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal)) throw new CommandLineException("Options require --name value pairs.");
            if (!values.TryAdd(args[i], args[i + 1])) throw new CommandLineException($"Duplicate option '{args[i]}'.");
        }
        return new(values);
    }
    public string Require(string name) => values.TryGetValue(name, out string? value) && !string.IsNullOrWhiteSpace(value) ? value : throw new CommandLineException($"Missing required option '{name}'.");
    public string? Optional(string name) => values.GetValueOrDefault(name);
    public void EnsureOnly(params string[] names)
    {
        var allowed = names.ToHashSet(StringComparer.Ordinal);
        string? unknown = values.Keys.FirstOrDefault(x => !allowed.Contains(x));
        if (unknown is not null) throw new CommandLineException($"Unknown option '{unknown}'.");
    }
}
