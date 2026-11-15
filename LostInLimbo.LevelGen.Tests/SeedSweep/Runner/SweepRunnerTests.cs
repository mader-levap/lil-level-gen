using LostInLimbo.LevelGen.SeedSweep.Data;
using LostInLimbo.LevelGen.SeedSweep.Runner;

namespace LostInLimbo.LevelGen.Tests.SeedSweep.Runner;

/// <summary>Tests <see cref="SweepRunner"/> end to end on a tiny sweep.</summary>
public sealed class SweepRunnerTests
{
  /// <summary>Tiny sweep: three runs at difficulty 5, every run checked for determinism, no warm-up.</summary>
  private static readonly SweepOptions TinySweep = new()
  {
    Runs = 3,
    DifficultyMin = 5,
    DifficultyMax = 5,
    DeterminismEvery = 1,
    Parallelism = 1,
    WarmupRuns = 0,
  };

  /// <summary>Per-run timeout used by direct warm-up calls.</summary>
  private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(SweepOptions.DefaultTimeoutSeconds);

  [Fact]
  public void RunAll_TinySweep_GeneratesCleanDeterministicRuns()
  {
    // Act: Run the sweep.
    var records = SweepRunner.RunAll(TinySweep).Records;

    // Assert: Three runs in index order, all generated, no errors (incl. determinism reruns).
    Assert.Equal([0, 1, 2], records.Select(r => r.Index));
    Assert.All(records, r => Assert.Equal("Paused", r.Status));
    Assert.All(records, r => Assert.Empty(r.Issues));
    Assert.All(records, r => Assert.Equal(5, r.Difficulty));
  }

  [Fact]
  public void RunAll_TinySweep_RecordsGenerationTimeWithinWallTime()
  {
    // Act: Run the sweep.
    var records = SweepRunner.RunAll(TinySweep with { DeterminismEvery = 0 }).Records;

    // Assert: Library-measured time is positive and never exceeds the harness' wall time.
    Assert.All(records, r => Assert.True(r.GenerationMs > 0, $"#{r.Index} GenerationMs {r.GenerationMs}"));
    Assert.All(records, r => Assert.True(r.GenerationMs <= r.WallMs, $"#{r.Index} GenerationMs {r.GenerationMs} > WallMs {r.WallMs}"));
  }

  [Fact]
  public void RunAll_SingleThread_RecordsConsecutiveThreadOrdinalsAndCounters()
  {
    // Arrange: One harness thread, so ordinals of the three runs follow each other.
    var options = TinySweep with { DeterminismEvery = 0 };

    // Act: Run the sweep on a dedicated thread, so earlier tests cannot share its counter.
    IReadOnlyList<RunRecord> records = [];
    var thread = new Thread(() => records = SweepRunner.RunAll(options).Records);
    thread.Start();
    thread.Join();

    // Assert: Fresh thread starts at 1 and counts up; counters are in range.
    Assert.Equal([1, 2, 3], records.Select(r => r.ThreadRunOrdinal));
    Assert.All(records, r => Assert.True(r.JitMethods >= 0));
    Assert.All(records, r => Assert.InRange(r.GcGeneration, -1, GC.MaxGeneration));
  }

  [Theory]
  [InlineData(new[] { 5, 2, 1 }, new[] { 5, 2, 1 }, -1)]
  [InlineData(new[] { 5, 2, 1 }, new[] { 6, 2, 1 }, 0)]
  [InlineData(new[] { 5, 2, 1 }, new[] { 7, 3, 2 }, 2)]
  public void HighestCollectedGeneration_ReturnsHighestGrownGeneration(int[] before, int[] after, int expected)
  {
    // Act: Compare samples.
    var generation = SweepRunner.HighestCollectedGeneration(before, after);

    // Assert: Highest generation that grew, or -1.
    Assert.Equal(expected, generation);
  }

  [Fact]
  public void RunAll_WithWarmup_ReturnsOnlySweepRecords()
  {
    // Act: Sweep with a few warm-up runs.
    var result = SweepRunner.RunAll(TinySweep with { DeterminismEvery = 0, WarmupRuns = 2 });

    // Assert: Warm-up kept apart; measured runs keep their indices and sweep seeds.
    Assert.Equal(2, result.WarmupRecords.Count);
    Assert.Equal([0, 1, 2], result.Records.Select(r => r.Index));
    Assert.All(result.Records, r => Assert.StartsWith("sweep-", r.Seed));
  }

  [Fact]
  public void WarmUp_GivenCount_ReturnsThatManyGeneratedRuns()
  {
    // Act: Warm up with three runs.
    var records = SweepRunner.WarmUp(3, 2, Timeout);

    // Assert: Three clean runs with warm-up seeds, cycling difficulty from 0.
    Assert.Equal(3, records.Count);
    Assert.Equal(["warmup-0", "warmup-1", "warmup-2"], records.Select(r => r.Seed));
    Assert.Equal([0, 1, 2], records.Select(r => r.Difficulty));
    Assert.All(records, r => Assert.Empty(r.Issues));
  }

  [Fact]
  public void WarmUp_Zero_DoesNothing()
  {
    // Act: No warm-up.
    var records = SweepRunner.WarmUp(0, 2, Timeout);

    // Assert: Empty.
    Assert.Empty(records);
  }

  [Fact]
  public void WarmUp_NegativeCount_Throws()
  {
    // Act: Invalid count.
    var act = () => SweepRunner.WarmUp(-1, 2, Timeout);

    // Assert: Rejected.
    Assert.Throws<ArgumentOutOfRangeException>(act);
  }

  [Fact]
  public void WarmUp_ZeroParallelism_Throws()
  {
    // Act: Invalid degree of parallelism.
    var act = () => SweepRunner.WarmUp(3, 0, Timeout);

    // Assert: Rejected, naming the parameter.
    var exception = Assert.Throws<ArgumentOutOfRangeException>(act);
    Assert.Equal("parallelism", exception.ParamName);
  }

  [Fact]
  public void RunAll_WhenCancelled_ThrowsOperationCanceledException()
  {
    // Arrange: Already cancelled token; warm-up enabled so it is cancelled as well.
    using var cancellation = new CancellationTokenSource();
    cancellation.Cancel();

    // Act: Run the sweep.
    var act = () => SweepRunner.RunAll(TinySweep with { WarmupRuns = 2 }, cancellationToken: cancellation.Token);

    // Assert: Cancellation respected.
    Assert.ThrowsAny<OperationCanceledException>(act);
  }
}
