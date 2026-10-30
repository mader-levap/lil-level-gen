using System.Diagnostics;

using LostInLimbo.LevelGen.Api;
using LostInLimbo.LevelGen.Data.Config;
using LostInLimbo.LevelGen.Data.Result.Stats;
using LostInLimbo.LevelGen.Data.Status;

namespace LostInLimbo.LevelGen.Tests.Api;

/// <summary>
/// Tests the level generation session API end to end, grouped into suites by command
/// (Start, Resume, Pause, Cancel, Clear) plus a suite for the command races.
/// </summary>
/// <remarks>
/// Every test method gets a fresh <see cref="LevelGenSession"/> (xunit constructs a new
/// test-class instance per test), so each test starts from the pristine never-ran state with
/// no teardown, no shared global state, and no dependence on test order or suite selection.
/// </remarks>
public sealed class LevelGenSessionTests
{
  private static readonly TimeSpan TerminalTimeout = TimeSpan.FromSeconds(10);

  /// <summary>
  /// Config with a short placeholder sleep, giving tests a safe window to observe
  /// mid-run state (Running, hidden world) before the run reaches a terminal state.
  /// The sleep is split evenly across the placeholder steps (100 ms per step by default).
  /// </summary>
  /// <param name="sleep">Total ms the generator should sleep instead of doing generation.</param>
  /// <param name="failAtStart">If true, the generator fails at start.</param>
  /// <param name="steps">How many placeholder steps the generation is split into.</param>
  /// <param name="failGraphBuild">If true, the graph build reports failure.</param>
  /// <param name="failGraphValidation">If true, graph validation reports failure after a successful build.</param>
  private static CnfLevelGeneration Config(int sleep = 1000, bool failAtStart = false, int steps = CnfDebug.DefaultPlaceholderSteps, bool failGraphBuild = false, bool failGraphValidation = false)
  {
    return CnfLevelGeneration.Default with
    {
      Debug = new CnfDebug
      {
        PlaceholderSleep = sleep,
        PlaceholderSteps = steps,
        FailAtStart = failAtStart,
        FailGraphBuild = failGraphBuild,
        FailGraphValidation = failGraphValidation,
      },
    };
  }

  /// <summary>
  /// Config for a run that must still be in flight when the test issues its command
  /// (Cancel/Pause/Clear).
  /// </summary>
  private static CnfLevelGeneration InFlightConfig() => Config(sleep: 5_000, steps: 50);

  /// <summary>Polls the given session until no run is in flight; fails the test on timeout.</summary>
  private static void WaitForTerminal(LevelGenSession session)
  {
    var stopwatch = Stopwatch.StartNew();
    while (session.Status().Result == EnGenResult.Running)
    {
      if (stopwatch.Elapsed > TerminalTimeout)
        Assert.Fail($"Generation did not reach a terminal state within {TerminalTimeout}.");
      Thread.Sleep(2);
    }
  }

  /// <summary>Tests the pristine never-ran state exposed before any command runs.</summary>
  public sealed class InitialStateTests
  {
    private readonly LevelGenSession _session = new();

    [Fact]
    public void Status_BeforeAnyRun_ReportsNoneWithNullWorld()
    {
      // Arrange: A fresh session starts in the never-ran state.

      // Assert: The session reports the never-ran status and exposes no world.
      Assert.Equal(EnGenResult.None, _session.Status().Result);
      Assert.Null(_session.Status().Error);
      Assert.Null(_session.World());
    }
  }

  /// <summary>Tests <see cref="LevelGenSession.Start(EnRunKind, CnfLevelGeneration, CancellationToken)"/>: claims, validation and start failures.</summary>
  public sealed class StartTests
  {
    private readonly LevelGenSession _session = new();

    [Fact]
    public void Start_WithStepKind_ReportsRunningWorldNull_ThenPausedWorldVisible()
    {
      // Arrange: Fresh session in its never-ran state.

      // Act: Start a stepwise run.
      var started = _session.Start(EnRunKind.Step, Config());

      // Assert: The run is in flight and its world stays hidden while being mutated.
      Assert.True(started);
      Assert.Equal(EnGenResult.Running, _session.Status().Result);
      Assert.Null(_session.World());

      // Act: Wait for the placeholder work to end.
      WaitForTerminal(_session);

      // Assert: A paused run exposes its partial world.
      Assert.Equal(EnGenResult.Paused, _session.Status().Result);
      Assert.NotNull(_session.World());
    }

    [Fact]
    public void Start_WhileRunIsInProgress_ReturnsFalse()
    {
      // Arrange: A run is already in flight.
      Assert.True(_session.Start(EnRunKind.Step, Config()));

      // Act: Attempting to start a second run.
      var startedAgain = _session.Start(EnRunKind.All, Config());

      // Assert: The second start is refused while the first run holds the claim.
      Assert.False(startedAgain);

      // Act: Let the first run finish.
      WaitForTerminal(_session);

      // Assert: Only the first run ever executed.
      Assert.Equal(EnGenResult.Paused, _session.Status().Result);
    }

    [Fact]
    public void Start_WithInvalidConfig_ThrowsAndStatusIsNotStuck()
    {
      // Arrange: Idle state and a config violating its own invariants.
      var invalid = CnfLevelGeneration.Default with
      {
        General = new CnfGeneral { Degeneration = -1.0f },
      };

      // Act/Assert: Start validates the config before claiming the run.
      Assert.Throws<ArgumentOutOfRangeException>(() => _session.Start(EnRunKind.All, invalid));

      // Assert: The failed call left no claim behind.
      Assert.NotEqual(EnGenResult.Running, _session.Status().Result);

      // Assert: A subsequent valid start still works.
      Assert.True(_session.Start(EnRunKind.All, Config()));
      WaitForTerminal(_session);
      Assert.Equal(EnGenResult.Finished, _session.Status().Result);
    }

    [Fact]
    public void Start_WithNullConfig_ThrowsArgumentNullExceptionWithoutClaimingRun()
    {
      // Act/Assert: A null config is rejected before the run is claimed.
      var exception = Assert.Throws<ArgumentNullException>(
        () => _session.Start(EnRunKind.All, null!));

      // Assert: The exception names the parameter and no claim was taken.
      Assert.Equal("config", exception.ParamName);
      Assert.NotEqual(EnGenResult.Running, _session.Status().Result);
    }

    [Fact]
    public void Start_WithPlaceholderStepsBelowOne_ThrowsNamingParameter()
    {
      // Arrange: Idle state and a config with a placeholder step count below one.
      var invalid = Config(steps: 0);

      // Act/Assert: Validation rejects the step count naming the offending parameter.
      var exception = Assert.Throws<ArgumentOutOfRangeException>(
        () => _session.Start(EnRunKind.All, invalid));

      // Assert: The failed call left no claim behind.
      Assert.Equal("PlaceholderSteps", exception.ParamName);
      Assert.NotEqual(EnGenResult.Running, _session.Status().Result);
    }

    [Fact]
    public void Start_WithInvalidRunKind_ThrowsWithoutClaimingRun()
    {
      // Arrange: Idle state and an out-of-range cast of EnRunKind.
      var invalidKind = (EnRunKind)99;

      // Act/Assert: Start rejects an undefined run kind before claiming the run.
      var exception = Assert.Throws<ArgumentOutOfRangeException>(
        () => _session.Start(invalidKind, Config()));

      // Assert: The exception names the offending parameter and no claim was taken.
      Assert.Equal("enRunKind", exception.ParamName);
      Assert.NotEqual(EnGenResult.Running, _session.Status().Result);

      // Assert: A subsequent valid start still works.
      Assert.True(_session.Start(EnRunKind.All, Config()));
      WaitForTerminal(_session);
      Assert.Equal(EnGenResult.Finished, _session.Status().Result);
    }

    [Fact]
    public void Start_WithFailAtStart_PublishesErrorAndWorldIsNull()
    {
      // Arrange: Debug config that fails immediately.
      var config = Config(failAtStart: true);

      // Act: Start the run and wait for the failure.
      Assert.True(_session.Start(EnRunKind.All, config));
      WaitForTerminal(_session);

      // Assert: The error is reported with details and the partial world stays hidden.
      Assert.Equal(EnGenResult.Error, _session.Status().Result);
      Assert.NotNull(_session.Status().Error);
      Assert.Null(_session.World());
    }

    [Fact]
    public void Start_WithFailGraphBuild_PublishesErrorAndWorldIsNull()
    {
      // Arrange: Debug config that fails the graph build.
      var config = Config(failGraphBuild: true);

      // Act: Start the run and wait for the failure.
      Assert.True(_session.Start(EnRunKind.All, config));
      WaitForTerminal(_session);

      // Assert: The failed build fails the run; the partially built graph stays hidden.
      Assert.Equal(EnGenResult.Error, _session.Status().Result);
      Assert.NotNull(_session.Status().Error);
      Assert.Null(_session.World());
    }

    [Fact]
    public void Start_WithValidationFailure_PublishesValidationErrorAndWorldVisible()
    {
      // Arrange: Debug config whose graph builds but fails validation.
      var config = Config(failGraphValidation: true);

      // Act: Start the run and wait for the validation failure.
      Assert.True(_session.Start(EnRunKind.All, config));
      WaitForTerminal(_session);

      // Assert: Unlike a plain error, the validation failure is reported distinctly, carries
      // its reason instead of an exception, and exposes the built graph for debugging.
      Assert.Equal(EnGenResult.ValidationError, _session.Status().Result);
      Assert.Null(_session.Status().Error);
      Assert.Equal("Deliberate validation failure.", _session.Status().FailureReason);
      var world = _session.World();
      Assert.NotNull(world);
      Assert.Equal(EnPhase.Graph, world.Phase);
      Assert.NotEmpty(world.Graph.Nodes);
    }

    [Fact]
    public void Start_AfterValidationErrorRun_Succeeds()
    {
      // Arrange: A validation-failed run; ValidationError is terminal but a fresh Start may
      // claim a new run.
      Assert.True(_session.Start(EnRunKind.All, Config(failGraphValidation: true)));
      WaitForTerminal(_session);
      Assert.Equal(EnGenResult.ValidationError, _session.Status().Result);

      // Act: Starting a fresh run after the validation failure.
      var started = _session.Start(EnRunKind.All, Config(sleep: 0));

      // Assert: The fresh run completes normally and replaces the failed run's state,
      // including the stale failure reason.
      Assert.True(started);
      WaitForTerminal(_session);
      Assert.Equal(EnGenResult.Finished, _session.Status().Result);
      Assert.Null(_session.Status().Error);
      Assert.Null(_session.Status().FailureReason);
      Assert.NotNull(_session.World());
    }

    [Fact]
    public void Start_AfterErrorRun_Succeeds()
    {
      // Arrange: A failed run; Error is terminal but a fresh Start may claim a new run.
      Assert.True(_session.Start(EnRunKind.All, Config(failAtStart: true)));
      WaitForTerminal(_session);
      Assert.Equal(EnGenResult.Error, _session.Status().Result);

      // Act: Starting a fresh run after the failure.
      var started = _session.Start(EnRunKind.All, Config(sleep: 0));

      // Assert: The fresh run completes normally and replaces the failed run's state.
      Assert.True(started);
      WaitForTerminal(_session);
      Assert.Equal(EnGenResult.Finished, _session.Status().Result);
      Assert.Null(_session.Status().Error);
      Assert.NotNull(_session.World());
    }
  }

  /// <summary>Tests <see cref="LevelGenSession.Resume(EnRunKind)"/>: stepwise and whole-level resumes.</summary>
  public sealed class ResumeTests
  {
    private readonly LevelGenSession _session = new();

    [Fact]
    public void Start_WithStepKind_ThenResumeWithAllKind_Finishes()
    {
      // Arrange: A stepwise run paused after its first segment.
      Assert.True(_session.Start(EnRunKind.Step, Config()));
      WaitForTerminal(_session);
      Assert.Equal(EnGenResult.Paused, _session.Status().Result);

      // Act: Resume as a full run.
      var resumed = _session.Resume(EnRunKind.All);

      // Assert: Resume honors the new run kind and completes the level.
      Assert.True(resumed);
      WaitForTerminal(_session);
      Assert.Equal(EnGenResult.Finished, _session.Status().Result);
      Assert.NotNull(_session.World());

      // Assert: Resuming an already finished run is refused.
      Assert.False(_session.Resume(EnRunKind.Step));
    }

    [Fact]
    public void Resume_WithStepKind_RepeatedlyEventuallyFinishes()
    {
      // Arrange: A stepwise run over a three-step placeholder level.
      Assert.True(_session.Start(EnRunKind.Step, Config(sleep: 0, steps: 3)));
      WaitForTerminal(_session);
      Assert.Equal(EnGenResult.Paused, _session.Status().Result);

      // Act: Resume one step at a time until no step remains (guard against an endless pause).
      var resumes = 0;
      while (_session.Status().Result == EnGenResult.Paused && resumes < 10)
      {
        Assert.True(_session.Resume(EnRunKind.Step));
        WaitForTerminal(_session);
        resumes++;
      }

      // Assert: Three steps total, so the start plus two resumes finished the level.
      Assert.Equal(EnGenResult.Finished, _session.Status().Result);
      Assert.Equal(2, resumes);
      Assert.NotNull(_session.World());

      // Assert: The result data counted every completed step.
      Assert.Equal(3L, _session.World()!.Stats.Steps);
    }

    [Fact]
    public void Resume_AfterExplicitPause_WithAllKind_Finishes()
    {
      // Arrange: A whole-level run over a 100 ms-per-step placeholder level, long enough to
      // pause into but short enough that the resumed remainder still finishes well in time.
      Assert.True(_session.Start(EnRunKind.All, Config(sleep: 1000, steps: 10)));

      // Act: Pause the run, wait for the boundary to be observed.
      Assert.True(_session.Pause());
      WaitForTerminal(_session);

      // Assert: The whole-level run paused with its partial world preserved.
      Assert.Equal(EnGenResult.Paused, _session.Status().Result);
      Assert.NotNull(_session.World());

      // Act: Resume the paused generation as a whole-level run again.
      var resumed = _session.Resume(EnRunKind.All);

      // Assert: The resumed run completes the level from where the pause stopped it.
      Assert.True(resumed);
      WaitForTerminal(_session);
      Assert.Equal(EnGenResult.Finished, _session.Status().Result);
      Assert.NotNull(_session.World());
      Assert.Equal(10L, _session.World()!.Stats.Steps);
    }

    [Fact]
    public void Resume_AfterErrorRun_ReturnsFalse()
    {
      // Arrange: A failed run; only a paused run can be resumed.
      Assert.True(_session.Start(EnRunKind.All, Config(failAtStart: true)));
      WaitForTerminal(_session);
      Assert.Equal(EnGenResult.Error, _session.Status().Result);

      // Act: Attempting to resume the failed run.
      var resumed = _session.Resume(EnRunKind.All);

      // Assert: Resuming is refused because failed runs cannot be continued, and the
      // error state is left untouched.
      Assert.False(resumed);
      Assert.Equal(EnGenResult.Error, _session.Status().Result);
      Assert.NotNull(_session.Status().Error);
    }

    [Fact]
    public void Resume_AfterValidationErrorRun_ReturnsFalse()
    {
      // Arrange: A validation-failed run; only a paused run can be resumed, so the
      // validation state stays terminal and keeps its world for inspection.
      Assert.True(_session.Start(EnRunKind.All, Config(failGraphValidation: true)));
      WaitForTerminal(_session);
      Assert.Equal(EnGenResult.ValidationError, _session.Status().Result);

      // Act: Attempting to resume the validation-failed run.
      var resumed = _session.Resume(EnRunKind.All);

      // Assert: Resuming is refused, the validation state is untouched, and the graph
      // built by the failed run is still visible for debugging.
      Assert.False(resumed);
      Assert.Equal(EnGenResult.ValidationError, _session.Status().Result);
      Assert.Equal("Deliberate validation failure.", _session.Status().FailureReason);
      Assert.NotNull(_session.World());
    }

    [Fact]
    public void Resume_WithInvalidRunKind_ThrowsWithoutClaimingRun()
    {
      // Arrange: Idle state (nothing paused to resume).
      var invalidKind = (EnRunKind)99;

      // Act/Assert: Resume rejects an undefined run kind before touching any claim.
      var exception = Assert.Throws<ArgumentOutOfRangeException>(
        () => _session.Resume(invalidKind));

      // Assert: The exception names the offending parameter and no claim was taken.
      Assert.Equal("enRunKind", exception.ParamName);
      Assert.Equal(EnGenResult.None, _session.Status().Result);
    }
  }

  /// <summary>Tests <see cref="LevelGenSession.Pause()"/>: accepted pauses and no-op refusals.</summary>
  public sealed class PauseTests
  {
    private readonly LevelGenSession _session = new();

    [Fact]
    public void Pause_WhileRunningAllKind_PublishesPausedAndWorldVisible()
    {
      // Arrange: A whole-level run long enough that it cannot finish before it is paused.
      Assert.True(_session.Start(EnRunKind.All, InFlightConfig()));
      Assert.Equal(EnGenResult.Running, _session.Status().Result);

      // Act: Request pause of the in-flight whole-level run.
      var paused = _session.Pause();

      // Assert: The run accepted the pause and reports it as its terminal state.
      Assert.True(paused);
      WaitForTerminal(_session);
      Assert.Equal(EnGenResult.Paused, _session.Status().Result);

      // Assert: Pausing is not an error and the partial world is preserved for inspection.
      Assert.Null(_session.Status().Error);
      Assert.NotNull(_session.World());
    }

    [Fact]
    public void Pause_WhenNoRunIsInFlight_ReturnsFalse()
    {
      // Arrange: Fresh session with no run at all.
      Assert.False(_session.Pause());

      // Act: Pausing after the run already reached a terminal state (finished run arranged below).
      Assert.True(_session.Start(EnRunKind.All, Config(sleep: 0)));
      WaitForTerminal(_session);
      Assert.Equal(EnGenResult.Finished, _session.Status().Result);
      var paused = _session.Pause();

      // Assert: The call is a no-op and does not disturb the finished run.
      Assert.False(paused);
      Assert.Equal(EnGenResult.Finished, _session.Status().Result);
      Assert.NotNull(_session.World());
    }

    [Fact]
    public void Pause_AfterFinished_ReturnsFalse()
    {
      // Arrange: A completed run with a visible world.
      Assert.True(_session.Start(EnRunKind.All, Config(sleep: 0)));
      WaitForTerminal(_session);
      Assert.Equal(EnGenResult.Finished, _session.Status().Result);

      // Act: Attempting to pause the finished run.
      var paused = _session.Pause();

      // Assert: There is nothing left to pause and the terminal state stands.
      Assert.False(paused);
      Assert.Equal(EnGenResult.Finished, _session.Status().Result);
      Assert.NotNull(_session.World());
    }

    [Fact]
    public void Pause_AfterCancelledRun_ReturnsFalse()
    {
      // Arrange: A cancelled run; cancelled is terminal.
      Assert.True(_session.Start(EnRunKind.All, InFlightConfig()));
      Assert.True(_session.Cancel());
      WaitForTerminal(_session);
      Assert.Equal(EnGenResult.Cancelled, _session.Status().Result);

      // Act: Attempting to pause the cancelled run.
      var paused = _session.Pause();

      // Assert: There is nothing left to pause and the terminal state stands.
      Assert.False(paused);
      Assert.Equal(EnGenResult.Cancelled, _session.Status().Result);
    }
  }

  /// <summary>Tests <see cref="LevelGenSession.Cancel()"/>: accepted cancellations, token cancellation and post-cancel recovery.</summary>
  public sealed class CancelTests
  {
    private readonly LevelGenSession _session = new();

    [Fact]
    public void Cancel_WhileRunning_PublishesCancelledWorldNullErrorNull()
    {
      // Arrange: A run long enough that it cannot finish before it is cancelled.
      Assert.True(_session.Start(EnRunKind.All, InFlightConfig()));
      Assert.Equal(EnGenResult.Running, _session.Status().Result);

      // Act: Request cancellation of the in-flight run.
      var cancelled = _session.Cancel();

      // Assert: The run accepted the cancellation and reports it as its terminal state.
      Assert.True(cancelled);
      WaitForTerminal(_session);
      Assert.Equal(EnGenResult.Cancelled, _session.Status().Result);

      // Assert: Cancellation is not an error and the partial world is discarded.
      Assert.Null(_session.Status().Error);
      Assert.Null(_session.World());
    }

    [Fact]
    public void Cancel_WhenNoRunIsInFlight_ReturnsFalse()
    {
      // Arrange: Fresh session with no run at all.
      Assert.False(_session.Cancel());

      // Arrange: A completed run with a visible world.
      Assert.True(_session.Start(EnRunKind.All, Config(sleep: 0)));
      WaitForTerminal(_session);
      Assert.Equal(EnGenResult.Finished, _session.Status().Result);

      // Act: Cancelling after the run already reached a terminal state.
      var cancelled = _session.Cancel();

      // Assert: The call is a no-op and does not disturb the finished run.
      Assert.False(cancelled);
      Assert.Equal(EnGenResult.Finished, _session.Status().Result);
      Assert.NotNull(_session.World());
    }

    [Fact]
    public void Cancel_AfterPausedRun_ReturnsFalse()
    {
      // Arrange: A paused run; it is no longer in flight, so there is nothing to cancel.
      Assert.True(_session.Start(EnRunKind.Step, Config(sleep: 0)));
      WaitForTerminal(_session);
      Assert.Equal(EnGenResult.Paused, _session.Status().Result);

      // Act: Cancelling the paused run.
      var cancelled = _session.Cancel();

      // Assert: The call is a no-op; cancelling a paused run goes through a fresh Start.
      Assert.False(cancelled);
      Assert.Equal(EnGenResult.Paused, _session.Status().Result);
      Assert.NotNull(_session.World());
    }

    [Fact]
    public void Start_WithPreCancelledToken_PublishesCancelled()
    {
      // Arrange: A token that is already cancelled before the run starts.
      using var cancellationTokenSource = new CancellationTokenSource();
      cancellationTokenSource.Cancel();

      // Act: Start the run with the cancelled token.
      var started = _session.Start(EnRunKind.All, InFlightConfig(), cancellationTokenSource.Token);

      // Assert: The run is accepted, then ends at its first checkpoint instead of generating.
      Assert.True(started);
      WaitForTerminal(_session);
      Assert.Equal(EnGenResult.Cancelled, _session.Status().Result);
      Assert.Null(_session.Status().Error);
      Assert.Null(_session.World());
    }

    [Fact]
    public void Start_WithExternalToken_CancelsRunWhenTokenFires()
    {
      // Arrange: A caller-owned token linked into the run.
      using var cancellationTokenSource = new CancellationTokenSource();
      Assert.True(_session.Start(EnRunKind.All, InFlightConfig(), cancellationTokenSource.Token));
      Assert.Equal(EnGenResult.Running, _session.Status().Result);

      // Act: The caller cancels through its own token, not through the session's Cancel().
      cancellationTokenSource.Cancel();

      // Assert: The linked source forwards the cancellation to the run.
      WaitForTerminal(_session);
      Assert.Equal(EnGenResult.Cancelled, _session.Status().Result);
      Assert.Null(_session.World());
    }

    [Fact]
    public void Resume_AfterCancelledRun_ReturnsFalse()
    {
      // Arrange: A cancelled run. Cancelled is terminal, so only a fresh Start is allowed.
      Assert.True(_session.Start(EnRunKind.All, InFlightConfig()));
      Assert.True(_session.Cancel());
      WaitForTerminal(_session);
      Assert.Equal(EnGenResult.Cancelled, _session.Status().Result);

      // Act: Attempting to resume the cancelled run.
      var resumed = _session.Resume(EnRunKind.All);

      // Assert: Resuming is refused because cancelled runs cannot be continued.
      Assert.False(resumed);
      Assert.Equal(EnGenResult.Cancelled, _session.Status().Result);
    }

    [Fact]
    public void Start_AfterCancelledRun_Succeeds()
    {
      // Arrange: A cancelled run that discarded its partial data.
      Assert.True(_session.Start(EnRunKind.All, InFlightConfig()));
      Assert.True(_session.Cancel());
      WaitForTerminal(_session);
      Assert.Equal(EnGenResult.Cancelled, _session.Status().Result);

      // Act: Starting a fresh run after the cancellation.
      var started = _session.Start(EnRunKind.All, Config(sleep: 0));

      // Assert: The fresh run completes normally and produces a world again.
      Assert.True(started);
      WaitForTerminal(_session);
      Assert.Equal(EnGenResult.Finished, _session.Status().Result);
      Assert.NotNull(_session.World());
    }
  }

  /// <summary>Tests <see cref="LevelGenSession.Clear()"/>: resets of idle, running and terminal state.</summary>
  public sealed class ClearTests
  {
    private readonly LevelGenSession _session = new();

    [Fact]
    public void Clear_WhenIdle_RestoresPristineState()
    {
      // Arrange: A completed run with a visible world.
      Assert.True(_session.Start(EnRunKind.All, Config()));
      WaitForTerminal(_session);
      Assert.Equal(EnGenResult.Finished, _session.Status().Result);
      Assert.NotNull(_session.World());

      // Act: Clear the session.
      var cleared = _session.Clear();

      // Assert: Back to the never-ran state.
      Assert.True(cleared);
      Assert.Equal(EnGenResult.None, _session.Status().Result);
      Assert.Null(_session.Status().Error);
      Assert.Null(_session.World());
    }

    [Fact]
    public void Clear_WhileRunning_PausesStopsAndClears()
    {
      // Arrange: A whole-level run long enough that it cannot finish before it is cleared.
      Assert.True(_session.Start(EnRunKind.All, InFlightConfig()));
      Assert.Equal(EnGenResult.Running, _session.Status().Result);

      // Act: Clear the session while the run is in flight.
      var cleared = _session.Clear();

      // Assert: The run was paused, stopped, and the state was reset to pristine.
      Assert.True(cleared);
      Assert.Equal(EnGenResult.None, _session.Status().Result);
      Assert.Null(_session.Status().Error);
      Assert.Null(_session.World());
    }

    [Fact]
    public void Clear_WhileRunning_WithZeroTimeout_ReturnsFalseAndKeepsState()
    {
      // Arrange: A whole-level run whose first step is still sleeping.
      Assert.True(_session.Start(EnRunKind.All, InFlightConfig()));
      Assert.Equal(EnGenResult.Running, _session.Status().Result);

      // Act: Clear with no patience for the in-flight run.
      var cleared = _session.Clear(TimeSpan.Zero);

      // Assert: Nothing was cleared; the run is still in flight (or just paused).
      Assert.False(cleared);
      Assert.NotEqual(EnGenResult.None, _session.Status().Result);

      // Act: The pause request landed, so the run stops on its own; clear again without rush.
      WaitForTerminal(_session);
      Assert.Equal(EnGenResult.Paused, _session.Status().Result);
      var clearedAfterStop = _session.Clear();

      // Assert: With the worker stopped, clearing succeeds.
      Assert.True(clearedAfterStop);
      Assert.Equal(EnGenResult.None, _session.Status().Result);
      Assert.Null(_session.World());
    }

    [Fact]
    public void Clear_OnTerminalRun_WithZeroTimeout_Clears()
    {
      // Arrange: A completed run that is terminal but not yet cleared.
      Assert.True(_session.Start(EnRunKind.All, Config()));
      WaitForTerminal(_session);
      Assert.Equal(EnGenResult.Finished, _session.Status().Result);

      // Act: Clear the settled state with no patience.
      var cleared = _session.Clear(TimeSpan.Zero);

      // Assert: Zero timeout only refuses an in-flight run; an already terminal state clears.
      Assert.True(cleared);
      Assert.Equal(EnGenResult.None, _session.Status().Result);
      Assert.Null(_session.World());
    }

    [Fact]
    public void Clear_WithNegativeTimeout_ThrowsArgumentOutOfRangeException()
    {
      // Act/Assert: A negative timeout is rejected before any state is touched.
      var exception = Assert.Throws<ArgumentOutOfRangeException>(
        () => _session.Clear(TimeSpan.FromMilliseconds(-1)));
      Assert.Equal("timeout", exception.ParamName);
      Assert.Equal(EnGenResult.None, _session.Status().Result);
    }

    [Fact]
    public void Clear_AfterCancelledRun_ClearsState()
    {
      // Arrange: A cancelled run; cancelled is terminal but not yet cleared.
      Assert.True(_session.Start(EnRunKind.All, InFlightConfig()));
      Assert.True(_session.Cancel());
      WaitForTerminal(_session);
      Assert.Equal(EnGenResult.Cancelled, _session.Status().Result);

      // Act: Clear the terminal-but-dirty state.
      var cleared = _session.Clear();

      // Assert: Back to pristine, with no lingering error or world.
      Assert.True(cleared);
      Assert.Equal(EnGenResult.None, _session.Status().Result);
      Assert.Null(_session.Status().Error);
      Assert.Null(_session.World());
    }

    [Fact]
    public void Clear_AfterValidationErrorRun_ClearsState()
    {
      // Arrange: A validation-failed run; terminal but not yet cleared.
      Assert.True(_session.Start(EnRunKind.All, Config(failGraphValidation: true)));
      WaitForTerminal(_session);
      Assert.Equal(EnGenResult.ValidationError, _session.Status().Result);

      // Act: Clear the terminal-but-dirty state.
      var cleared = _session.Clear();

      // Assert: Back to pristine, with no lingering failure reason or world.
      Assert.True(cleared);
      Assert.Equal(EnGenResult.None, _session.Status().Result);
      Assert.Null(_session.Status().FailureReason);
      Assert.Null(_session.World());
    }

    [Fact]
    public void Start_AfterClear_Succeeds()
    {
      // Arrange: A paused run with a partial world, then cleared.
      Assert.True(_session.Start(EnRunKind.Step, Config()));
      WaitForTerminal(_session);
      Assert.Equal(EnGenResult.Paused, _session.Status().Result);
      Assert.True(_session.Clear());

      // Act: Starting a fresh run after the clear.
      var started = _session.Start(EnRunKind.All, Config(sleep: 0));

      // Assert: The fresh run completes normally from scratch.
      Assert.True(started);
      WaitForTerminal(_session);
      Assert.Equal(EnGenResult.Finished, _session.Status().Result);
      Assert.NotNull(_session.World());
    }
  }

  /// <summary>
  /// Tests that <see cref="LevelGenSession.World()"/> hands out defensive copies:
  /// consumers can never affect the generator or later reads by mutating the returned world.
  /// </summary>
  public sealed class WorldIsolationTests
  {
    private readonly LevelGenSession _session = new();

    [Fact]
    public void World_AfterFinished_ReturnsFreshCopyEachCall()
    {
      // Arrange: A completed run with a visible world containing a built graph.
      Assert.True(_session.Start(EnRunKind.All, Config(sleep: 0)));
      WaitForTerminal(_session);
      Assert.Equal(EnGenResult.Finished, _session.Status().Result);

      // Act: Read the world twice.
      var first = _session.World();
      var second = _session.World();

      // Assert: Each call hands out its own instance carrying the same generated data.
      Assert.NotNull(first);
      Assert.NotNull(second);
      Assert.NotSame(first, second);
      Assert.NotSame(first!.Graph, second!.Graph);
      Assert.NotSame(first.Stats, second.Stats);
      Assert.NotSame(first.Graph.Nodes, second.Graph.Nodes);
      Assert.NotSame(first.Graph.Edges, second.Graph.Edges);
      Assert.Equal(first.Phase, second.Phase);
      Assert.Equal(first.Stats.Steps, second.Stats.Steps);
      Assert.Equal(first.Graph.Nodes.Count, second.Graph.Nodes.Count);
      Assert.Equal(first.Graph.Edges.Count, second.Graph.Edges.Count);
    }

    [Fact]
    public void World_MutatingReturnedWorld_DoesNotAffectSubsequentCalls()
    {
      // Arrange: A completed run with a visible world containing a built graph.
      Assert.True(_session.Start(EnRunKind.All, Config(sleep: 0)));
      WaitForTerminal(_session);
      Assert.Equal(EnGenResult.Finished, _session.Status().Result);

      // Act: Mutate every mutable surface of the handed-out world the way a misbehaving
      // consumer would: top-level sections and the nested node/edge containers.
      var world = _session.World();
      Assert.NotNull(world);
      foreach (var node in world!.Graph.Nodes)
        node.Pois.Clear();
      foreach (var edge in world.Graph.Edges)
        edge.Conditions.Clear();
      world.Graph.Nodes.Clear();
      world.Graph.Edges.Clear();
      world.Graph = new();
      world.Stats.Steps = 999;
      world.Stats = new();
      world.Phase = EnPhase.Prepass;

      // Assert: The next call still reports the consistent generated world.
      var next = _session.World();
      Assert.NotNull(next);
      Assert.Equal(EnPhase.Completed, next!.Phase);
      Assert.Equal(10L, next.Stats.Steps);
      Assert.NotEmpty(next.Graph.Nodes);
      Assert.Contains(next.Graph.Nodes, node => node.Pois.Count > 0);
    }

    [Fact]
    public void World_PausedWorldMutatedBeforeResume_DoesNotCorruptGeneration()
    {
      // Arrange: A stepwise run paused after its first segment: the graph is built and one
      // map step completed, so both the graph and the step counter carry real data.
      Assert.True(_session.Start(EnRunKind.Step, Config(sleep: 0)));
      WaitForTerminal(_session);
      Assert.Equal(EnGenResult.Paused, _session.Status().Result);

      // Act: Mutate the handed-out world the way a misbehaving consumer would.
      var world = _session.World();
      Assert.NotNull(world);
      world!.Stats.Steps = 999;
      world.Graph.Nodes.Clear();
      world.Phase = EnPhase.Prepass;

      // Act: Continue generation from the paused state.
      Assert.True(_session.Resume(EnRunKind.All));
      WaitForTerminal(_session);

      // Assert: The resumed run completes against its own untouched data, not the mutated
      // copy: full step budget, intact graph, correct final phase.
      Assert.Equal(EnGenResult.Finished, _session.Status().Result);
      var finalWorld = _session.World();
      Assert.NotNull(finalWorld);
      Assert.Equal(10L, finalWorld!.Stats.Steps);
      Assert.NotEmpty(finalWorld.Graph.Nodes);
      Assert.Equal(EnPhase.Completed, finalWorld.Phase);
    }
  }

  /// <summary>Tests command races through the test seams: Clear/Start, Cancel/Pause during claim windows and World sampling.</summary>
  public sealed class RaceTests
  {
    private readonly LevelGenSession _session = new();

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
          started = _session.Start(EnRunKind.All, Config(sleep: 100));
        });
        var clearTask = Task.Run(() =>
        {
          barrier.SignalAndWait();
          cleared = _session.Clear(TimeSpan.Zero);
        });
        await Task.WhenAll(startTask, clearTask);

        // Assert: Start is never refused: every iteration begins from a non-running state.
        Assert.True(started);

        // Act: Let whatever run won the race reach its terminal state.
        WaitForTerminal(_session);
        var result = _session.Status().Result;

        // Assert: Any stable state must expose its world - a state that reports Finished or
        // Paused with a null world means Clear wiped the run's generator mid-flight.
        if (result is EnGenResult.Finished or EnGenResult.Paused)
          Assert.NotNull(_session.World());

        // Assert: The winning run's state is coherent with what the clear could have done.
        Assert.True(result is EnGenResult.Finished or EnGenResult.Paused,
          $"Unexpected terminal state {result} after clear={cleared}, start={started}.");

        // Act: Settle back to pristine for the next iteration.
        var settled = _session.Clear();

        // Assert: Once the run reached a terminal state, clearing always succeeds.
        Assert.True(settled);
        Assert.Equal(EnGenResult.None, _session.Status().Result);
        Assert.Null(_session.World());
      }

      // Assert: After the storm, a full run still starts and completes normally.
      Assert.True(_session.Start(EnRunKind.All, Config(sleep: 0)));
      WaitForTerminal(_session);
      Assert.Equal(EnGenResult.Finished, _session.Status().Result);
      Assert.NotNull(_session.World());
    }

    [Fact]
    public async Task Clear_ConcurrentWithStartOfNewRun_NeverWipesTheNewRun()
    {
      // Arrange: A long-running first run; Clear runs in the background, so it pauses run 1
      // and then races its reset against the fresh Start below while run 1 stops.
      Assert.True(_session.Start(EnRunKind.All, InFlightConfig()));
      var clearTask = Task.Run(() => _session.Clear());

      // Act: Wait for the pause to stop run 1, then claim the next run while Clear is
      // between observing the terminal state and publishing the reset.
      WaitForTerminal(_session);
      var startedNew = _session.Start(EnRunKind.All, Config(sleep: 0));

      // Act: Let the background clear reach its decision.
      var cleared = await clearTask.WaitAsync(TerminalTimeout);

      // Act: Run 2 (or the run started after a successful clear) completes on its own.
      WaitForTerminal(_session);
      var result = _session.Status().Result;

      // Assert: The fresh start was never refused: the state was non-running when claimed.
      Assert.True(startedNew);

      // Assert: Whichever side won the race, the surviving run reports a stable state with a
      // visible world. A Clear that waited out run 2 and then wiped it would end in None with
      // a null world instead.
      Assert.Equal(EnGenResult.Finished, result);
      Assert.NotNull(_session.World());

      // Assert: The clear either reset before the claim (startedNew then ran from scratch)
      // or correctly refused once the new claim appeared - never both disturbed states.
      if (!cleared) Assert.Equal(EnGenResult.Finished, _session.Status().Result);
    }

    [Fact]
    public async Task Cancel_DuringClaimWindowOfNextRun_CancelsTheNewRun()
    {
      // Arrange: A paused run, so the previous run's cancellation source is still installed when
      // the next run claims the state.
      Assert.True(_session.Start(EnRunKind.Step, Config(sleep: 0)));
      WaitForTerminal(_session);
      Assert.Equal(EnGenResult.Paused, _session.Status().Result);

      using var cancelReachedCall = new ManualResetEventSlim(false);
      var startReturned = false;
      var cancelReturned = false;
      Exception? cancelFailure = null;

      try
      {
        // Act: Hold the claim window open until the racer reaches its Cancel call, then give it
        // time to execute Cancel inside that window.
        _session.AfterClaimTestHook = () =>
        {
          cancelReachedCall.Wait(TimeSpan.FromSeconds(5));
          Thread.Sleep(20);
        };
        var cancelTask = Task.Run(() =>
        {
          // Wait until the claim of the new run makes the store report Running.
          var deadline = Stopwatch.StartNew();
          while (_session.Status().Result != EnGenResult.Running && deadline.Elapsed < TimeSpan.FromSeconds(5))
            Thread.Sleep(1);
          cancelReachedCall.Set();
          try { cancelReturned = _session.Cancel(); }
          catch (Exception exception) { cancelFailure = exception; }
        });
        var startTask = Task.Run(() => startReturned = _session.Start(EnRunKind.All, Config(sleep: 2000)));

        // Act: Let both commands and the run they targeted settle.
        await Task.WhenAll(startTask, cancelTask);
        WaitForTerminal(_session);

        // Assert: Both calls reported success, and no command threw.
        Assert.Null(cancelFailure);
        Assert.True(startReturned);
        Assert.True(cancelReturned);

        // Assert: The cancellation reached the run that held the claim when Cancel ran. Cancelling
        // the previous run's retired source instead would leave this run untouched, so it would
        // run its full budget and report Finished.
        Assert.Equal(EnGenResult.Cancelled, _session.Status().Result);
        Assert.Null(_session.World());
      }
      finally
      {
        _session.AfterClaimTestHook = null;

        // Act: Never leave a running state behind, whatever the outcome above was.
        if (_session.Status().Result == EnGenResult.Running)
        {
          _session.Cancel();
          WaitForTerminal(_session);
        }
      }
    }

    [Fact]
    public async Task Cancel_DuringResumeClaimWindow_CancelsTheResumedRun()
    {
      // Arrange: A paused stepwise run whose full placeholder sleep is paid on resume, so the
      // resumed segment is long enough to observe and the paused run's source is still installed.
      Assert.True(_session.Start(EnRunKind.Step, Config(sleep: 2000)));
      WaitForTerminal(_session);
      Assert.Equal(EnGenResult.Paused, _session.Status().Result);

      using var cancelReachedCall = new ManualResetEventSlim(false);
      var resumeReturned = false;
      var cancelReturned = false;
      Exception? cancelFailure = null;

      try
      {
        // Act: Hold the claim window open until the racer reaches its Cancel call, then give it
        // time to execute Cancel inside that window.
        _session.AfterClaimTestHook = () =>
        {
          cancelReachedCall.Wait(TimeSpan.FromSeconds(5));
          Thread.Sleep(20);
        };
        var cancelTask = Task.Run(() =>
        {
          // Wait until the claim of the resumed run makes the store report Running.
          var deadline = Stopwatch.StartNew();
          while (_session.Status().Result != EnGenResult.Running && deadline.Elapsed < TimeSpan.FromSeconds(5))
            Thread.Sleep(1);
          cancelReachedCall.Set();
          try { cancelReturned = _session.Cancel(); }
          catch (Exception exception) { cancelFailure = exception; }
        });
        var resumeTask = Task.Run(() => resumeReturned = _session.Resume(EnRunKind.All));

        // Act: Let both commands and the run they targeted settle.
        await Task.WhenAll(resumeTask, cancelTask);
        WaitForTerminal(_session);

        // Assert: Both calls reported success, and no command threw.
        Assert.Null(cancelFailure);
        Assert.True(resumeReturned);
        Assert.True(cancelReturned);

        // Assert: The cancellation reached the resumed run. Cancelling the paused run's retired
        // source instead would leave the resumed segment untouched, so it would report Finished.
        Assert.Equal(EnGenResult.Cancelled, _session.Status().Result);
        Assert.Null(_session.World());
      }
      finally
      {
        _session.AfterClaimTestHook = null;

        // Act: Never leave a running state behind, whatever the outcome above was.
        if (_session.Status().Result == EnGenResult.Running)
        {
          _session.Cancel();
          WaitForTerminal(_session);
        }
      }
    }

    [Fact]
    public async Task Pause_DuringClaimWindowOfNextRun_PausesTheNewRun()
    {
      // Arrange: A paused run, so the previous run's generator is still installed when the next
      // run claims the state.
      Assert.True(_session.Start(EnRunKind.Step, Config(sleep: 0)));
      WaitForTerminal(_session);
      Assert.Equal(EnGenResult.Paused, _session.Status().Result);

      using var pauseReachedCall = new ManualResetEventSlim(false);
      var startReturned = false;
      var pauseReturned = false;
      Exception? pauseFailure = null;

      try
      {
        // Act: Hold the claim window open until the racer reaches its Pause call, then give it
        // time to execute Pause inside that window.
        _session.AfterClaimTestHook = () =>
        {
          pauseReachedCall.Wait(TimeSpan.FromSeconds(5));
          Thread.Sleep(20);
        };
        var pauseTask = Task.Run(() =>
        {
          // Wait until the claim of the new run makes the store report Running.
          var deadline = Stopwatch.StartNew();
          while (_session.Status().Result != EnGenResult.Running && deadline.Elapsed < TimeSpan.FromSeconds(5))
            Thread.Sleep(1);
          pauseReachedCall.Set();
          try { pauseReturned = _session.Pause(); }
          catch (Exception exception) { pauseFailure = exception; }
        });
        var startTask = Task.Run(() => startReturned = _session.Start(EnRunKind.All, Config(sleep: 2000)));

        // Act: Let both commands and the run they targeted settle.
        await Task.WhenAll(startTask, pauseTask);
        WaitForTerminal(_session);

        // Assert: Both calls reported success, and no command threw.
        Assert.Null(pauseFailure);
        Assert.True(startReturned);
        Assert.True(pauseReturned);

        // Assert: The accepted request stopped the new run. Acknowledging the previous run's dead
        // generator instead would leave this run untouched, so it would report Finished.
        Assert.Equal(EnGenResult.Paused, _session.Status().Result);
        Assert.True(_session.World()!.Stats.Steps < 10,
          $"Expected a partially stepped run, got {_session.World()!.Stats.Steps} steps.");
      }
      finally
      {
        _session.AfterClaimTestHook = null;

        // Act: Never leave a running state behind, whatever the outcome above was.
        if (_session.Status().Result == EnGenResult.Running)
        {
          _session.Cancel();
          WaitForTerminal(_session);
        }
      }
    }

    [Fact]
    public void Clear_ConcurrentWithSelfTerminatingRun_NeverPausesTheNextRun()
    {
      // Arrange: A run long enough that Clear observes it as Running and enters its pause path
      // (60 ms per step: still in flight when Clear checks, yet cancelled from the hook fast).
      Assert.True(_session.Start(EnRunKind.All, Config(sleep: 600)));
      Assert.Equal(EnGenResult.Running, _session.Status().Result);

      var cleared = false;
      var nextRunStarted = false;

      try
      {
        // Act: Inside Clear's window between capturing the claim and requesting the pause, the
        // observed run terminates by itself and the next run claims the state - so Clear no
        // longer owns the run it is about to pause.
        _session.BeforeClearPauseTestHook = () =>
        {
          Assert.True(_session.Cancel());
          WaitForTerminal(_session);
          nextRunStarted = _session.Start(EnRunKind.All, Config(sleep: 400));
        };

        cleared = _session.Clear(TimeSpan.FromSeconds(10));

        // Act: Let the run that claimed during Clear settle.
        WaitForTerminal(_session);

        // Assert: Clear refused because a newer run claimed the state it captured.
        Assert.True(nextRunStarted);
        Assert.False(cleared);

        // Assert: The newer run was never disturbed by the refused Clear: it ran its full step
        // budget instead of stopping at a pause nobody requested for it.
        Assert.Equal(EnGenResult.Finished, _session.Status().Result);
        Assert.Equal(10L, _session.World()!.Stats.Steps);
      }
      finally
      {
        _session.BeforeClearPauseTestHook = null;

        // Act: Never leave a running state behind, whatever the outcome above was.
        if (_session.Status().Result == EnGenResult.Running)
        {
          _session.Cancel();
          WaitForTerminal(_session);
        }
      }
    }

    [Fact]
    public void World_WhenNextRunClaimsDuringCall_ReturnsTheObservedRunsWorld()
    {
      // Arrange: A finished run whose world holds every completed step.
      Assert.True(_session.Start(EnRunKind.All, Config(sleep: 0)));
      WaitForTerminal(_session);
      Assert.Equal(EnGenResult.Finished, _session.Status().Result);

      try
      {
        // Act: Claim and install the next run between World()'s state sample and its return.
        // The next run sleeps long enough (100 ms per step) that its own world still shows zero
        // steps while the call is in flight, so the two worlds are distinguishable.
        _session.WorldSampleTestHook = () =>
          Assert.True(_session.Start(EnRunKind.All, Config(sleep: 1000)));

        var world = _session.World();

        // Assert: The returned world belongs to the run whose status the call observed (finished,
        // all steps), never to the run that claimed while the call was in flight (no steps yet).
        Assert.NotNull(world);
        Assert.Equal(10L, world!.Stats.Steps);
      }
      finally
      {
        _session.WorldSampleTestHook = null;

        // Act: Stop the long-running claim the hook started so the session is never left running.
        if (_session.Status().Result == EnGenResult.Running)
        {
          _session.Cancel();
          WaitForTerminal(_session);
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
        Assert.True(_session.Start(EnRunKind.All, Config(sleep: 0), cancellationTokenSource.Token));
        WaitForTerminal(_session);

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
            nextRunStarted = _session.Start(EnRunKind.All, Config(sleep: 0));
          }
          else
          {
            cleared = _session.Clear();
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
        WaitForTerminal(_session);
        if (_session.Status().Result != EnGenResult.None)
          Assert.True(_session.Clear());
      }

      // Assert: After the storm, a full run still starts and completes normally.
      Assert.True(_session.Start(EnRunKind.All, Config(sleep: 0)));
      WaitForTerminal(_session);
      Assert.Equal(EnGenResult.Finished, _session.Status().Result);
      Assert.NotNull(_session.World());
    }
  }
}
