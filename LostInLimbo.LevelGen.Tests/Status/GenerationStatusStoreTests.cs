using LostInLimbo.LevelGen.Data.Other;
using LostInLimbo.LevelGen.Data.Result;
using LostInLimbo.LevelGen.Internal.Status;

namespace LostInLimbo.LevelGen.Tests.Status;

/// <summary>
/// Tests the generation status store, grouped into suites by API area: publish/read
/// snapshots, run claims, idle resets and the concurrency guarantees.
/// </summary>
/// <remarks>
/// The concurrency suite spawns threads with barriers, so every suite is pinned to the
/// <c>GenerationStatusStoreTests</c> xunit collection: the suites never run in parallel
/// with each other, matching the sequential behavior of the original single-class layout.
/// </remarks>
public sealed class GenerationStatusStoreTests
{
  /// <summary>Tests <see cref="GenerationStatusStore.Current"/>, <see cref="GenerationStatusStore.Publish(GenerationStatus)"/> and world pairing in snapshots.</summary>
  [Collection("GenerationStatusStoreTests")]
  public sealed class PublishTests
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
    public void Publish_WithWorld_PairsWorldWithStatusAsOneSnapshot()
    {
      // Arrange: Fresh store and a world instance to pair with a terminal status.
      var store = new GenerationStatusStore();
      var world = new LilWorld();

      // Act: Publish a paused snapshot that exposes its world.
      store.Publish(new GenerationStatus { Result = EnGenResult.Paused }, world);

      // Assert: Status and world come from one read and describe the same run.
      var snapshot = store.CurrentSnapshot;
      Assert.Equal(EnGenResult.Paused, snapshot.Status.Result);
      Assert.Same(world, snapshot.World);
      Assert.Same(snapshot.Status, store.Current);
    }

    [Fact]
    public void Publish_WithoutWorld_KeepsWorldHidden()
    {
      // Arrange: Fresh store and a status that must not expose a world.
      var store = new GenerationStatusStore();

      // Act: Publish without pairing a world (the single-argument overload).
      store.Publish(new GenerationStatus { Result = EnGenResult.Cancelled });

      // Assert: The snapshot carries no world alongside the status.
      var snapshot = store.CurrentSnapshot;
      Assert.Equal(EnGenResult.Cancelled, snapshot.Status.Result);
      Assert.Null(snapshot.World);
    }
  }

  /// <summary>Tests <see cref="GenerationStatusStore.TryBeginRun"/>, <see cref="GenerationStatusStore.TryBeginResume"/> and claim-id bumping.</summary>
  [Collection("GenerationStatusStoreTests")]
  public sealed class ClaimTests
  {
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
    public void TryBeginRun_AfterTerminalWithWorld_DropsWorldFromSnapshot()
    {
      // Arrange: Store whose terminal snapshot exposes a world.
      var store = new GenerationStatusStore();
      store.Publish(new GenerationStatus { Result = EnGenResult.Finished }, new LilWorld());

      // Act: Claim the next run.
      Assert.True(store.TryBeginRun());

      // Assert: The claim replaces the whole pair, so the new run's Running status never keeps
      // the previous run's world attached.
      var snapshot = store.CurrentSnapshot;
      Assert.Equal(EnGenResult.Running, snapshot.Status.Result);
      Assert.Null(snapshot.World);
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
    public void ClaimId_BumpsOnEachSuccessfulClaim()
    {
      // Arrange: Fresh store.
      var store = new GenerationStatusStore();
      var initialClaimId = store.ClaimId;

      // Act: One refused claim (already running) and two successful claims with a publish between.
      Assert.True(store.TryBeginRun());
      var firstClaimId = store.ClaimId;
      Assert.False(store.TryBeginRun()); // Refused: must not bump the id.
      store.Publish(new GenerationStatus { Result = EnGenResult.Paused });
      Assert.True(store.TryBeginRun());
      var secondClaimId = store.ClaimId;

      // Assert: Id grows strictly per successful claim; refused claims leave it alone.
      Assert.True(firstClaimId > initialClaimId);
      Assert.True(secondClaimId > firstClaimId);
      Assert.Equal(secondClaimId, store.ClaimId);
    }
  }

  /// <summary>Tests <see cref="GenerationStatusStore.TryPublishIdle"/>: resets of non-running states, stale claim ids and world dropping.</summary>
  [Collection("GenerationStatusStoreTests")]
  public sealed class IdleResetTests
  {
    [Theory]
    [InlineData(EnGenResult.None)]
    [InlineData(EnGenResult.Paused)]
    [InlineData(EnGenResult.Finished)]
    [InlineData(EnGenResult.Cancelled)]
    [InlineData(EnGenResult.Error)]
    public void TryPublishIdle_WhenNotRunning_ReturnsTrueAndRestoresDefault(EnGenResult result)
    {
      // Arrange: Store is in the given non-running state (with a real error object when needed,
      // respecting the documented GenerationStatus invariant). No claim has happened yet, so the
      // current claim id identifies the state to clear.
      var store = new GenerationStatusStore();
      store.Publish(new GenerationStatus
      {
        Result = result,
        Error = result == EnGenResult.Error ? new InvalidOperationException() : null,
      });
      var expectedClaimId = store.ClaimId;

      // Act: Attempting to reset to idle.
      var published = store.TryPublishIdle(expectedClaimId);

      // Assert: The reset succeeded and the pristine snapshot replaced the old one.
      Assert.True(published);
      Assert.Same(GenerationStatus.Default, store.Current);
      Assert.Equal(EnGenResult.None, store.Current.Result);
      Assert.Null(store.Current.Error);
    }

    [Fact]
    public void TryPublishIdle_WhileRunning_ReturnsFalse()
    {
      // Arrange: Store already claims a run.
      var store = new GenerationStatusStore();
      Assert.True(store.TryBeginRun());
      var expectedClaimId = store.ClaimId;

      // Act: Attempting to reset to idle under the running claim.
      var published = store.TryPublishIdle(expectedClaimId);

      // Assert: The reset is refused and the running status is left untouched.
      Assert.False(published);
      Assert.Equal(EnGenResult.Running, store.Current.Result);
    }

    [Fact]
    public void TryPublishIdle_WithCurrentClaimIdAfterTerminal_ResetsToDefault()
    {
      // Arrange: A claimed run reached a terminal state and the id was captured for clearing.
      var store = new GenerationStatusStore();
      Assert.True(store.TryBeginRun());
      store.Publish(new GenerationStatus { Result = EnGenResult.Finished });
      var expectedClaimId = store.ClaimId;

      // Act: Resetting with the id of the run that actually produced this state.
      var published = store.TryPublishIdle(expectedClaimId);

      // Assert: The reset succeeds because the observed run still holds the claim.
      Assert.True(published);
      Assert.Equal(EnGenResult.None, store.Current.Result);
    }

    [Fact]
    public void TryPublishIdle_WithStaleClaimId_ReturnsFalse()
    {
      // Arrange: The caller observed run 1, but run 2 claimed afterwards and finished on its own.
      var store = new GenerationStatusStore();
      Assert.True(store.TryBeginRun());
      var staleClaimId = store.ClaimId;
      store.Publish(new GenerationStatus { Result = EnGenResult.Paused });
      Assert.True(store.TryBeginRun());
      store.Publish(new GenerationStatus { Result = EnGenResult.Finished });

      // Act: Attempting to clear with the id captured before run 2 claimed.
      var published = store.TryPublishIdle(staleClaimId);

      // Assert: The reset is refused; run 2's state is never wiped by a stale clear.
      Assert.False(published);
      Assert.Equal(EnGenResult.Finished, store.Current.Result);
      Assert.NotEqual(staleClaimId, store.ClaimId);
    }

    [Fact]
    public void TryPublishIdle_AfterTerminalWithWorld_DropsWorldFromSnapshot()
    {
      // Arrange: Store whose terminal snapshot exposes a world and the claim id to clear with.
      var store = new GenerationStatusStore();
      store.Publish(new GenerationStatus { Result = EnGenResult.Finished }, new LilWorld());
      var expectedClaimId = store.ClaimId;

      // Act: Reset to idle.
      Assert.True(store.TryPublishIdle(expectedClaimId));

      // Assert: The pristine snapshot carries neither status nor world of the cleared run.
      Assert.Same(GenerationStatus.Default, store.Current);
      Assert.Null(store.CurrentSnapshot.World);
    }
  }

  /// <summary>Tests the CAS guarantees under thread races: run claims, resume claims and clear-vs-claim.</summary>
  [Collection("GenerationStatusStoreTests")]
  public sealed class ConcurrencyTests
  {
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

    [Fact]
    public async Task TryPublishIdle_WhenClaimRacesClear_NeverResetsAStaleRun()
    {
      // Repeat the race so both orderings (clear first, claim first) are exercised.
      for (var iteration = 0; iteration < 50; iteration++)
      {
        // Arrange: Run 1 finished and its claim id was captured for clearing; a concurrent
        // claim for run 2 (which then finishes on its own) races the reset.
        var store = new GenerationStatusStore();
        Assert.True(store.TryBeginRun());
        store.Publish(new GenerationStatus { Result = EnGenResult.Finished });
        var staleClaimId = store.ClaimId;
        using var barrier = new Barrier(2);
        var cleared = false;
        var claimed = false;

        var clearTask = Task.Run(() =>
        {
          barrier.SignalAndWait();
          cleared = store.TryPublishIdle(staleClaimId);
        });
        var claimTask = Task.Run(() =>
        {
          barrier.SignalAndWait();
          claimed = store.TryBeginRun();
          if (claimed) store.Publish(new GenerationStatus { Result = EnGenResult.Finished });
        });

        // Act: Wait until both racers finished.
        await Task.WhenAll(clearTask, claimTask);

        if (cleared)
        {
          // Assert: The reset won, so the claim must have followed from idle and run 2's
          // terminal state is the last thing published - never wiped by the stale reset.
          Assert.True(claimed);
          Assert.Equal(EnGenResult.Finished, store.Current.Result);
        }
        else
        {
          // Assert: A refusal must be explained by the concurrent claim winning first;
          // run 2's state is left untouched.
          Assert.True(claimed);
          Assert.Equal(EnGenResult.Finished, store.Current.Result);
          Assert.NotEqual(staleClaimId, store.ClaimId);
        }
      }
    }
  }
}
