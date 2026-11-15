using System.Globalization;
using LostInLimbo.LevelGen.Data.Prop;
using LostInLimbo.LevelGen.SeedSweep.Data;

namespace LostInLimbo.LevelGen.SeedSweep.Cli;

/// <summary>
/// Parses the harness command line. Pure: no console output, no file access.
/// </summary>
/// <remarks>
/// Grammar: <c>[dump] [index...] [--option value]... [--help]</c>. Bare integers are dump
/// indices and are only valid after <c>dump</c>. Every problem is collected, not only the first.
/// </remarks>
internal static class SweepOptionsParser
{
  /// <summary>Usage text printed by <c>--help</c> and after invalid arguments.</summary>
  public const string Usage =
    """
    Lost in Limbo level generation seed sweep.

    Usage:
      dotnet run --project LostInLimbo.LevelGen.SeedSweep -c Release -- [options]
      dotnet run --project LostInLimbo.LevelGen.SeedSweep -c Release -- dump <index...> [options]
      dotnet run --project LostInLimbo.LevelGen.SeedSweep -c Release -- dump --text-seed <seed> --difficulty <d> --many-themes on|off

    Options:
      --runs N                 Number of runs (default 10000).
      --difficulty A[-B]       Difficulty or inclusive range, 0-20 (default 0-20). Runs cycle through it.
      --many-themes MODE       on, off or both (default both: alternates per difficulty cycle).
      --seed-prefix P          Text seed of run i is "P-i" (default sweep).
      --start-index N          Index of the first run (default 0).
      --determinism-every N    Generate every Nth run twice and compare; 0 disables (default 25).
      --parallel N             Degree of parallelism; 0 = processor count (default 0).
      --timeout-seconds N      Per-run timeout (default 20).
      --out DIR                Output root (default <project>/TestResults).
      --text-seed S            dump only: dump this text seed instead of run indices.
      --help                   Show this text.

    Results go to <output root>/<yyyyMMdd-HHmmss>/.
    Exit codes: 0 = no errors, 1 = at least one run with errors, 2 = invalid arguments.
    """;

  /// <summary>
  /// Parses the arguments.
  /// </summary>
  /// <param name="args">Command-line arguments, without the program name.</param>
  /// <returns>Options and the list of problems; never throws for bad input.</returns>
  /// <exception cref="ArgumentNullException"><paramref name="args"/> is null.</exception>
  public static SweepParseResult Parse(IReadOnlyList<string> args)
  {
    if (args is null) throw new ArgumentNullException(nameof(args));

    var errors = new List<string>();
    var options = new SweepOptions();
    var dumpIndices = new List<int>();
    var position = 0;

    if (args.Count > 0 && args[0] == "dump")
    {
      options = options with { Command = EnSweepCommand.Dump };
      position = 1;
    }

    for (; position < args.Count; position++)
    {
      var arg = args[position];
      if (arg is "--help" or "-h")
        return new SweepParseResult(options with { Command = EnSweepCommand.Help }, []);

      if (!arg.StartsWith("--", StringComparison.Ordinal))
      {
        if (options.Command == EnSweepCommand.Dump && TryParseInt(arg, out var index) && index >= 0)
          dumpIndices.Add(index);
        else
          errors.Add($"Unexpected argument '{arg}'.");
        continue;
      }

      if (position + 1 >= args.Count)
      {
        errors.Add($"Option {arg} needs a value.");
        continue;
      }

      var value = args[++position];
      options = ApplyOption(options, arg, value, errors);
    }

    options = options with { DumpIndices = dumpIndices };
    Validate(options, errors);
    return new SweepParseResult(options, errors);
  }

  /// <summary>Command that runs the harness from the repository root; arguments follow it.</summary>
  public const string RunCommand = "dotnet run --project LostInLimbo.LevelGen.SeedSweep -c Release --";

  /// <summary>
  /// Inverse of <see cref="Parse"/>: every option that affects the result, written out
  /// explicitly (defaults included), so parsing the arguments gives the same options.
  /// </summary>
  /// <remarks>
  /// A sweep lists all sweep options. A dump lists its indices or text seed plus the options
  /// that shape a dumped run (difficulty, themes, seed prefix, timeout, output root).
  /// <see cref="SweepOptions.OutputRoot"/> is included only when set.
  /// </remarks>
  /// <param name="options">Options to write out.</param>
  /// <returns>Argument list, without the program name.</returns>
  public static IReadOnlyList<string> ToArguments(SweepOptions options)
  {
    if (options is null) throw new ArgumentNullException(nameof(options));

    var args = new List<string>();
    var difficulty = options.DifficultyMin == options.DifficultyMax
      ? Invariant(options.DifficultyMin)
      : $"{Invariant(options.DifficultyMin)}-{Invariant(options.DifficultyMax)}";
    var manyThemes = options.ManyThemes.ToString().ToLowerInvariant();

    if (options.Command == EnSweepCommand.Dump)
    {
      args.Add("dump");
      if (options.DumpTextSeed is not null) args.AddRange(["--text-seed", options.DumpTextSeed]);
      else args.AddRange(options.DumpIndices.Select(Invariant));

      args.AddRange(["--difficulty", difficulty, "--many-themes", manyThemes]);
      if (options.DumpTextSeed is null) args.AddRange(["--seed-prefix", options.SeedPrefix]);
    }
    else
    {
      args.AddRange([
        "--runs", Invariant(options.Runs),
        "--difficulty", difficulty,
        "--many-themes", manyThemes,
        "--seed-prefix", options.SeedPrefix,
        "--start-index", Invariant(options.StartIndex),
        "--determinism-every", Invariant(options.DeterminismEvery),
        "--parallel", Invariant(options.Parallelism),
      ]);
    }

    args.AddRange(["--timeout-seconds", Invariant(options.TimeoutSeconds)]);
    if (options.OutputRoot is not null) args.AddRange(["--out", options.OutputRoot]);
    return args;
  }

  /// <summary>Joins arguments into one shell line, quoting those with spaces or quotes.</summary>
  /// <param name="args">Arguments.</param>
  /// <returns>Command-line text.</returns>
  public static string JoinArguments(IEnumerable<string> args)
  {
    if (args is null) throw new ArgumentNullException(nameof(args));

    return string.Join(" ", args.Select(a =>
      a.Length == 0 || a.Any(ch => char.IsWhiteSpace(ch) || ch == '"') ? $"\"{a.Replace("\"", "\\\"")}\"" : a));
  }

  //
  // Internals
  //

  /// <summary>Formats an integer with the invariant culture.</summary>
  private static string Invariant(int value) => value.ToString(CultureInfo.InvariantCulture);

  /// <summary>Applies one <c>--option value</c> pair.</summary>
  /// <param name="options">Options so far.</param>
  /// <param name="name">Option name including the dashes.</param>
  /// <param name="value">Option value.</param>
  /// <param name="errors">Problem list to extend.</param>
  /// <returns>Options with the value applied, or unchanged on a problem.</returns>
  private static SweepOptions ApplyOption(SweepOptions options, string name, string value, List<string> errors)
  {
    switch (name)
    {
      case "--runs":
        return WithInt(options, name, value, errors, (o, v) => o with { Runs = v });
      case "--start-index":
        return WithInt(options, name, value, errors, (o, v) => o with { StartIndex = v });
      case "--determinism-every":
        return WithInt(options, name, value, errors, (o, v) => o with { DeterminismEvery = v });
      case "--parallel":
        return WithInt(options, name, value, errors, (o, v) => o with { Parallelism = v });
      case "--timeout-seconds":
        return WithInt(options, name, value, errors, (o, v) => o with { TimeoutSeconds = v });
      case "--seed-prefix":
        return options with { SeedPrefix = value };
      case "--out":
        return options with { OutputRoot = value };
      case "--text-seed":
        return options with { DumpTextSeed = value };
      case "--difficulty":
        return ApplyDifficulty(options, value, errors);
      case "--many-themes":
        return ApplyManyThemes(options, value, errors);
      default:
        errors.Add($"Unknown option {name}.");
        return options;
    }
  }

  /// <summary>Parses an integer option value and applies it.</summary>
  private static SweepOptions WithInt(
    SweepOptions options, string name, string value, List<string> errors, Func<SweepOptions, int, SweepOptions> apply)
  {
    if (TryParseInt(value, out var number)) return apply(options, number);

    errors.Add($"Option {name} needs an integer, got '{value}'.");
    return options;
  }

  /// <summary>Parses <c>A</c> or <c>A-B</c> into the difficulty range.</summary>
  private static SweepOptions ApplyDifficulty(SweepOptions options, string value, List<string> errors)
  {
    var parts = value.Split('-');
    if (parts.Length is 1 or 2
        && TryParseInt(parts[0], out var min)
        && TryParseInt(parts[^1], out var max))
    {
      return options with { DifficultyMin = min, DifficultyMax = max };
    }

    errors.Add($"Option --difficulty needs 'A' or 'A-B', got '{value}'.");
    return options;
  }

  /// <summary>Parses <c>on</c>, <c>off</c> or <c>both</c>.</summary>
  private static SweepOptions ApplyManyThemes(SweepOptions options, string value, List<string> errors)
  {
    switch (value.ToLowerInvariant())
    {
      case "on": return options with { ManyThemes = EnManyThemesMode.On };
      case "off": return options with { ManyThemes = EnManyThemesMode.Off };
      case "both": return options with { ManyThemes = EnManyThemesMode.Both };
      default:
        errors.Add($"Option --many-themes needs on, off or both, got '{value}'.");
        return options;
    }
  }

  /// <summary>Checks value ranges and combinations once all arguments are read.</summary>
  private static void Validate(SweepOptions options, List<string> errors)
  {
    if (options.Runs < 1) errors.Add("--runs must be at least 1.");
    if (options.StartIndex < 0) errors.Add("--start-index must not be negative.");
    if (options.DeterminismEvery < 0) errors.Add("--determinism-every must not be negative.");
    if (options.Parallelism < 0) errors.Add("--parallel must not be negative.");
    if (options.TimeoutSeconds < 1) errors.Add("--timeout-seconds must be at least 1.");
    if (string.IsNullOrWhiteSpace(options.SeedPrefix)) errors.Add("--seed-prefix must not be empty.");
    if (options.DifficultyMin < 0 || options.DifficultyMax > PropMain.MaxDifficulty || options.DifficultyMin > options.DifficultyMax)
      errors.Add($"--difficulty must be within 0-{PropMain.MaxDifficulty}, low end first.");

    if (options.Command != EnSweepCommand.Dump)
    {
      if (options.DumpTextSeed is not null) errors.Add("--text-seed is only valid with dump.");
      return;
    }

    var hasSeed = !string.IsNullOrWhiteSpace(options.DumpTextSeed);
    if (hasSeed == options.DumpIndices.Count > 0)
      errors.Add("dump needs either run indices or --text-seed (not both).");

    if (hasSeed)
    {
      if (options.DifficultyMin != options.DifficultyMax) errors.Add("dump --text-seed needs a single --difficulty.");
      if (options.ManyThemes == EnManyThemesMode.Both) errors.Add("dump --text-seed needs --many-themes on or off.");
    }
  }

  /// <summary>Parses an integer with the invariant culture.</summary>
  private static bool TryParseInt(string text, out int value)
  {
    return int.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value);
  }
}
