using System.Diagnostics;

using LostInLimbo.LevelGen.Api;
using LostInLimbo.LevelGen.Data.Config;
using LostInLimbo.LevelGen.Data.Other;

namespace LostInLimbo.LevelGen.Tests.Api;

/// <summary>
/// Tests the static level generation API end to end. Tests in this class run sequentially
/// (xunit gives each class its own collection) because the API keeps global state; every
/// test ends at a terminal state and <see cref="Dispose"/> restores pristine state for
/// the next one, so order does not matter.
/// </summary>
public sealed class LevelGenerationTests : IDisposable
{
  private static readonly TimeSpan TerminalTimeout = TimeSpan.FromSeconds(10);

  /// <summary>
  /// Config with a short placeholder sleep, giving tests a safe window to observe
  /// mid-run state (Running, hidden world) before the run reaches a terminal state.
  /// The sleep is split evenly across the placeholder steps (100 ms per step by default).
  /// </summary>
  /// <param name="sleep">Total ms the generator should sleep instead of doing generation.</param>
  /// <param name="failAtStart">If true, generator fails at start.</param>
  /// <param name="steps">How many placeholder steps the generation is split into.</param>
  private static CnfLevelGeneration Config(int sleep = 1000, bool failAtStart = false, int steps = CnfDebug.DefaultPlaceholderSteps)
  {
    return CnfLevelGeneration.Default with
    {
      Debug = new CnfDebug
      {
        PlaceholderSleep = sleep,
        PlaceholderSteps = steps,
        FailAtStart = failAtStart,
      },
    };
  }

  /// <summary>Polls until no run is in flight; fails the test on timeout.</summary>
  private static void WaitForTerminal()
  {
    var stopwatch = Stopwatch.StartNew();
    while (LevelGeneration.Status().Result == EnGenResult.Running)
    {
      if (stopwatch.Elapsed > TerminalTimeout)
        Assert.Fail($"Generation did not reach a terminal state within {TerminalTimeout}.");
      Thread.Sleep(10);
    }
  }

  /// <summary>Restores pristine state so the next test starts from None with no world.</summary>
  public void Dispose()
  {
    WaitForTerminal();
    LevelGeneration.Clear();
  }

  // TESTS

  [Fact]
  public void Start_WithStepKind_ReportsRunningWorldNull_ThenPausedWorldVisible()
  {
    // Arrange: Pristine state guaranteed by the previous test's Dispose.

    // Act: Start a stepwise run.
    var started = LevelGeneration.Start(EnRunKind.Step, Config());

    // Assert: The run is in flight and its world stays hidden while being mutated.
    Assert.True(started);
    Assert.Equal(EnGenResult.Running, LevelGeneration.Status().Result);
    Assert.Null(LevelGeneration.World());

    // Act: Wait for the placeholder work to end.
    WaitForTerminal();

    // Assert: A paused run exposes its partial world.
    Assert.Equal(EnGenResult.Paused, LevelGeneration.Status().Result);
    Assert.NotNull(LevelGeneration.World());
  }

  [Fact]
  public void Start_WhileRunIsInProgress_ReturnsFalse()
  {
    // Arrange: A run is already in flight.
    Assert.True(LevelGeneration.Start(EnRunKind.Step, Config()));

    // Act: Attempting to start a second run.
    var startedAgain = LevelGeneration.Start(EnRunKind.All, Config());

    // Assert: The second start is refused while the first run holds the claim.
    Assert.False(startedAgain);

    // Act: Let the first run finish.
    WaitForTerminal();

    // Assert: Only the first run ever executed.
    Assert.Equal(EnGenResult.Paused, LevelGeneration.Status().Result);
  }

  [Fact]
  public void Start_WithStepKind_ThenResumeWithAllKind_Finishes()
  {
    // Arrange: A stepwise run paused after its first segment.
    Assert.True(LevelGeneration.Start(EnRunKind.Step, Config()));
    WaitForTerminal();
    Assert.Equal(EnGenResult.Paused, LevelGeneration.Status().Result);

    // Act: Resume as a full run.
    var resumed = LevelGeneration.Resume(EnRunKind.All);

    // Assert: Resume honors the new run kind and completes the level.
    Assert.True(resumed);
    WaitForTerminal();
    Assert.Equal(EnGenResult.Finished, LevelGeneration.Status().Result);
    Assert.NotNull(LevelGeneration.World());

    // Assert: Resuming an already finished run is refused.
    Assert.False(LevelGeneration.Resume(EnRunKind.Step));
  }

  [Fact]
  public void Resume_WithStepKind_RepeatedlyEventuallyFinishes()
  {
    // Arrange: A stepwise run over a three-step placeholder level.
    Assert.True(LevelGeneration.Start(EnRunKind.Step, Config(sleep: 0, steps: 3)));
    WaitForTerminal();
    Assert.Equal(EnGenResult.Paused, LevelGeneration.Status().Result);

    // Act: Resume one step at a time until no step remains (guard against an endless pause).
    var resumes = 0;
    while (LevelGeneration.Status().Result == EnGenResult.Paused && resumes < 10)
    {
      Assert.True(LevelGeneration.Resume(EnRunKind.Step));
      WaitForTerminal();
      resumes++;
    }

    // Assert: Three steps total, so the start plus two resumes finished the level.
    Assert.Equal(EnGenResult.Finished, LevelGeneration.Status().Result);
    Assert.Equal(2, resumes);
    Assert.NotNull(LevelGeneration.World());

    // Assert: The result data counted every completed step.
    Assert.Equal(3L, LevelGeneration.World()!.Stats.Steps);
  }

  [Fact]
  public void Start_WithInvalidConfig_ThrowsAndStatusIsNotStuck()
  {
    // Arrange: Idle state and a config violating its own invariants.
    var invalid = CnfLevelGeneration.Default with
    {
      General = new CnfGeneral { Corruption = -1.0f },
    };

    // Act/Assert: Start validates the config before claiming the run.
    Assert.Throws<ArgumentOutOfRangeException>(() => LevelGeneration.Start(EnRunKind.All, invalid));

    // Assert: The failed call left no claim behind.
    Assert.NotEqual(EnGenResult.Running, LevelGeneration.Status().Result);

    // Assert: A subsequent valid start still works.
    Assert.True(LevelGeneration.Start(EnRunKind.All, Config()));
    WaitForTerminal();
    Assert.Equal(EnGenResult.Finished, LevelGeneration.Status().Result);
  }

  [Fact]
  public void Start_WithFailAtStart_PublishesErrorAndWorldIsNull()
  {
    // Arrange: Debug config that fails immediately.
    var config = Config(failAtStart: true);

    // Act: Start the run and wait for the failure.
    Assert.True(LevelGeneration.Start(EnRunKind.All, config));
    WaitForTerminal();

    // Assert: The error is reported with details and the partial world stays hidden.
    Assert.Equal(EnGenResult.Error, LevelGeneration.Status().Result);
    Assert.NotNull(LevelGeneration.Status().Error);
    Assert.Null(LevelGeneration.World());
  }

  [Fact]
  public void Clear_WhenIdle_RestoresPristineState()
  {
    // Arrange: A completed run with a visible world.
    Assert.True(LevelGeneration.Start(EnRunKind.All, Config()));
    WaitForTerminal();
    Assert.Equal(EnGenResult.Finished, LevelGeneration.Status().Result);
    Assert.NotNull(LevelGeneration.World());

    // Act: Clear the static API.
    var cleared = LevelGeneration.Clear();

    // Assert: Back to the never-ran state.
    Assert.True(cleared);
    Assert.Equal(EnGenResult.None, LevelGeneration.Status().Result);
    Assert.Null(LevelGeneration.Status().Error);
    Assert.Null(LevelGeneration.World());
  }

  [Fact]
  public void Cancel_WhileRunning_PublishesCancelledWorldNullErrorNull()
  {
    // Arrange: A run long enough that it cannot finish before it is cancelled.
    Assert.True(LevelGeneration.Start(EnRunKind.All, Config(sleep: 30_000)));
    Assert.Equal(EnGenResult.Running, LevelGeneration.Status().Result);

    // Act: Request cancellation of the in-flight run.
    var cancelled = LevelGeneration.Cancel();

    // Assert: The run accepted the cancellation and reports it as its terminal state.
    Assert.True(cancelled);
    WaitForTerminal();
    Assert.Equal(EnGenResult.Cancelled, LevelGeneration.Status().Result);

    // Assert: Cancellation is not an error and the partial world is discarded.
    Assert.Null(LevelGeneration.Status().Error);
    Assert.Null(LevelGeneration.World());
  }

  [Fact]
  public void Cancel_WhenNoRunIsInFlight_ReturnsFalse()
  {
    // Arrange: Pristine state with no run at all.
    Assert.False(LevelGeneration.Cancel());

    // Arrange: A completed run with a visible world.
    Assert.True(LevelGeneration.Start(EnRunKind.All, Config(sleep: 0)));
    WaitForTerminal();
    Assert.Equal(EnGenResult.Finished, LevelGeneration.Status().Result);

    // Act: Cancelling after the run already reached a terminal state.
    var cancelled = LevelGeneration.Cancel();

    // Assert: The call is a no-op and does not disturb the finished run.
    Assert.False(cancelled);
    Assert.Equal(EnGenResult.Finished, LevelGeneration.Status().Result);
    Assert.NotNull(LevelGeneration.World());
  }

  [Fact]
  public void Start_WithPreCancelledToken_PublishesCancelled()
  {
    // Arrange: A token that is already cancelled before the run starts.
    using var cancellationTokenSource = new CancellationTokenSource();
    cancellationTokenSource.Cancel();

    // Act: Start the run with the cancelled token.
    var started = LevelGeneration.Start(EnRunKind.All, Config(sleep: 30_000), cancellationTokenSource.Token);

    // Assert: The run is accepted, then ends at its first checkpoint instead of generating.
    Assert.True(started);
    WaitForTerminal();
    Assert.Equal(EnGenResult.Cancelled, LevelGeneration.Status().Result);
    Assert.Null(LevelGeneration.Status().Error);
    Assert.Null(LevelGeneration.World());
  }

  [Fact]
  public void Start_WithExternalToken_CancelsRunWhenTokenFires()
  {
    // Arrange: A caller-owned token linked into the run.
    using var cancellationTokenSource = new CancellationTokenSource();
    Assert.True(LevelGeneration.Start(EnRunKind.All, Config(sleep: 30_000), cancellationTokenSource.Token));
    Assert.Equal(EnGenResult.Running, LevelGeneration.Status().Result);

    // Act: The caller cancels through its own token, not through LevelGeneration.Cancel().
    cancellationTokenSource.Cancel();

    // Assert: The linked source forwards the cancellation to the run.
    WaitForTerminal();
    Assert.Equal(EnGenResult.Cancelled, LevelGeneration.Status().Result);
    Assert.Null(LevelGeneration.World());
  }

  [Fact]
  public void Resume_AfterCancelledRun_ReturnsFalse()
  {
    // Arrange: A cancelled run. Cancelled is terminal, so only a fresh Start is allowed.
    Assert.True(LevelGeneration.Start(EnRunKind.Step, Config(sleep: 30_000)));
    Assert.True(LevelGeneration.Cancel());
    WaitForTerminal();
    Assert.Equal(EnGenResult.Cancelled, LevelGeneration.Status().Result);

    // Act: Attempting to resume the cancelled run.
    var resumed = LevelGeneration.Resume(EnRunKind.All);

    // Assert: Resuming is refused because cancelled runs cannot be continued.
    Assert.False(resumed);
    Assert.Equal(EnGenResult.Cancelled, LevelGeneration.Status().Result);
  }

  [Fact]
  public void Start_AfterCancelledRun_Succeeds()
  {
    // Arrange: A cancelled run that discarded its partial data.
    Assert.True(LevelGeneration.Start(EnRunKind.All, Config(sleep: 30_000)));
    Assert.True(LevelGeneration.Cancel());
    WaitForTerminal();
    Assert.Equal(EnGenResult.Cancelled, LevelGeneration.Status().Result);

    // Act: Starting a fresh run after the cancellation.
    var started = LevelGeneration.Start(EnRunKind.All, Config(sleep: 0));

    // Assert: The fresh run completes normally and produces a world again.
    Assert.True(started);
    WaitForTerminal();
    Assert.Equal(EnGenResult.Finished, LevelGeneration.Status().Result);
    Assert.NotNull(LevelGeneration.World());
  }

  [Fact]
  public void Pause_WhileRunningAllKind_PublishesPausedAndWorldVisible()
  {
    // Arrange: A whole-level run long enough that it cannot finish before it is paused.
    Assert.True(LevelGeneration.Start(EnRunKind.All, Config(sleep: 30_000)));
    Assert.Equal(EnGenResult.Running, LevelGeneration.Status().Result);

    // Act: Request pause of the in-flight whole-level run.
    var paused = LevelGeneration.Pause();

    // Assert: The run accepted the pause and reports it as its terminal state.
    Assert.True(paused);
    WaitForTerminal();
    Assert.Equal(EnGenResult.Paused, LevelGeneration.Status().Result);

    // Assert: Pausing is not an error and the partial world is preserved for inspection.
    Assert.Null(LevelGeneration.Status().Error);
    Assert.NotNull(LevelGeneration.World());
  }

  [Fact]
  public void Pause_WhenNoRunIsInFlight_ReturnsFalse()
  {
    // Arrange: Pristine state with no run at all.
    Assert.False(LevelGeneration.Pause());

    // Act: Pausing after the run already reached a terminal state (finished run arranged below).
    Assert.True(LevelGeneration.Start(EnRunKind.All, Config(sleep: 0)));
    WaitForTerminal();
    Assert.Equal(EnGenResult.Finished, LevelGeneration.Status().Result);
    var paused = LevelGeneration.Pause();

    // Assert: The call is a no-op and does not disturb the finished run.
    Assert.False(paused);
    Assert.Equal(EnGenResult.Finished, LevelGeneration.Status().Result);
    Assert.NotNull(LevelGeneration.World());
  }

  [Fact]
  public void Pause_AfterFinished_ReturnsFalse()
  {
    // Arrange: A completed run with a visible world.
    Assert.True(LevelGeneration.Start(EnRunKind.All, Config(sleep: 0)));
    WaitForTerminal();
    Assert.Equal(EnGenResult.Finished, LevelGeneration.Status().Result);

    // Act: Attempting to pause the finished run.
    var paused = LevelGeneration.Pause();

    // Assert: There is nothing left to pause and the terminal state stands.
    Assert.False(paused);
    Assert.Equal(EnGenResult.Finished, LevelGeneration.Status().Result);
    Assert.NotNull(LevelGeneration.World());
  }

  [Fact]
  public void Pause_AfterCancelledRun_ReturnsFalse()
  {
    // Arrange: A cancelled run; cancelled is terminal.
    Assert.True(LevelGeneration.Start(EnRunKind.All, Config(sleep: 30_000)));
    Assert.True(LevelGeneration.Cancel());
    WaitForTerminal();
    Assert.Equal(EnGenResult.Cancelled, LevelGeneration.Status().Result);

    // Act: Attempting to pause the cancelled run.
    var paused = LevelGeneration.Pause();

    // Assert: There is nothing left to pause and the terminal state stands.
    Assert.False(paused);
    Assert.Equal(EnGenResult.Cancelled, LevelGeneration.Status().Result);
  }

  [Fact]
  public void Resume_AfterExplicitPause_WithAllKind_Finishes()
  {
    // Arrange: A whole-level run over a 300 ms-per-step placeholder level, long enough to
    // pause into but short enough that the resumed remainder still finishes well in time.
    Assert.True(LevelGeneration.Start(EnRunKind.All, Config(sleep: 3000, steps: 10)));

    // Act: Pause the run, wait for the boundary to be observed.
    Assert.True(LevelGeneration.Pause());
    WaitForTerminal();

    // Assert: The whole-level run paused with its partial world preserved.
    Assert.Equal(EnGenResult.Paused, LevelGeneration.Status().Result);
    Assert.NotNull(LevelGeneration.World());

    // Act: Resume the paused generation as a whole-level run again.
    var resumed = LevelGeneration.Resume(EnRunKind.All);

    // Assert: The resumed run completes the level from where the pause stopped it.
    Assert.True(resumed);
    WaitForTerminal();
    Assert.Equal(EnGenResult.Finished, LevelGeneration.Status().Result);
    Assert.NotNull(LevelGeneration.World());
    Assert.Equal(10L, LevelGeneration.World()!.Stats.Steps);
  }

  [Fact]
  public void Clear_WhileRunning_PausesStopsAndClears()
  {
    // Arrange: A whole-level run long enough that it cannot finish before it is cleared.
    Assert.True(LevelGeneration.Start(EnRunKind.All, Config(sleep: 30_000)));
    Assert.Equal(EnGenResult.Running, LevelGeneration.Status().Result);

    // Act: Clear the API while the run is in flight.
    var cleared = LevelGeneration.Clear();

    // Assert: The run was paused, stopped, and the state was reset to pristine.
    Assert.True(cleared);
    Assert.Equal(EnGenResult.None, LevelGeneration.Status().Result);
    Assert.Null(LevelGeneration.Status().Error);
    Assert.Null(LevelGeneration.World());
  }

  [Fact]
  public void Clear_WhileRunning_WithZeroTimeout_ReturnsFalseAndKeepsState()
  {
    // Arrange: A whole-level run whose first step is still sleeping.
    Assert.True(LevelGeneration.Start(EnRunKind.All, Config(sleep: 30_000)));
    Assert.Equal(EnGenResult.Running, LevelGeneration.Status().Result);

    // Act: Clear with no patience for the in-flight run.
    var cleared = LevelGeneration.Clear(TimeSpan.Zero);

    // Assert: Nothing was cleared; the run is still in flight (or just paused).
    Assert.False(cleared);
    Assert.NotEqual(EnGenResult.None, LevelGeneration.Status().Result);

    // Act: The pause request landed, so the run stops on its own; clear again without rush.
    WaitForTerminal();
    Assert.Equal(EnGenResult.Paused, LevelGeneration.Status().Result);
    var clearedAfterStop = LevelGeneration.Clear();

    // Assert: With the worker stopped, clearing succeeds.
    Assert.True(clearedAfterStop);
    Assert.Equal(EnGenResult.None, LevelGeneration.Status().Result);
    Assert.Null(LevelGeneration.World());
  }

  [Fact]
  public void Clear_WithNegativeTimeout_ThrowsArgumentOutOfRangeException()
  {
    // Act/Assert: A negative timeout is rejected before any state is touched.
    var exception = Assert.Throws<ArgumentOutOfRangeException>(
      () => LevelGeneration.Clear(TimeSpan.FromMilliseconds(-1)));
    Assert.Equal("timeout", exception.ParamName);
    Assert.Equal(EnGenResult.None, LevelGeneration.Status().Result);
  }

  [Fact]
  public void Clear_AfterCancelledRun_ClearsState()
  {
    // Arrange: A cancelled run; cancelled is terminal but not yet cleared.
    Assert.True(LevelGeneration.Start(EnRunKind.All, Config(sleep: 30_000)));
    Assert.True(LevelGeneration.Cancel());
    WaitForTerminal();
    Assert.Equal(EnGenResult.Cancelled, LevelGeneration.Status().Result);

    // Act: Clear the terminal-but-dirty state.
    var cleared = LevelGeneration.Clear();

    // Assert: Back to pristine, with no lingering error or world.
    Assert.True(cleared);
    Assert.Equal(EnGenResult.None, LevelGeneration.Status().Result);
    Assert.Null(LevelGeneration.Status().Error);
    Assert.Null(LevelGeneration.World());
  }

  [Fact]
  public void Start_AfterClear_Succeeds()
  {
    // Arrange: A paused run with a partial world, then cleared.
    Assert.True(LevelGeneration.Start(EnRunKind.Step, Config()));
    WaitForTerminal();
    Assert.Equal(EnGenResult.Paused, LevelGeneration.Status().Result);
    Assert.True(LevelGeneration.Clear());

    // Act: Starting a fresh run after the clear.
    var started = LevelGeneration.Start(EnRunKind.All, Config(sleep: 0));

    // Assert: The fresh run completes normally from scratch.
    Assert.True(started);
    WaitForTerminal();
    Assert.Equal(EnGenResult.Finished, LevelGeneration.Status().Result);
    Assert.NotNull(LevelGeneration.World());
  }
}
