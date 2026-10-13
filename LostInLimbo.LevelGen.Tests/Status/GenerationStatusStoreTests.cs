using LostInLimbo.LevelGen.Data.Other;
using LostInLimbo.LevelGen.Internal.Status;

namespace LostInLimbo.LevelGen.Tests.Status;

public sealed class GenerationStatusStoreTests
{
  [Fact]
  public void Current_Initially_ReturnsNoneResult()
  {
    // Arrange: Fresh store.
    var store = new GenerationStatusStore();

    // Assert: Store starts with result None (never ran).
    Assert.Equal(EnGenResult.None, store.Current.Result);
    Assert.Null(store.Current.Error);
  }

  [Fact]
  public void Publish_ReplacesWholeSnapshot()
  {
    // Arrange: Fresh store.
    var store = new GenerationStatusStore();
    var published = new GenerationStatus { Result = EnGenResult.Finished };

    // Act: Publish a new snapshot.
    store.Publish(published);

    // Assert: Reading returns exactly the published snapshot.
    Assert.Same(published, store.Current);
  }

  [Fact]
  public void TryBeginRun_WhenIdle_ReturnsTrueAndMarksRunning()
  {
    // Arrange: Fresh store.
    var store = new GenerationStatusStore();

    // Act: Claim the run.
    var claimed = store.TryBeginRun();

    // Assert: Claim succeeded and store now reports running.
    Assert.True(claimed);
    Assert.Equal(EnGenResult.Running, store.Current.Result);
  }

  [Fact]
  public void TryBeginRun_WhileRunning_ReturnsFalse()
  {
    // Arrange: Store already claims a run.
    var store = new GenerationStatusStore();
    Assert.True(store.TryBeginRun());

    // Act: Attempting a second claim.
    var claimed = store.TryBeginRun();

    // Assert: Second claim is refused.
    Assert.False(claimed);
    Assert.Equal(EnGenResult.Running, store.Current.Result);
  }

  [Fact]
  public void TryBeginRun_AfterFinished_SucceedsAgain()
  {
    // Arrange: Store finished a run successfully.
    var store = new GenerationStatusStore();
    Assert.True(store.TryBeginRun());
    store.Publish(new GenerationStatus { Result = EnGenResult.Finished });

    // Act: Claim a new run.
    var claimed = store.TryBeginRun();

    // Assert: New claim succeeds.
    Assert.True(claimed);
    Assert.Equal(EnGenResult.Running, store.Current.Result);
  }

  [Fact]
  public void TryBeginRun_AfterError_SucceedsAgain()
  {
    // Arrange: Store finished a failed run.
    var store = new GenerationStatusStore();
    Assert.True(store.TryBeginRun());
    store.Publish(new GenerationStatus { Result = EnGenResult.Error, Error = new InvalidOperationException() });

    // Act: Claim a new run.
    var claimed = store.TryBeginRun();

    // Assert: New claim succeeds and clears the previous error.
    Assert.True(claimed);
    Assert.Equal(EnGenResult.Running, store.Current.Result);
    Assert.Null(store.Current.Error);
  }

  [Fact]
  public void TryBeginResume_WhenPaused_ReturnsTrueAndMarksRunning()
  {
    // Arrange: Store has a paused run.
    var store = new GenerationStatusStore();
    store.Publish(new GenerationStatus { Result = EnGenResult.Paused });

    // Act: Claim the resume.
    var claimed = store.TryBeginResume();

    // Assert: Claim succeeded and store now reports running.
    Assert.True(claimed);
    Assert.Equal(EnGenResult.Running, store.Current.Result);
  }

  [Theory]
  [InlineData(EnGenResult.None)]
  [InlineData(EnGenResult.Running)]
  [InlineData(EnGenResult.Finished)]
  [InlineData(EnGenResult.Error)]
  public void TryBeginResume_WhenNotPaused_ReturnsFalse(EnGenResult result)
  {
    // Arrange: Store is in the given non-paused state (with a real error object when needed,
    // respecting the documented GenerationStatus invariant).
    var store = new GenerationStatusStore();
    store.Publish(new GenerationStatus
    {
      Result = result,
      Error = result == EnGenResult.Error ? new InvalidOperationException() : null,
    });

    // Act: Attempting to claim a resume.
    var claimed = store.TryBeginResume();

    // Assert: Claim is refused and the status is left untouched.
    Assert.False(claimed);
    Assert.Equal(result, store.Current.Result);
  }

  [Fact]
  public async Task TryBeginRun_WhenThreadsRace_ExactlyOneWins()
  {
    // Arrange: Store ready to be claimed and tasks synchronized on a barrier.
    var store = new GenerationStatusStore();
    const int taskCount = 4;
    using var barrier = new Barrier(taskCount);
    var wins = 0;
    var tasks = Enumerable.Range(0, taskCount)
      .Select(_ => Task.Run(() =>
      {
        barrier.SignalAndWait();
        if (store.TryBeginRun()) Interlocked.Increment(ref wins);
      }))
      .ToArray();

    // Act: Wait until every racer has attempted the claim.
    await Task.WhenAll(tasks);

    // Assert: The CAS guarantees exactly one winner, regardless of timing.
    Assert.Equal(1, Volatile.Read(ref wins));
    Assert.Equal(EnGenResult.Running, store.Current.Result);
  }

  [Fact]
  public async Task TryBeginResume_WhenThreadsRace_ExactlyOneWins()
  {
    // Arrange: Store has a paused run and tasks synchronized on a barrier.
    var store = new GenerationStatusStore();
    store.Publish(new GenerationStatus { Result = EnGenResult.Paused });
    const int taskCount = 4;
    using var barrier = new Barrier(taskCount);
    var wins = 0;
    var tasks = Enumerable.Range(0, taskCount)
      .Select(_ => Task.Run(() =>
      {
        barrier.SignalAndWait();
        if (store.TryBeginResume()) Interlocked.Increment(ref wins);
      }))
      .ToArray();

    // Act: Wait until every racer has attempted the claim.
    await Task.WhenAll(tasks);

    // Assert: The CAS guarantees exactly one winner, regardless of timing.
    Assert.Equal(1, Volatile.Read(ref wins));
    Assert.Equal(EnGenResult.Running, store.Current.Result);
  }
}
