using LostInLimbo.LevelGen.SeedSweep.Data;
using LostInLimbo.LevelGen.SeedSweep.Runner;

namespace LostInLimbo.LevelGen.Tests.SeedSweep.Runner;

/// <summary>Tests <see cref="SweepRunner"/> end to end on a tiny sweep.</summary>
public sealed class SweepRunnerTests
{
  /// <summary>Tiny sweep: three runs at difficulty 5, every run checked for determinism.</summary>
  private static readonly SweepOptions TinySweep = new()
  {
    Runs = 3,
    DifficultyMin = 5,
    DifficultyMax = 5,
    DeterminismEvery = 1,
    Parallelism = 1,
  };

  [Fact]
  public void RunAll_TinySweep_GeneratesCleanDeterministicRuns()
  {
    // Act: Run the sweep.
    var records = SweepRunner.RunAll(TinySweep);

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
    var records = SweepRunner.RunAll(TinySweep with { DeterminismEvery = 0 });

    // Assert: Library-measured time is positive and never exceeds the harness' wall time.
    Assert.All(records, r => Assert.True(r.GenerationMs > 0, $"#{r.Index} GenerationMs {r.GenerationMs}"));
    Assert.All(records, r => Assert.True(r.GenerationMs <= r.WallMs, $"#{r.Index} GenerationMs {r.GenerationMs} > WallMs {r.WallMs}"));
  }

  [Fact]
  public void RunAll_WhenCancelled_ThrowsOperationCanceledException()
  {
    // Arrange: Already cancelled token.
    using var cancellation = new CancellationTokenSource();
    cancellation.Cancel();

    // Act: Run the sweep.
    var act = () => SweepRunner.RunAll(TinySweep, cancellationToken: cancellation.Token);

    // Assert: Cancellation respected.
    Assert.ThrowsAny<OperationCanceledException>(act);
  }
}
