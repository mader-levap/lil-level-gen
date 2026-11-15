using LostInLimbo.LevelGen.SeedSweep.Data;
using LostInLimbo.LevelGen.SeedSweep.Export;
using LostInLimbo.LevelGen.SeedSweep.Runner;

namespace LostInLimbo.LevelGen.Tests.SeedSweep.Export;

/// <summary>Tests <see cref="SweepReport"/>: Markdown report, CSV, options file and error grouping.</summary>
public sealed class SweepReportTests
{
  /// <summary>Tiny sweep: three runs at difficulty 5, no determinism reruns, two warm-up runs.</summary>
  private static readonly SweepOptions TinySweep = new()
  {
    Runs = 3,
    DifficultyMin = 5,
    DifficultyMax = 5,
    DeterminismEvery = 0,
    Parallelism = 1,
    WarmupRuns = 2,
  };

  [Fact]
  public void BuildReportAndCsv_TinySweep_ContainSectionsAndRows()
  {
    // Arrange: Tiny sweep results.
    var result = SweepRunner.RunAll(TinySweep);

    // Act: Build both outputs.
    var report = SweepReport.BuildReport(result, TimeSpan.FromSeconds(1), "--runs 3");
    var csv = SweepReport.BuildCsv(result.Records);

    // Assert: Report sections present, CSV has a header plus one row per run.
    Assert.StartsWith("# Seed sweep report", report);
    Assert.Contains("## Errors (by kind)", report);
    Assert.Contains("- Status: Paused=3", report);
    Assert.Contains("| D | n | Size |", report);
    Assert.Contains("| D | Runs | Hits | Share |", report);
    Assert.Matches(@"\| 5 \| 3 \| \d \| \d+ % \|", report);
    var lines = csv.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
    Assert.Equal(4, lines.Length);
    Assert.StartsWith("Index,Difficulty,", lines[0]);
    Assert.EndsWith(",Issues", lines[0]);
    Assert.Contains(",GenerationMs,WallMs,", lines[0]);
    Assert.Contains("## Timing", report);
    Assert.Contains("| Generation |", report);
    Assert.Contains("Slowest runs (generation time):", report);
    Assert.Contains("Measured after 2 untimed warm-up runs", report);
  }

  [Fact]
  public void BuildOptions_DumpOptions_ListsCommandLineAndDumpTargets()
  {
    // Arrange: Dump of two runs.
    var options = new SweepOptions { Command = EnSweepCommand.Dump, DumpIndices = [924, 967] };

    // Act: Build the options file.
    var text = SweepReport.BuildOptions(options, "dump 924 967", "lib.dll");

    // Assert: Markdown with command line and dump indices.
    Assert.StartsWith("# Seed sweep options", text);
    Assert.Contains("dotnet run --project LostInLimbo.LevelGen.SeedSweep -c Release -- dump 924 967 --difficulty 0-20", text);
    Assert.Contains("- Command line as typed: `dump 924 967`", text);
    Assert.Contains("| Dump indices | 924 967 |", text);
    Assert.Contains("| Warm-up runs | 42 |", text);
  }

  [Fact]
  public void BuildOptions_CustomOutputRoot_OmittedFromReproduceCommandOnly()
  {
    // Arrange: Sweep typed with a custom output root.
    var options = new SweepOptions { Runs = 100, OutputRoot = "C:\\results" };

    // Act: Build the options file.
    var text = SweepReport.BuildOptions(options, "--runs 100 --out C:\\results", "lib.dll");

    // Assert: Reproduce command has no --out; typed command line and table still show it.
    var reproduce = text.Split('\n').Single(line => line.StartsWith("dotnet run ", StringComparison.Ordinal));
    Assert.DoesNotContain("--out", reproduce);
    Assert.Contains("- Command line as typed: `--runs 100 --out C:\\results`", text);
    Assert.Contains("| Output root | C:\\results |", text);
  }

  [Fact]
  public void ErrorKind_ReplacesDigits()
  {
    // Act: Group a numbered message.
    var kind = SweepReport.ErrorKind("node degree 4 > MaxEdges 3");

    // Assert: Digits masked.
    Assert.Equal("node degree # > MaxEdges #", kind);
  }
}
