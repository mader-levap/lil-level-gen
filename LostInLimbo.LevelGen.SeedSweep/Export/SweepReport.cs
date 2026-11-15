using System.Globalization;
using System.Reflection;
using System.Text;
using LostInLimbo.LevelGen.SeedSweep.Cli;
using LostInLimbo.LevelGen.SeedSweep.Data;

namespace LostInLimbo.LevelGen.SeedSweep.Export;

/// <summary>
/// Turns sweep records into the Markdown report and the CSV table. Pure: returns strings only.
/// </summary>
internal static class SweepReport
{
  /// <summary>How many example runs a report line lists.</summary>
  private const int ExampleCount = 4;
  /// <summary>Error kinds are cut to this length before grouping.</summary>
  private const int MaxErrorKindLength = 110;
  /// <summary>Runs whose generation time exceeds this (ms) are listed as slow.</summary>
  private const double SlowRunMs = 50;
  /// <summary>How many of the slowest runs the timing section lists.</summary>
  private const int SlowestRunCount = 5;
  /// <summary>Percentile reported besides the median.</summary>
  private const double HighPercentile = 0.99;

  /// <summary>Upper bounds (inclusive) of the Size bands of the connectivity table.</summary>
  private static readonly int[] SizeBandMax = [2, 5, 10, int.MaxValue];

  private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

  /// <summary>
  /// Builds the Markdown report.
  /// </summary>
  /// <param name="result">Measured records (sorted by index) and warm-up records.</param>
  /// <param name="elapsed">Wall time of the whole sweep.</param>
  /// <param name="commandLine">Command line, printed in the header.</param>
  /// <returns>Report as Markdown.</returns>
  public static string BuildReport(SweepResult result, TimeSpan elapsed, string commandLine)
  {
    if (result is null) throw new ArgumentNullException(nameof(result));

    var records = result.Records;
    var text = new StringBuilder();
    var ok = records.Where(r => r.Generated).ToList();
    AppendHeader(text, records, elapsed, commandLine);
    AppendErrors(text, records);
    AppendWarmupErrors(text, result.WarmupRecords);
    AppendDifficultyTable(text, ok);
    AppendSuspicious(text, ok);
    AppendBuilders(text, ok);
    AppendConnectivityBands(text, ok);
    AppendTiming(text, ok, result.WarmupRecords);
    return text.ToString();
  }

  /// <summary>
  /// Builds the CSV table: one row per run, one column per settable <see cref="RunRecord"/>
  /// property, then a quoted Issues column joined with " | ".
  /// </summary>
  /// <param name="records">Records sorted by index.</param>
  /// <returns>CSV text with a header row.</returns>
  public static string BuildCsv(IReadOnlyList<RunRecord> records)
  {
    if (records is null) throw new ArgumentNullException(nameof(records));

    var columns = typeof(RunRecord)
      .GetProperties(BindingFlags.Public | BindingFlags.Instance)
      .Where(p => p.CanWrite)
      .OrderBy(p => p.MetadataToken)
      .ToList();

    var text = new StringBuilder();
    text.AppendLine(string.Join(",", columns.Select(p => p.Name)) + ",Issues");
    foreach (var record in records)
    {
      text.Append(string.Join(",", columns.Select(p => Convert.ToString(p.GetValue(record), Invariant))));
      text.Append(",\"").Append(string.Join(" | ", record.Issues).Replace("\"", "'")).AppendLine("\"");
    }

    return text.ToString();
  }

  /// <summary>
  /// Builds the Markdown options file: a full command that reproduces the result (every option
  /// explicit, defaults included, except <c>--out</c>: where results go does not affect them),
  /// the command line as typed, and the effective option values.
  /// </summary>
  /// <param name="options">Effective options; pass a resolved <see cref="SweepOptions.OutputRoot"/> to have it listed in the table.</param>
  /// <param name="commandLine">Command line as typed.</param>
  /// <param name="assemblyLocation">Path of the library assembly that generated the levels.</param>
  /// <returns>Options as Markdown.</returns>
  public static string BuildOptions(SweepOptions options, string commandLine, string assemblyLocation)
  {
    if (options is null) throw new ArgumentNullException(nameof(options));

    var text = new StringBuilder();
    text.AppendLine("# Seed sweep options").AppendLine();
    text.AppendLine("Reproduce (from the repository root):").AppendLine();
    text.AppendLine("```bash");
    var reproduce = SweepOptionsParser.ToArguments(options with { OutputRoot = null });
    text.AppendLine($"{SweepOptionsParser.RunCommand} {SweepOptionsParser.JoinArguments(reproduce)}");
    text.AppendLine("```").AppendLine();
    text.AppendLine($"- Command line as typed: `{(commandLine.Length == 0 ? "(defaults)" : commandLine)}`");
    text.AppendLine($"- LevelGen assembly: `{assemblyLocation}`").AppendLine();
    TableHeader(text, "Option", "Value");
    TableRow(text, "Command", options.Command.ToString());
    TableRow(text, "Runs", options.Runs.ToString(Invariant));
    TableRow(text, "Difficulty", $"{options.DifficultyMin}-{options.DifficultyMax}");
    TableRow(text, "Many themes", options.ManyThemes.ToString());
    TableRow(text, "Seed prefix", $"`{options.SeedPrefix}`");
    TableRow(text, "Start index", options.StartIndex.ToString(Invariant));
    TableRow(text, "Determinism every", options.DeterminismEvery.ToString(Invariant));
    TableRow(text, "Parallelism", options.EffectiveParallelism.ToString(Invariant));
    TableRow(text, "Timeout (s)", options.TimeoutSeconds.ToString(Invariant));
    TableRow(text, "Warm-up runs", options.WarmupRuns.ToString(Invariant));
    TableRow(text, "Output root", options.OutputRoot ?? "(default)");
    if (options.Command == EnSweepCommand.Dump)
    {
      TableRow(text, "Dump indices", options.DumpIndices.Count == 0 ? "-" : string.Join(" ", options.DumpIndices));
      TableRow(text, "Dump text seed", options.DumpTextSeed is null ? "-" : $"`{options.DumpTextSeed}`");
    }

    return text.ToString();
  }

  /// <summary>Groups an error message by kind: digits become '#', long messages are cut.</summary>
  /// <param name="issue">Error message.</param>
  /// <returns>Error kind.</returns>
  public static string ErrorKind(string issue)
  {
    var kind = new StringBuilder(issue.Length);
    foreach (var ch in issue) kind.Append(char.IsDigit(ch) ? '#' : ch);
    return kind.Length > MaxErrorKindLength ? kind.ToString(0, MaxErrorKindLength) : kind.ToString();
  }

  #region Sections

  /// <summary>Title, command line, run count, status and builder counts.</summary>
  private static void AppendHeader(StringBuilder text, IReadOnlyList<RunRecord> records, TimeSpan elapsed, string commandLine)
  {
    text.AppendLine("# Seed sweep report").AppendLine();
    text.AppendLine($"- Command line: `{(commandLine.Length == 0 ? "(defaults)" : commandLine)}`");
    text.AppendLine(Format("- Runs: {0}, wall time {1:F1} s", records.Count, elapsed.TotalSeconds));
    text.AppendLine($"- Status: {Counts(records, r => r.Status)}");
    text.AppendLine($"- Builders: {Counts(records, r => r.Builder)}");
  }

  /// <summary>Errors grouped by kind, most frequent first.</summary>
  private static void AppendErrors(StringBuilder text, IReadOnlyList<RunRecord> records)
  {
    Heading(text, "Errors (by kind)");
    var errors = records.SelectMany(r => r.Issues.Select(i => (Run: r, Issue: i))).ToList();
    if (errors.Count == 0)
    {
      text.AppendLine("None.");
      return;
    }

    var runs = records.Count(r => r.Issues.Count > 0);
    text.AppendLine($"{runs} run(s) with errors, {errors.Count} error(s).").AppendLine();
    TableHeader(text, "Count", "Kind", "Examples");
    foreach (var group in errors.GroupBy(x => ErrorKind(x.Issue)).OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.Ordinal))
    {
      var examples = group.Take(ExampleCount).Select(x => $"{x.Run.Label} {x.Run.Seed}");
      TableRow(text, group.Count().ToString(Invariant), $"`{group.Key}`", string.Join("; ", examples));
    }
  }

  /// <summary>Warm-up runs with errors: not counted with the sweep, but never silently dropped.</summary>
  private static void AppendWarmupErrors(StringBuilder text, IReadOnlyList<RunRecord> warmup)
  {
    var failed = warmup.Where(r => r.Issues.Count > 0).ToList();
    if (failed.Count == 0) return;

    text.AppendLine().AppendLine($"Warm-up runs with errors (not counted above): {failed.Count}.").AppendLine();
    foreach (var record in failed.Take(ExampleCount))
      text.AppendLine($"- {record.Label} `{record.Seed}`: {string.Join("; ", record.Issues)}");
  }

  /// <summary>Means per difficulty.</summary>
  private static void AppendDifficultyTable(StringBuilder text, List<RunRecord> ok)
  {
    Heading(text, "Per difficulty (means)");
    TableHeader(text,
      "D", "n", "Size", "Conn", "ExtraE", "Cx", "Sol", "zeroSol", "Sec", "SecN", "Goal/req", "Opt/req",
      "Shared", "GoalOnSE", "SolInStart", "Useless", "Stacked", "ExitOpen", "OpenReach", "gen ms");
    foreach (var group in ok.GroupBy(r => r.Difficulty).OrderBy(g => g.Key))
    {
      var runs = group.ToList();
      string Mean(Func<RunRecord, double> value, string format = "F1") => runs.Average(value).ToString(format, Invariant);
      TableRow(text,
        group.Key.ToString(Invariant), runs.Count.ToString(Invariant),
        Mean(r => r.Size), Mean(r => r.Connectivity), Mean(r => r.ExtraEdges),
        Mean(r => r.Complexity), Mean(r => r.Solutions), runs.Count(r => r.Complexity > 0 && r.Solutions == 0).ToString(Invariant),
        Mean(r => r.SecretAmount), Mean(r => r.SecretNodes),
        $"{Mean(r => r.Goals, "F2")}/{Mean(r => r.GoalAmount, "F2")}", $"{Mean(r => r.OptGoals, "F2")}/{Mean(r => r.OptGoalAmount, "F2")}",
        Mean(r => r.SharedStartExit ? 1 : 0, "P0"), Mean(r => r.GoalsOnStartExit, "F2"),
        Mean(r => r.SolutionsInStart, "F2"), Mean(r => r.UselessObstacles, "F2"), Mean(r => r.StackedEdges, "F2"),
        Mean(r => r.ExitOpen ? 1 : 0, "P0"), Mean(r => r.OpenReachShare, "P0"), Mean(r => r.GenerationMs, "F2"));
    }
  }

  /// <summary>Counts of runs matching suspicious patterns, with examples.</summary>
  private static void AppendSuspicious(StringBuilder text, List<RunRecord> ok)
  {
    Heading(text, "Shortfalls / suspicious (count of runs)");
    TableHeader(text, "Runs", "Check", "Examples");
    void Line(string name, Func<RunRecord, bool> predicate)
    {
      var hits = ok.Where(predicate).ToList();
      TableRow(text, hits.Count.ToString(Invariant), name, string.Join("; ", hits.Take(ExampleCount).Select(r => r.Label)));
    }

    Line("Complexity>0 but no obstacle", r => r.Complexity > 0 && r.Solutions == 0);
    Line("obstacles < Complexity", r => r.Solutions < r.Complexity);
    Line("… missing >= 2", r => r.Complexity - r.Solutions >= 2);
    Line("only secret passages are locked (d>=1, Cx>=1)", IsSecretOnly);
    Line("every obstacle is key-at-start-door", IsAllKeyAtStartDoor);
    Line("goals < GoalAmount", r => r.Goals < r.GoalAmount);
    Line("… while enough ordinary nodes", r => r.Goals < r.GoalAmount && r.Size >= r.GoalAmount);
    Line("opt goals < OptGoalAmount", r => r.OptGoals < r.OptGoalAmount);
    Line("… while free ordinary nodes remained", r => r.OptGoals < r.OptGoalAmount && r.FreeNodes > 0);
    Line("secret nodes < Amount", r => r.SecretNodes < r.SecretAmount);
    Line("secret nodes > Amount", r => r.SecretNodes > r.SecretAmount);
    Line("extra edges < Connectivity", r => r.ExtraEdges < r.Connectivity);
    Line("… missing >= 3", r => r.Connectivity - r.ExtraEdges >= 3);
    Line("extra edges > Connectivity", r => r.ExtraEdges > r.Connectivity);
    Line("distinct groups < GroupCount", r => r.DistinctGroups < r.GroupCount);
    Line("nodes without group", r => r.NullGroupNodes > 0);
    Line("exit reachable with no solution (Cx>0, start!=exit)", r => r.Complexity > 0 && !r.SharedStartExit && r.ExitOpen);
    Line("… exit open even without secret passages", r => r.Complexity > 0 && !r.SharedStartExit && r.ExitOpenNoSecret);
    Line("… exit open only via secret passage", r => r.Complexity > 0 && !r.SharedStartExit && r.ExitOpen && !r.ExitOpenNoSecret);
    Line("conditioned one-way NON-secret passage", r => r.ConditionedOneWayNonSecret > 0);
    Line("useless obstacle (bypassed lock)", r => r.UselessObstacles > 0);
    Line("all obstacles useless", r => r.Solutions > 0 && r.UselessObstacles == r.Solutions);
    Line("solution in start node", r => r.SolutionsInStart > 0);
    Line("goal on start/exit node", r => r.GoalsOnStartExit > 0);
    Line("goal node holds a solution", r => r.GoalsWithSolution > 0);
    Line("shared start/exit", r => r.SharedStartExit);
    Line("conditioned one-way passage", r => r.ConditionedOneWay > 0);
    Line("conditioned secret passage", r => r.ConditionedSecret > 0);
    Line("stacked conditions (>1 on a passage)", r => r.StackedEdges > 0);
    Line("3+ conditions on a passage", r => r.MaxConditionsPerEdge >= 3);
    Line("one RelSize only (Size>=4)", r => r.Size >= 4 && r.RelSizeMin == r.RelSizeMax);
    Line($"slow run > {SlowRunMs.ToString(Invariant)} ms", r => r.GenerationMs > SlowRunMs);

    AppendKeyAtStartDoorByDifficulty(text, ok);
  }

  /// <summary>Per difficulty: runs where every obstacle is key-at-start-door, count and share.</summary>
  private static void AppendKeyAtStartDoorByDifficulty(StringBuilder text, List<RunRecord> ok)
  {
    text.AppendLine().AppendLine("### Every obstacle key-at-start-door, by difficulty").AppendLine();
    TableHeader(text, "D", "Runs", "Hits", "Share");
    foreach (var group in ok.GroupBy(r => r.Difficulty).OrderBy(g => g.Key))
    {
      var runs = group.Count();
      var hits = group.Count(IsAllKeyAtStartDoor);
      TableRow(text,
        group.Key.ToString(Invariant), runs.ToString(Invariant), hits.ToString(Invariant),
        ((double)hits / runs).ToString("P0", Invariant));
    }
  }

  /// <summary>Per-builder ratios.</summary>
  private static void AppendBuilders(StringBuilder text, List<RunRecord> ok)
  {
    Heading(text, "By builder");
    var first = true;
    foreach (var group in ok.GroupBy(r => r.Builder).OrderBy(g => g.Key, StringComparer.Ordinal))
    {
      var runs = group.ToList();
      var obstacles = Math.Max(1, runs.Sum(r => r.Solutions));
      if (!first) text.AppendLine();
      first = false;
      text.AppendLine($"### {group.Key} (n={runs.Count})").AppendLine();
      text.AppendLine(Format("- obstacles / Complexity: {0:P1}", (double)runs.Sum(r => r.Solutions) / Math.Max(1, runs.Sum(r => r.Complexity))));
      text.AppendLine(Format("- extra edges / Connectivity: {0:P1}", (double)runs.Sum(r => r.ExtraEdges) / Math.Max(1, runs.Sum(r => r.Connectivity))));
      text.AppendLine(Format("- shared start/exit: {0:P1}", runs.Average(r => r.SharedStartExit ? 1.0 : 0.0)));
      text.AppendLine(Format("- useless / obstacle: {0:P1}", (double)runs.Sum(r => r.UselessObstacles) / obstacles));
      text.AppendLine(Format("- solution in start / obstacle: {0:P1}", (double)runs.Sum(r => r.SolutionsInStart) / obstacles));
      text.AppendLine(Format("- stacked / conditioned passage: {0:P1}", (double)runs.Sum(r => r.StackedEdges) / Math.Max(1, runs.Sum(r => r.ConditionedEdges))));
      text.AppendLine(Format("- key next to its door: {0:P1} of obstacles; in start node next to its door: {1:P1}",
        (double)runs.Sum(r => r.KeyNextToDoor) / obstacles, (double)runs.Sum(r => r.KeyAtStartDoor) / obstacles));
      text.AppendLine($"- runs where every obstacle is key-at-start-door: {runs.Count(IsAllKeyAtStartDoor)}");
      text.AppendLine($"- secret-only runs: {runs.Count(IsSecretOnly)}");
    }
  }

  /// <summary>Delivered extra passages vs. Connectivity by builder and Size band.</summary>
  private static void AppendConnectivityBands(StringBuilder text, List<RunRecord> ok)
  {
    Heading(text, "Connectivity fulfilment");
    text.AppendLine("Extra edges / Connectivity, runs with Connectivity > 0; run count in parentheses.").AppendLine();

    var bandNames = new List<string>();
    var low = 0;
    foreach (var high in SizeBandMax)
    {
      bandNames.Add(high == int.MaxValue ? $"S{low + 1}+" : low + 1 == high ? $"S{high}" : $"S{low + 1}-{high}");
      low = high;
    }

    TableHeader(text, ["Builder", .. bandNames]);
    foreach (var group in ok.Where(r => r.Connectivity > 0).GroupBy(r => r.Builder).OrderBy(g => g.Key, StringComparer.Ordinal))
    {
      var cells = new List<string> { group.Key };
      low = 0;
      foreach (var high in SizeBandMax)
      {
        var band = group.Where(r => r.Size > low && r.Size <= high).ToList();
        cells.Add(band.Count == 0
          ? "-"
          : Format("{0:P0} ({1})", (double)band.Sum(r => Math.Min(r.ExtraEdges, r.Connectivity)) / band.Sum(r => r.Connectivity), band.Count));
        low = high;
      }

      TableRow(text, [.. cells]);
    }
  }

  /// <summary>Distribution of generation and wall time, and the slowest runs.</summary>
  private static void AppendTiming(StringBuilder text, List<RunRecord> ok, IReadOnlyList<RunRecord> warmup)
  {
    Heading(text, "Timing");
    if (ok.Count == 0)
    {
      text.AppendLine("No generated runs.");
      return;
    }

    var warmupNote = warmup.Count == 0
      ? "Measured without warm-up (first runs include JIT compilation)."
      : $"Measured after {warmup.Count} untimed warm-up runs (builders: {Counts(warmup, r => r.Builder)}); tiered JIT is off in the harness.";
    text.AppendLine(warmupNote)
      .AppendLine("Generation time is measured by the library (`GenerationStatus.Elapsed`). Wall time includes the harness'")
      .AppendLine("status polling and rounds up to the OS timer tick. Parallel runs share the CPU; use `--parallel 1` for clean numbers.")
      .AppendLine();
    TableHeader(text, "Time (ms)", "Mean", "Median", "p99", "Max");
    TimingRow(text, "Generation", ok.Select(r => r.GenerationMs));
    TimingRow(text, "Wall", ok.Select(r => r.WallMs));

    text.AppendLine().AppendLine("Slowest runs (generation time):").AppendLine();
    TableHeader(text, "Run", "Seed", "Generation (ms)", "Wall (ms)");
    foreach (var record in ok.OrderByDescending(r => r.GenerationMs).ThenBy(r => r.Index).Take(SlowestRunCount))
      TableRow(text, record.Label, $"`{record.Seed}`", record.GenerationMs.ToString("F2", Invariant), record.WallMs.ToString("F1", Invariant));
  }

  /// <summary>One row of the timing table.</summary>
  private static void TimingRow(StringBuilder text, string name, IEnumerable<double> values)
  {
    var sorted = values.OrderBy(v => v).ToList();
    string Ms(double value) => value.ToString("F2", Invariant);
    TableRow(text, name, Ms(sorted.Average()), Ms(sorted[sorted.Count / 2]), Ms(sorted[(int)(sorted.Count * HighPercentile)]), Ms(sorted[^1]));
  }

  #endregion

  #region Predicates

  /// <summary>
  /// A run that should have obstacles, but every locked passage is secret, so the ordinary
  /// level is fully open.
  /// </summary>
  internal static bool IsSecretOnly(RunRecord r) =>
    r.Difficulty >= 1 && r.Complexity >= 1 && r.Solutions > 0 && r.ConditionedEdges == r.ConditionedSecret;

  /// <summary>A run whose every obstacle has its key in the start node, next to its door.</summary>
  internal static bool IsAllKeyAtStartDoor(RunRecord r) => r.Solutions > 0 && r.KeyAtStartDoor == r.Solutions;

  #endregion

  #region Markdown

  /// <summary>Second-level heading preceded by a blank line.</summary>
  private static void Heading(StringBuilder text, string title) => text.AppendLine().AppendLine($"## {title}").AppendLine();

  /// <summary>Table header row and separator.</summary>
  private static void TableHeader(StringBuilder text, params string[] columns)
  {
    TableRow(text, columns);
    TableRow(text, columns.Select(_ => "---").ToArray());
  }

  /// <summary>Table row; pipes inside cells are escaped.</summary>
  private static void TableRow(StringBuilder text, params string[] cells) =>
    text.Append("| ").Append(string.Join(" | ", cells.Select(c => c.Replace("|", "\\|")))).AppendLine(" |");

  #endregion

  /// <summary>"key=count" list, sorted by key.</summary>
  private static string Counts(IEnumerable<RunRecord> records, Func<RunRecord, string> key) =>
    string.Join(", ", records.GroupBy(key).OrderBy(g => g.Key, StringComparer.Ordinal).Select(g => $"{g.Key}={g.Count()}"));

  /// <summary>Invariant-culture format.</summary>
  private static string Format(string format, params object[] values) => string.Format(Invariant, format, values);
}
