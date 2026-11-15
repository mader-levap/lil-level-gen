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
    Assert.Contains("| Run | Seed | Generation (ms) | Wall (ms) | Comment |", report);
    Assert.Contains(",ThreadRunOrdinal,JitMethods,GcGeneration,", lines[0]);
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
    Assert.Contains("| Warm-up runs | 420 |", text);
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

  #region Slow-run comments

  /// <summary>Clean run: not first on its thread, no JIT, no GC.</summary>
  private static RunRecord Run(int index, double generationMs, string builder = "Simple", int size = 10) => new()
  {
    Index = index,
    Builder = builder,
    Size = size,
    Status = "Paused",
    GenerationMs = generationMs,
    ThreadRunOrdinal = 2,
    JitMethods = 0,
    GcGeneration = -1,
  };

  /// <summary>Peer group of <paramref name="count"/> runs, each 0.2 ms, plus <paramref name="extra"/>.</summary>
  private static PeerMedians Peers(int count, params RunRecord[] extra) =>
    PeerMedians.From(Enumerable.Range(100, count).Select(i => Run(i, 0.2)).Concat(extra));

  [Fact]
  public void SlowRunComment_FirstRunOnThread_ReportsWorkerThreadArtifact()
  {
    // Arrange: First run on its thread, no peers.
    var run = Run(1, 3.0);
    run.ThreadRunOrdinal = 1;

    // Act: Comment.
    var comment = SweepReport.SlowRunComment(run, Peers(0));

    // Assert: Thread reason.
    Assert.Equal("Likely artifact of worker thread's first run.", comment);
  }

  [Fact]
  public void SlowRunComment_JitDuringRun_ReportsMethodCount()
  {
    // Arrange: JIT activity, no peers.
    var run = Run(1, 3.0);
    run.JitMethods = 12;

    // Act: Comment.
    var comment = SweepReport.SlowRunComment(run, Peers(0));

    // Assert: JIT reason with count.
    Assert.Equal("JIT compiled 12 method(s) during the run.", comment);
  }

  [Fact]
  public void SlowRunComment_GcDuringRun_ReportsGeneration()
  {
    // Arrange: Gen 2 collection, no peers.
    var run = Run(1, 3.0);
    run.GcGeneration = 2;

    // Act: Comment.
    var comment = SweepReport.SlowRunComment(run, Peers(0));

    // Assert: GC reason.
    Assert.Equal("Garbage collection (gen 2) during the run.", comment);
  }

  [Fact]
  public void SlowRunComment_SeveralReasons_JoinsInFixedOrder()
  {
    // Arrange: All three artifacts, no peers.
    var run = Run(1, 3.0);
    run.ThreadRunOrdinal = 1;
    run.JitMethods = 3;
    run.GcGeneration = 0;

    // Act: Comment.
    var comment = SweepReport.SlowRunComment(run, Peers(0));

    // Assert: Thread, JIT, GC.
    Assert.Equal(
      "Likely artifact of worker thread's first run. JIT compiled 3 method(s) during the run. Garbage collection (gen 0) during the run.",
      comment);
  }

  [Fact]
  public void SlowRunComment_WithinFactorOfPeers_ReportsHeavyLevel()
  {
    // Arrange: 0.5 ms against a 0.2 ms median of 10 peers.
    var run = Run(1, 0.5);

    // Act: Comment.
    var comment = SweepReport.SlowRunComment(run, Peers(10, run));

    // Assert: Explained by level size.
    Assert.Equal("Consistent with level size (2.5x median of Simple S10).", comment);
  }

  [Fact]
  public void SlowRunComment_FarAboveUnexplained_ReportsOutlierWithDump()
  {
    // Arrange: 2 ms against a 0.2 ms median, no artifact.
    var run = Run(1250, 2.0);

    // Act: Comment.
    var comment = SweepReport.SlowRunComment(run, Peers(10, run));

    // Assert: Outlier hint with the dump command.
    Assert.Equal("Outlier: 10x median of Simple S10; re-check with `dump 1250`.", comment);
  }

  [Fact]
  public void SlowRunComment_FarAboveWithArtifact_ReportsArtifactOnly()
  {
    // Arrange: Outlier that also had JIT activity.
    var run = Run(1, 2.0);
    run.JitMethods = 4;

    // Act: Comment.
    var comment = SweepReport.SlowRunComment(run, Peers(10, run));

    // Assert: The artifact explains it; no outlier hint.
    Assert.Equal("JIT compiled 4 method(s) during the run.", comment);
  }

  [Fact]
  public void SlowRunComment_SmallPeerGroupAndNoArtifact_ReturnsDash()
  {
    // Arrange: Only 2 peers besides the run itself.
    var run = Run(1, 2.0);

    // Act: Comment.
    var comment = SweepReport.SlowRunComment(run, Peers(2, run));

    // Assert: Nothing to say.
    Assert.Equal("-", comment);
  }

  [Fact]
  public void SlowRunComment_CleanRunWithoutPeers_ReturnsDash()
  {
    // Act: Comment on a clean run with no peer data.
    var comment = SweepReport.SlowRunComment(Run(1, 2.0), Peers(0));

    // Assert: Nothing to say.
    Assert.Equal("-", comment);
  }

  #endregion

  [Fact]
  public void ErrorKind_ReplacesDigits()
  {
    // Act: Group a numbered message.
    var kind = SweepReport.ErrorKind("node degree 4 > MaxEdges 3");

    // Assert: Digits masked.
    Assert.Equal("node degree # > MaxEdges #", kind);
  }
}
