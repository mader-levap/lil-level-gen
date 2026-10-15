using System.Diagnostics;

using LostInLimbo.LevelGen.Api;
using LostInLimbo.LevelGen.Data.Config;
using LostInLimbo.LevelGen.Data.Other;

namespace LostInLimbo.LevelGen.Tests.Api;

/// <summary>
/// Tests the static level generation API end to end, grouped into suites by command
/// (Start, Resume, Pause, Cancel, Clear) plus a suite for the command races.
/// </summary>
/// <remarks>
/// The API keeps global state, so every suite is pinned to the
/// <c>LevelGenerationApi</c> xunit collection: the suites never run in parallel with
/// each other, and every test ends at a terminal state. Each suite's
/// <see cref="IDisposable.Dispose"/> restores pristine state for the next test, so
/// order and suite selection do not matter.
/// </remarks>
public sealed class LevelGenerationTests
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

  /// <summary>
  /// Config for a run that must still be in flight when the test issues its command
  /// (Cancel/Pause/Clear).
  /// </summary>
  private static CnfLevelGeneration InFlightConfig() => Config(sleep: 5_000, steps: 50);

  /// <summary>Polls until no run is in flight; fails the test on timeout.</summary>
  private static void WaitForTerminal()
  {
    var stopwatch = Stopwatch.StartNew();
    while (LevelGeneration.Status().Result == EnGenResult.Running)
    {
      if (stopwatch.Elapsed > TerminalTimeout)
        Assert.Fail($"Generation did not reach a terminal state within {TerminalTimeout}.");
      Thread.Sleep(2);
    }
  }

  /// <summary>Restores pristine state so the next test starts from None with no world.</summary>
  private static void ResetState()
  {
    // Clear every test seam first: a hook left behind by a failed test must never fire in a
    // later test's commands.
    LevelGeneration.AfterClaimTestHook = null;
    LevelGeneration.BeforeClearPauseTestHook = null;
    LevelGeneration.WorldSampleTestHook = null;
    WaitForTerminal();
    LevelGeneration.Clear();
  }

  /// <summary>Tests the pristine never-ran state exposed before any command runs.</summary>
  [Collection("LevelGenerationApi")]
  public sealed class InitialStateTests : IDisposable
  {
    /// <inheritdoc cref="ResetState"/>
    public void Dispose() => ResetState();

    [Fact]
    public void Status_BeforeAnyRun_ReportsNoneWithNullWorld()
    {
      // Arrange: Pristine state guaranteed by the previous test's Dispose (or by being
      // the first test in a fresh process; both end at the same never-ran state).

      // Assert: The API reports the never-ran status and exposes no world.
      Assert.Equal(EnGenResult.None, LevelGeneration.Status().Result);
      Assert.Null(LevelGeneration.Status().Error);
      Assert.Null(LevelGeneration.World());
    }
  }

  /// <summary>Tests <see cref="LevelGeneration.Start(EnRunKind, CnfLevelGeneration, CancellationToken)"/>: claims, validation and start failures.</summary>
  [Collection("LevelGenerationApi")]
  public sealed class StartTests : IDisposable
  {
    /// <inheritdoc cref="ResetState"/>
    public void Dispose() => ResetState();

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
    public void Start_WithNullConfig_ThrowsArgumentNullExceptionWithoutClaimingRun()
    {
      // Act/Assert: A null config is rejected before the run is claimed.
      var exception = Assert.Throws<ArgumentNullException>(
        () => LevelGeneration.Start(EnRunKind.All, null!));

      // Assert: The exception names the parameter and no claim was taken.
      Assert.Equal("config", exception.ParamName);
      Assert.NotEqual(EnGenResult.Running, LevelGeneration.Status().Result);
    }

    [Fact]
    public void Start_WithPlaceholderStepsBelowOne_ThrowsNamingParameter()
    {
      // Arrange: Idle state and a config with a placeholder step count below one.
      var invalid = Config(steps: 0);

      // Act/Assert: Validation rejects the step count naming the offending parameter.
      var exception = Assert.Throws<ArgumentOutOfRangeException>(
        () => LevelGeneration.Start(EnRunKind.All, invalid));

      // Assert: The failed call left no claim behind.
      Assert.Equal("PlaceholderSteps", exception.ParamName);
      Assert.NotEqual(EnGenResult.Running, LevelGeneration.Status().Result);
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
    public void Start_AfterErrorRun_Succeeds()
    {
      // Arrange: A failed run; Error is terminal but a fresh Start may claim a new run.
      Assert.True(LevelGeneration.Start(EnRunKind.All, Config(failAtStart: true)));
      WaitForTerminal();
      Assert.Equal(EnGenResult.Error, LevelGeneration.Status().Result);

      // Act: Starting a fresh run after the failure.
      var started = LevelGeneration.Start(EnRunKind.All, Config(sleep: 0));

      // Assert: The fresh run completes normally and replaces the failed run's state.
      Assert.True(started);
      WaitForTerminal();
      Assert.Equal(EnGenResult.Finished, LevelGeneration.Status().Result);
      Assert.Null(LevelGeneration.Status().Error);
      Assert.NotNull(LevelGeneration.World());
    }
  }

  /// <summary>Tests <see cref="LevelGeneration.Resume(EnRunKind)"/>: stepwise and whole-level resumes.</summary>
  [Collection("LevelGenerationApi")]
  public sealed class ResumeTests : IDisposable
  {
    /// <inheritdoc cref="ResetState"/>
    public void Dispose() => ResetState();

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
    public void Resume_AfterExplicitPause_WithAllKind_Finishes()
    {
      // Arrange: A whole-level run over a 100 ms-per-step placeholder level, long enough to
      // pause into but short enough that the resumed remainder still finishes well in time.
      Assert.True(LevelGeneration.Start(EnRunKind.All, Config(sleep: 1000, steps: 10)));

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
    public void Resume_AfterErrorRun_ReturnsFalse()
    {
      // Arrange: A failed run; only a paused run can be resumed.
      Assert.True(LevelGeneration.Start(EnRunKind.All, Config(failAtStart: true)));
      WaitForTerminal();
      Assert.Equal(EnGenResult.Error, LevelGeneration.Status().Result);

      // Act: Attempting to resume the failed run.
      var resumed = LevelGeneration.Resume(EnRunKind.All);

      // Assert: Resuming is refused because failed runs cannot be continued, and the
      // error state is left untouched.
      Assert.False(resumed);
      Assert.Equal(EnGenResult.Error, LevelGeneration.Status().Result);
      Assert.NotNull(LevelGeneration.Status().Error);
    }
  }

  /// <summary>Tests <see cref="LevelGeneration.Pause()"/>: accepted pauses and no-op refusals.</summary>
  [Collection("LevelGenerationApi")]
  public sealed class PauseTests : IDisposable
  {
    /// <inheritdoc cref="ResetState"/>
    public void Dispose() => ResetState();

    [Fact]
    public void Pause_WhileRunningAllKind_PublishesPausedAndWorldVisible()
    {
      // Arrange: A whole-level run long enough that it cannot finish before it is paused.
      Assert.True(LevelGeneration.Start(EnRunKind.All, InFlightConfig()));
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
      Assert.True(LevelGeneration.Start(EnRunKind.All, InFlightConfig()));
      Assert.True(LevelGeneration.Cancel());
      WaitForTerminal();
      Assert.Equal(EnGenResult.Cancelled, LevelGeneration.Status().Result);

      // Act: Attempting to pause the cancelled run.
      var paused = LevelGeneration.Pause();

      // Assert: There is nothing left to pause and the terminal state stands.
      Assert.False(paused);
      Assert.Equal(EnGenResult.Cancelled, LevelGeneration.Status().Result);
    }
  }

  /// <summary>Tests <see cref="LevelGeneration.Cancel()"/>: accepted cancellations, token cancellation and post-cancel recovery.</summary>
  [Collection("LevelGenerationApi")]
  public sealed class CancelTests : IDisposable
  {
    /// <inheritdoc cref="ResetState"/>
    public void Dispose() => ResetState();

    [Fact]
    public void Cancel_WhileRunning_PublishesCancelledWorldNullErrorNull()
    {
      // Arrange: A run long enough that it cannot finish before it is cancelled.
      Assert.True(LevelGeneration.Start(EnRunKind.All, InFlightConfig()));
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
    public void Cancel_AfterPausedRun_ReturnsFalse()
    {
      // Arrange: A paused run; it is no longer in flight, so there is nothing to cancel.
      Assert.True(LevelGeneration.Start(EnRunKind.Step, Config(sleep: 0)));
      WaitForTerminal();
      Assert.Equal(EnGenResult.Paused, LevelGeneration.Status().Result);

      // Act: Cancelling the paused run.
      var cancelled = LevelGeneration.Cancel();

      // Assert: The call is a no-op; cancelling a paused run goes through a fresh Start.
      Assert.False(cancelled);
      Assert.Equal(EnGenResult.Paused, LevelGeneration.Status().Result);
      Assert.NotNull(LevelGeneration.World());
    }

    [Fact]
    public void Start_WithPreCancelledToken_PublishesCancelled()
    {
      // Arrange: A token that is already cancelled before the run starts.
      using var cancellationTokenSource = new CancellationTokenSource();
      cancellationTokenSource.Cancel();

      // Act: Start the run with the cancelled token.
      var started = LevelGeneration.Start(EnRunKind.All, InFlightConfig(), cancellationTokenSource.Token);

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
      Assert.True(LevelGeneration.Start(EnRunKind.All, InFlightConfig(), cancellationTokenSource.Token));
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
      Assert.True(LevelGeneration.Start(EnRunKind.All, InFlightConfig()));
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
      Assert.True(LevelGeneration.Start(EnRunKind.All, InFlightConfig()));
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
  }

  /// <summary>Tests <see cref="LevelGeneration.Clear()"/>: resets of idle, running and terminal state.</summary>
  [Collection("LevelGenerationApi")]
  public sealed class ClearTests : IDisposable
  {
    /// <inheritdoc cref="ResetState"/>
    public void Dispose() => ResetState();

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
    public void Clear_WhileRunning_PausesStopsAndClears()
    {
      // Arrange: A whole-level run long enough that it cannot finish before it is cleared.
      Assert.True(LevelGeneration.Start(EnRunKind.All, InFlightConfig()));
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
      Assert.True(LevelGeneration.Start(EnRunKind.All, InFlightConfig()));
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
      Assert.True(LevelGeneration.Start(EnRunKind.All, InFlightConfig()));
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

  /// <summary>Tests command races through the test seams: Clear/Start, Cancel/Pause during claim windows and World sampling.</summary>
  [Collection("LevelGenerationApi")]
  public sealed class RaceTests : IDisposable
  {
    /// <inheritdoc cref="ResetState"/>
    public void Dispose() => ResetState();

    [Fact]
    public async Task Clear_And_Start_WhenRaced_NeverLeaveInconsistentState()
    {
      // Repeat a synchronized Clear/Start race so both orderings (clear first, start first)
      // are exercised; each iteration settles back to pristine state before the next one.
      for (var iteration = 0; iteration < 10; iteration++)
      {
        // Arrange: Pristine state guaranteed by the previous iteration's settle.
        using var barrier = new Barrier(2);
        var started = false;
        var cleared = false;

        // Act: Race a fresh run against a clear with a zero wait budget.
        var startTask = Task.Run(() =>
        {
          barrier.SignalAndWait();
          started = LevelGeneration.Start(EnRunKind.All, Config(sleep: 100));
        });
        var clearTask = Task.Run(() =>
        {
          barrier.SignalAndWait();
          cleared = LevelGeneration.Clear(TimeSpan.Zero);
        });
        await Task.WhenAll(startTask, clearTask);

        // Assert: Start is never refused: every iteration begins from a non-running state.
        Assert.True(started);

        // Act: Let whatever run won the race reach its terminal state.
        WaitForTerminal();
        var result = LevelGeneration.Status().Result;

        // Assert: Any stable state must expose its world - a state that reports Finished or
        // Paused with a null world means Clear wiped the run's generator mid-flight.
        if (result is EnGenResult.Finished or EnGenResult.Paused)
          Assert.NotNull(LevelGeneration.World());

        // Assert: The winning run's state is coherent with what the clear could have done.
        Assert.True(result is EnGenResult.Finished or EnGenResult.Paused,
          $"Unexpected terminal state {result} after clear={cleared}, start={started}.");

        // Act: Settle back to pristine for the next iteration.
        var settled = LevelGeneration.Clear();

        // Assert: Once the run reached a terminal state, clearing always succeeds.
        Assert.True(settled);
        Assert.Equal(EnGenResult.None, LevelGeneration.Status().Result);
        Assert.Null(LevelGeneration.World());
      }

      // Assert: After the storm, a full run still starts and completes normally.
      Assert.True(LevelGeneration.Start(EnRunKind.All, Config(sleep: 0)));
      WaitForTerminal();
      Assert.Equal(EnGenResult.Finished, LevelGeneration.Status().Result);
      Assert.NotNull(LevelGeneration.World());
    }

    [Fact]
    public async Task Clear_ConcurrentWithStartOfNewRun_NeverWipesTheNewRun()
    {
      // Arrange: A long-running first run; Clear runs in the background, so it pauses run 1
      // and then races its reset against the fresh Start below while run 1 stops.
      Assert.True(LevelGeneration.Start(EnRunKind.All, InFlightConfig()));
      var clearTask = Task.Run(() => LevelGeneration.Clear());

      // Act: Wait for the pause to stop run 1, then claim the next run while Clear is
      // between observing the terminal state and publishing the reset.
      WaitForTerminal();
      var startedNew = LevelGeneration.Start(EnRunKind.All, Config(sleep: 0));

      // Act: Let the background clear reach its decision.
      var cleared = await clearTask.WaitAsync(TerminalTimeout);

      // Act: Run 2 (or the run started after a successful clear) completes on its own.
      WaitForTerminal();
      var result = LevelGeneration.Status().Result;

      // Assert: The fresh start was never refused: the state was non-running when claimed.
      Assert.True(startedNew);

      // Assert: Whichever side won the race, the surviving run reports a stable state with a
      // visible world. A Clear that waited out run 2 and then wiped it would end in None with
      // a null world instead.
      Assert.Equal(EnGenResult.Finished, result);
      Assert.NotNull(LevelGeneration.World());

      // Assert: The clear either reset before the claim (startedNew then ran from scratch)
      // or correctly refused once the new claim appeared - never both disturbed states.
      if (!cleared) Assert.Equal(EnGenResult.Finished, LevelGeneration.Status().Result);
    }

    [Fact]
    public async Task Cancel_DuringClaimWindowOfNextRun_CancelsTheNewRun()
    {
      // Arrange: A paused run, so the previous run's cancellation source is still installed when
      // the next run claims the state.
      Assert.True(LevelGeneration.Start(EnRunKind.Step, Config(sleep: 0)));
      WaitForTerminal();
      Assert.Equal(EnGenResult.Paused, LevelGeneration.Status().Result);

      using var cancelReachedCall = new ManualResetEventSlim(false);
      var startReturned = false;
      var cancelReturned = false;
      Exception? cancelFailure = null;

      try
      {
        // Act: Hold the claim window open until the racer reached its Cancel call, then give it
        // time to execute Cancel inside that window.
        LevelGeneration.AfterClaimTestHook = () =>
        {
          cancelReachedCall.Wait(TimeSpan.FromSeconds(5));
          Thread.Sleep(20);
        };
        var cancelTask = Task.Run(() =>
        {
          // Wait until the claim of the new run made the store report Running.
          var deadline = Stopwatch.StartNew();
          while (LevelGeneration.Status().Result != EnGenResult.Running && deadline.Elapsed < TimeSpan.FromSeconds(5))
            Thread.Sleep(1);
          cancelReachedCall.Set();
          try { cancelReturned = LevelGeneration.Cancel(); }
          catch (Exception exception) { cancelFailure = exception; }
        });
        var startTask = Task.Run(() => startReturned = LevelGeneration.Start(EnRunKind.All, Config(sleep: 2000)));

        // Act: Let both commands and the run they targeted settle.
        await Task.WhenAll(startTask, cancelTask);
        WaitForTerminal();

        // Assert: Both calls reported success, and no command threw.
        Assert.Null(cancelFailure);
        Assert.True(startReturned);
        Assert.True(cancelReturned);

        // Assert: The cancellation reached the run that held the claim when Cancel ran. Cancelling
        // the previous run's retired source instead would leave this run untouched, so it would
        // run its full budget and report Finished.
        Assert.Equal(EnGenResult.Cancelled, LevelGeneration.Status().Result);
        Assert.Null(LevelGeneration.World());
      }
      finally
      {
        LevelGeneration.AfterClaimTestHook = null;

        // Act: Never leave a running state behind for Dispose, whatever the outcome above was.
        if (LevelGeneration.Status().Result == EnGenResult.Running)
        {
          LevelGeneration.Cancel();
          WaitForTerminal();
        }
      }
    }

    [Fact]
    public async Task Cancel_DuringResumeClaimWindow_CancelsTheResumedRun()
    {
      // Arrange: A paused stepwise run whose full placeholder sleep is paid on resume, so the
      // resumed segment is long enough to observe and the paused run's source is still installed.
      Assert.True(LevelGeneration.Start(EnRunKind.Step, Config(sleep: 2000)));
      WaitForTerminal();
      Assert.Equal(EnGenResult.Paused, LevelGeneration.Status().Result);

      using var cancelReachedCall = new ManualResetEventSlim(false);
      var resumeReturned = false;
      var cancelReturned = false;
      Exception? cancelFailure = null;

      try
      {
        // Act: Hold the claim window open until the racer reached its Cancel call, then give it
        // time to execute Cancel inside that window.
        LevelGeneration.AfterClaimTestHook = () =>
        {
          cancelReachedCall.Wait(TimeSpan.FromSeconds(5));
          Thread.Sleep(20);
        };
        var cancelTask = Task.Run(() =>
        {
          // Wait until the claim of the resumed run made the store report Running.
          var deadline = Stopwatch.StartNew();
          while (LevelGeneration.Status().Result != EnGenResult.Running && deadline.Elapsed < TimeSpan.FromSeconds(5))
            Thread.Sleep(1);
          cancelReachedCall.Set();
          try { cancelReturned = LevelGeneration.Cancel(); }
          catch (Exception exception) { cancelFailure = exception; }
        });
        var resumeTask = Task.Run(() => resumeReturned = LevelGeneration.Resume(EnRunKind.All));

        // Act: Let both commands and the run they targeted settle.
        await Task.WhenAll(resumeTask, cancelTask);
        WaitForTerminal();

        // Assert: Both calls reported success, and no command threw.
        Assert.Null(cancelFailure);
        Assert.True(resumeReturned);
        Assert.True(cancelReturned);

        // Assert: The cancellation reached the resumed run. Cancelling the paused run's retired
        // source instead would leave the resumed segment untouched, so it would report Finished.
        Assert.Equal(EnGenResult.Cancelled, LevelGeneration.Status().Result);
        Assert.Null(LevelGeneration.World());
      }
      finally
      {
        LevelGeneration.AfterClaimTestHook = null;

        // Act: Never leave a running state behind for Dispose, whatever the outcome above was.
        if (LevelGeneration.Status().Result == EnGenResult.Running)
        {
          LevelGeneration.Cancel();
          WaitForTerminal();
        }
      }
    }

    [Fact]
    public async Task Pause_DuringClaimWindowOfNextRun_PausesTheNewRun()
    {
      // Arrange: A paused run, so the previous run's generator is still installed when the next
      // run claims the state.
      Assert.True(LevelGeneration.Start(EnRunKind.Step, Config(sleep: 0)));
      WaitForTerminal();
      Assert.Equal(EnGenResult.Paused, LevelGeneration.Status().Result);

      using var pauseReachedCall = new ManualResetEventSlim(false);
      var startReturned = false;
      var pauseReturned = false;
      Exception? pauseFailure = null;

      try
      {
        // Act: Hold the claim window open until the racer reached its Pause call, then give it
        // time to execute Pause inside that window.
        LevelGeneration.AfterClaimTestHook = () =>
        {
          pauseReachedCall.Wait(TimeSpan.FromSeconds(5));
          Thread.Sleep(20);
        };
        var pauseTask = Task.Run(() =>
        {
          // Wait until the claim of the new run made the store report Running.
          var deadline = Stopwatch.StartNew();
          while (LevelGeneration.Status().Result != EnGenResult.Running && deadline.Elapsed < TimeSpan.FromSeconds(5))
            Thread.Sleep(1);
          pauseReachedCall.Set();
          try { pauseReturned = LevelGeneration.Pause(); }
          catch (Exception exception) { pauseFailure = exception; }
        });
        var startTask = Task.Run(() => startReturned = LevelGeneration.Start(EnRunKind.All, Config(sleep: 2000)));

        // Act: Let both commands and the run they targeted settle.
        await Task.WhenAll(startTask, pauseTask);
        WaitForTerminal();

        // Assert: Both calls reported success, and no command threw.
        Assert.Null(pauseFailure);
        Assert.True(startReturned);
        Assert.True(pauseReturned);

        // Assert: The accepted request stopped the new run. Acknowledging the previous run's dead
        // generator instead would leave this run untouched, so it would report Finished.
        Assert.Equal(EnGenResult.Paused, LevelGeneration.Status().Result);
        Assert.True(LevelGeneration.World()!.Stats.Steps < 10,
          $"Expected a partially stepped run, got {LevelGeneration.World()!.Stats.Steps} steps.");
      }
      finally
      {
        LevelGeneration.AfterClaimTestHook = null;

        // Act: Never leave a running state behind for Dispose, whatever the outcome above was.
        if (LevelGeneration.Status().Result == EnGenResult.Running)
        {
          LevelGeneration.Cancel();
          WaitForTerminal();
        }
      }
    }

    [Fact]
    public void Clear_ConcurrentWithSelfTerminatingRun_NeverPausesTheNextRun()
    {
      // Arrange: A run long enough that Clear observes it as Running and enters its pause path
      // (60 ms per step: still in flight when Clear checks, yet cancelled from the hook fast).
      Assert.True(LevelGeneration.Start(EnRunKind.All, Config(sleep: 600)));
      Assert.Equal(EnGenResult.Running, LevelGeneration.Status().Result);

      var cleared = false;
      var nextRunStarted = false;

      try
      {
        // Act: Inside Clear's window between capturing the claim and requesting the pause, the
        // observed run terminates by itself and the next run claims the state - so Clear no
        // longer owns the run it is about to pause.
        LevelGeneration.BeforeClearPauseTestHook = () =>
        {
          Assert.True(LevelGeneration.Cancel());
          WaitForTerminal();
          nextRunStarted = LevelGeneration.Start(EnRunKind.All, Config(sleep: 400));
        };

        cleared = LevelGeneration.Clear(TimeSpan.FromSeconds(10));

        // Act: Let the run that claimed during Clear settle.
        WaitForTerminal();

        // Assert: Clear refused because a newer run claimed the state it captured.
        Assert.True(nextRunStarted);
        Assert.False(cleared);

        // Assert: The newer run was never disturbed by the refused Clear: it ran its full step
        // budget instead of stopping at a pause nobody requested for it.
        Assert.Equal(EnGenResult.Finished, LevelGeneration.Status().Result);
        Assert.Equal(10L, LevelGeneration.World()!.Stats.Steps);
      }
      finally
      {
        LevelGeneration.BeforeClearPauseTestHook = null;

        // Act: Never leave a running state behind for Dispose, whatever the outcome above was.
        if (LevelGeneration.Status().Result == EnGenResult.Running)
        {
          LevelGeneration.Cancel();
          WaitForTerminal();
        }
      }
    }

    [Fact]
    public void World_WhenNextRunClaimsDuringCall_ReturnsTheObservedRunsWorld()
    {
      // Arrange: A finished run whose world holds every completed step.
      Assert.True(LevelGeneration.Start(EnRunKind.All, Config(sleep: 0)));
      WaitForTerminal();
      Assert.Equal(EnGenResult.Finished, LevelGeneration.Status().Result);

      try
      {
        // Act: Claim and install the next run between World()'s state sample and its return.
        // The next run sleeps long enough (100 ms per step) that its own world still shows zero
        // steps while the call is in flight, so the two worlds are distinguishable.
        LevelGeneration.WorldSampleTestHook = () =>
          Assert.True(LevelGeneration.Start(EnRunKind.All, Config(sleep: 1000)));

        var world = LevelGeneration.World();

        // Assert: The returned world belongs to the run whose status the call observed (finished,
        // all steps), never to the run that claimed while the call was in flight (no steps yet).
        Assert.NotNull(world);
        Assert.Equal(10L, world!.Stats.Steps);
      }
      finally
      {
        LevelGeneration.WorldSampleTestHook = null;

        // Act: Stop the long-running claim the hook started so Dispose never waits for it.
        if (LevelGeneration.Status().Result == EnGenResult.Running)
        {
          LevelGeneration.Cancel();
          WaitForTerminal();
        }
      }
    }

    [Fact]
    public async Task ExternalTokenCancel_RacingDisposalOfItsRunSource_NeverThrows()
    {
      // Repeat so both orderings (external cancel first, disposal first) are exercised: the run's
      // linked cancellation source is disposed by the next claim or by Clear, racing the caller
      // who cancels through its own token.
      for (var iteration = 0; iteration < 30; iteration++)
      {
        // Arrange: A finished run whose source is linked to a caller-owned token.
        using var cancellationTokenSource = new CancellationTokenSource();
        Assert.True(LevelGeneration.Start(EnRunKind.All, Config(sleep: 0), cancellationTokenSource.Token));
        WaitForTerminal();

        using var barrier = new Barrier(2);
        Exception? cancelFailure = null;
        var nextRunStarted = false;
        var cleared = false;

        // Act: Race the external cancel against whatever disposes that run's linked source.
        var cancelTask = Task.Run(() =>
        {
          try
          {
            barrier.SignalAndWait();
            cancellationTokenSource.Cancel();
          }
          catch (Exception exception)
          {
            cancelFailure = exception;
          }
        });
        var disposalTask = Task.Run(() =>
        {
          barrier.SignalAndWait();
          if (iteration % 2 == 0)
          {
            nextRunStarted = LevelGeneration.Start(EnRunKind.All, Config(sleep: 0));
          }
          else
          {
            cleared = LevelGeneration.Clear();
          }
        });
        await Task.WhenAll(cancelTask, disposalTask);

        // Assert: The caller's Cancel never observes the disposal of the linked source.
        Assert.Null(cancelFailure);

        // Assert: The disposal side of the race went through: the state was terminal beforehand,
        // so both a fresh claim and a reset must always be accepted.
        if (iteration % 2 == 0) Assert.True(nextRunStarted);
        else Assert.True(cleared);

        // Act: Settle back to pristine for the next iteration.
        WaitForTerminal();
        if (LevelGeneration.Status().Result != EnGenResult.None)
          Assert.True(LevelGeneration.Clear());
      }

      // Assert: After the storm, a full run still starts and completes normally.
      Assert.True(LevelGeneration.Start(EnRunKind.All, Config(sleep: 0)));
      WaitForTerminal();
      Assert.Equal(EnGenResult.Finished, LevelGeneration.Status().Result);
      Assert.NotNull(LevelGeneration.World());
    }
  }
}
