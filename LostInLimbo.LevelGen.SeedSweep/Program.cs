using System.Diagnostics;
using System.Text;
using LostInLimbo.LevelGen.Api;
using LostInLimbo.LevelGen.SeedSweep.Cli;
using LostInLimbo.LevelGen.SeedSweep.Data;
using LostInLimbo.LevelGen.SeedSweep.Export;
using LostInLimbo.LevelGen.SeedSweep.Runner;

namespace LostInLimbo.LevelGen.SeedSweep;

/// <summary>
/// Entry point of the seed sweep: parses arguments, runs a sweep or a dump and writes results to
/// a timestamped folder under <see cref="ResultPaths.DefaultRoot"/> (or <c>--out</c>).
/// </summary>
internal static class Program
{
  /// <summary>Exit code: no run had errors.</summary>
  public const int ExitOk = 0;
  /// <summary>Exit code: at least one run had errors.</summary>
  public const int ExitErrors = 1;
  /// <summary>Exit code: invalid arguments.</summary>
  public const int ExitBadArguments = 2;

  /// <summary>Report progress every this many finished runs.</summary>
  private const int ProgressEvery = 500;

  /// <summary>Entry point.</summary>
  /// <param name="args">Command-line arguments.</param>
  /// <returns>Exit code, see <see cref="ExitOk"/>, <see cref="ExitErrors"/>, <see cref="ExitBadArguments"/>.</returns>
  private static int Main(string[] args)
  {
    var parsed = SweepOptionsParser.Parse(args);
    if (!parsed.Success)
    {
      foreach (var error in parsed.Errors) Console.Error.WriteLine($"error: {error}");
      Console.Error.WriteLine();
      Console.Error.WriteLine(SweepOptionsParser.Usage);
      return ExitBadArguments;
    }

    // Resolve the output root up front so options.md can list it explicitly.
    var options = parsed.Options with { OutputRoot = parsed.Options.OutputRoot ?? ResultPaths.DefaultRoot };
    var commandLine = string.Join(" ", args.Select(Quote));
    switch (options.Command)
    {
      case EnSweepCommand.Help:
        Console.WriteLine(SweepOptionsParser.Usage);
        return ExitOk;
      case EnSweepCommand.Dump:
        return RunDump(options, commandLine);
      default:
        return RunSweep(options, commandLine);
    }
  }

  /// <summary>Runs a sweep and writes report.md, runs.csv and options.md.</summary>
  private static int RunSweep(SweepOptions options, string commandLine)
  {
    var folder = ResultPaths.CreateRunFolder(options.OutputRoot ?? ResultPaths.DefaultRoot, DateTime.Now);
    Console.WriteLine($"LevelGen assembly: {typeof(LevelGenConfig).Assembly.Location}");
    Console.WriteLine($"Output: {folder}");
    Console.WriteLine($"Runs: {options.Runs}, parallelism {options.EffectiveParallelism}");

    if (options.WarmupRuns > 0) Console.WriteLine($"Warming up ({options.WarmupRuns} untimed runs)...");

    // The sweep's wall time includes the warm-up; per-run timings do not.
    var stopwatch = Stopwatch.StartNew();
    var result = SweepRunner.RunAll(options, finished =>
    {
      if (finished % ProgressEvery == 0 || finished == options.Runs) Console.WriteLine($"  {finished}/{options.Runs}");
    });
    stopwatch.Stop();

    var report = SweepReport.BuildReport(result, stopwatch.Elapsed, commandLine);
    File.WriteAllText(Path.Combine(folder, "report.md"), report);
    File.WriteAllText(Path.Combine(folder, "runs.csv"), SweepReport.BuildCsv(result.Records));
    File.WriteAllText(Path.Combine(folder, "options.md"), SweepReport.BuildOptions(options, commandLine, typeof(LevelGenConfig).Assembly.Location));

    Console.WriteLine();
    Console.WriteLine(report);
    Console.WriteLine($"Results written to {folder}");
    var anyErrors = result.Records.Concat(result.WarmupRecords).Any(r => r.Issues.Count > 0);
    return anyErrors ? ExitErrors : ExitOk;
  }

  /// <summary>Dumps the selected runs to the console and to dump-*.md.</summary>
  private static int RunDump(SweepOptions options, string commandLine)
  {
    var specs = options.DumpTextSeed is not null
      ? [RunSpec.ForTextSeed(options)]
      : options.DumpIndices.Select(i => RunSpec.ForIndex(i, options)).ToList();

    // Warm up so the generation time shown for the dumped runs excludes JIT compilation.
    var warmup = SweepRunner.WarmUp(options.WarmupRuns, TimeSpan.FromSeconds(options.TimeoutSeconds));
    var text = new StringBuilder();
    var hasErrors = warmup.Any(r => r.Issues.Count > 0);
    foreach (var failed in warmup.Where(r => r.Issues.Count > 0))
      Console.Error.WriteLine($"warm-up run {failed.Label} {failed.Seed}: {string.Join("; ", failed.Issues)}");
    foreach (var spec in specs)
    {
      var (record, config, world) = SweepRunner.RunDetailed(spec, checkDeterminism: false, TimeSpan.FromSeconds(options.TimeoutSeconds));
      hasErrors |= record.Issues.Count > 0;
      if (config is null) text.AppendLine($"## {spec}").AppendLine().AppendLine($"No config: {string.Join("; ", record.Issues)}").AppendLine();
      else text.Append(GraphDumper.Dump(spec, config, world?.Graph, record));
    }

    var folder = ResultPaths.CreateRunFolder(options.OutputRoot ?? ResultPaths.DefaultRoot, DateTime.Now);
    var fileName = options.DumpTextSeed is not null
      ? $"dump-seed-{SafeFileName(options.DumpTextSeed)}.md"
      : $"dump-{string.Join("-", options.DumpIndices)}.md";
    File.WriteAllText(Path.Combine(folder, fileName), $"# Seed sweep dump{Environment.NewLine}{Environment.NewLine}Command line: `{commandLine}`{Environment.NewLine}{Environment.NewLine}{text}");
    File.WriteAllText(Path.Combine(folder, "options.md"), SweepReport.BuildOptions(options, commandLine, typeof(LevelGenConfig).Assembly.Location));

    Console.Write(text);
    Console.WriteLine($"Dump written to {Path.Combine(folder, fileName)}");
    return hasErrors ? ExitErrors : ExitOk;
  }

  /// <summary>Quotes an argument containing spaces.</summary>
  private static string Quote(string arg) => arg.Contains(' ') ? $"\"{arg}\"" : arg;

  /// <summary>Replaces characters not allowed in file names.</summary>
  private static string SafeFileName(string text)
  {
    var invalid = Path.GetInvalidFileNameChars();
    return new string(text.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray());
  }
}
