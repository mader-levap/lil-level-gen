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
}
