using System.Diagnostics;

using LostInLimbo.LevelGen.Data.Config;
using LostInLimbo.LevelGen.Data.Other;
using LostInLimbo.LevelGen.Data.Result;
using LostInLimbo.LevelGen.Data.Result.Stats;
using LostInLimbo.LevelGen.Internal.Generator;
using LostInLimbo.LevelGen.Internal.Status;

namespace LostInLimbo.LevelGen.Tests.Generator;

/// <summary>
/// Tests level generator functionality, grouped into suites by concern: construction,
/// Execute semantics, failure/cancellation and pause requests.
/// </summary>
/// <remarks>
/// The pause and cancellation suites spawn worker threads with timing margins, so every
/// suite is pinned to the <c>LevelGeneratorTests</c> xunit collection: the suites never
/// run in parallel with each other, matching the sequential behavior of the original
/// single-class layout.
/// </remarks>
public sealed class LevelGeneratorTests
{
  /// <summary>
  /// Config with the placeholder sleep disabled so Execute-based tests run instantly,
  /// and with a configurable number of placeholder steps (default from <see cref="CnfDebug"/>).
  /// </summary>
  private static CnfLevelGeneration FastConfig(int placeholderSteps = CnfDebug.DefaultPlaceholderSteps)
  {
    return CnfLevelGeneration.Default with
    {
      Debug = new CnfDebug { PlaceholderSleep = 0, PlaceholderSteps = placeholderSteps },
    };
  }

  /// <summary>
  /// Reads the world paired with the store's current snapshot, which exists once a run
  /// published a paused or finished terminal state. The generator keeps its world private,
  /// so the store is the only observation point for tests. Fails the test when the
  /// snapshot does not carry a world.
  /// </summary>
  /// <param name="statusStore">Store whose snapshot carries the published world.</param>
  /// <returns>The published world.</returns>
  private static LilWorld PublishedWorld(GenerationStatusStore statusStore)
  {
    // Assert: A paused or finished run always publishes its world with the status.
    var world = statusStore.CurrentSnapshot.World;
    Assert.NotNull(world);
    return world;
  }

  /// <summary>Tests <see cref="LevelGenerator"/> construction: argument validation.</summary>
  [Collection("LevelGeneratorTests")]
  public sealed class ConstructionTests
  {
    [Fact]
    public void Constructor_WithNullStatusStore_ThrowsArgumentNullException()
    {
      // Arrange: Valid run kind and config, but null store.
      var config = CnfLevelGeneration.Default;

      // Act: Constructing with null store.
      // Assert: ArgumentNullException is thrown naming the statusStore parameter.
      var exception = Assert.Throws<ArgumentNullException>(
        () => new LevelGenerator(EnRunKind.All, config, null!));
      Assert.Equal("statusStore", exception.ParamName);
    }
  }

  /// <summary>Tests <see cref="LevelGenerator.Execute"/> semantics: run kinds, step counts, world mutation and run-kind switching.</summary>
  [Collection("LevelGeneratorTests")]
  public sealed class ExecuteTests
  {
    [Fact]
    public void Execute_KeepsSameWorldInstance()
    {
      // Arrange: Generator and a first run paused after one step, so its world is published.
      var statusStore = new GenerationStatusStore();
      Assert.True(statusStore.TryBeginRun());
      var generator = new LevelGenerator(EnRunKind.Step, FastConfig(), statusStore);
      generator.Execute();
      Assert.Equal(EnGenResult.Paused, statusStore.Current.Result);
      LilWorld worldBefore = PublishedWorld(statusStore);

      // Act: Claim and run the remainder to completion the way Resume does.
      Assert.True(statusStore.TryBeginRun());
      generator.SetRunKind(EnRunKind.All);
      generator.Execute();

      // Assert: World is mutated in place, never replaced across pause and resume.
      Assert.Equal(EnGenResult.Finished, statusStore.Current.Result);
      Assert.Same(worldBefore, PublishedWorld(statusStore));
    }

    [Fact]
    public void Execute_WithAllRunKind_PublishesFinished()
    {
      // Arrange: Generator running the whole level at once, without placeholder sleep.
      var statusStore = new GenerationStatusStore();
      Assert.True(statusStore.TryBeginRun());
      var generator = new LevelGenerator(EnRunKind.All, FastConfig(), statusStore);

      // Act: Run generation to completion.
      generator.Execute();

      // Assert: Whole-level run finishes successfully.
      Assert.Equal(EnGenResult.Finished, statusStore.Current.Result);
      Assert.Null(statusStore.Current.Error);
    }

    [Fact]
    public void SetRunKind_FromStepToAll_ThenExecute_PublishesFinished()
    {
      // Arrange: Generator originally created as a stepwise run, run already claimed.
      var statusStore = new GenerationStatusStore();
      Assert.True(statusStore.TryBeginRun());
      var generator = new LevelGenerator(EnRunKind.Step, FastConfig(), statusStore);

      // Act: Upgrade the kind the way Resume does, then execute.
      generator.SetRunKind(EnRunKind.All);
      generator.Execute();

      // Assert: The updated run kind is honored.
      Assert.Equal(EnGenResult.Finished, statusStore.Current.Result);
      Assert.Null(statusStore.Current.Error);
    }

    [Fact]
    public void SetRunKind_FromAllToStep_ThenExecute_PublishesPaused()
    {
      // Arrange: Generator originally created as a full run, run already claimed.
      var statusStore = new GenerationStatusStore();
      Assert.True(statusStore.TryBeginRun());
      var generator = new LevelGenerator(EnRunKind.All, FastConfig(), statusStore);

      // Act: Downgrade the kind to a single step, then execute.
      generator.SetRunKind(EnRunKind.Step);
      generator.Execute();

      // Assert: The updated run kind is honored.
      Assert.Equal(EnGenResult.Paused, statusStore.Current.Result);
      Assert.Null(statusStore.Current.Error);
    }

    [Fact]
    public void Execute_AfterClaimedRun_PublishesPaused()
    {
      // Arrange: Store already claims a run is in progress (as Start does before launching
      // the thread) and a generator writing into it. Placeholder sleep is disabled.
      var statusStore = new GenerationStatusStore();
      Assert.True(statusStore.TryBeginRun());
      var generator = new LevelGenerator(EnRunKind.Step, FastConfig(), statusStore);

      // Act: Run generation to completion.
      generator.Execute();

      // Assert: Status reports paused and no error.
      Assert.Equal(EnGenResult.Paused, statusStore.Current.Result);
      Assert.Null(statusStore.Current.Error);
    }

    [Fact]
    public void Execute_WithStepKind_RunsExactlyOneStep()
    {
      // Arrange: Claimed stepwise run without placeholder sleep.
      var statusStore = new GenerationStatusStore();
      Assert.True(statusStore.TryBeginRun());
      var generator = new LevelGenerator(EnRunKind.Step, FastConfig(), statusStore);

      // Act: Run a single execution.
      generator.Execute();

      // Assert: Exactly one step was made and the run pauses with the rest still pending.
      Assert.Equal(EnGenResult.Paused, statusStore.Current.Result);
      Assert.Equal(1L, PublishedWorld(statusStore).Stats.Steps);
    }

    [Fact]
    public void Execute_WithAreaKind_RunsExactlyOneStep()
    {
      // Arrange: Claimed area run; areas do not exist yet, so Area advances like Step.
      var statusStore = new GenerationStatusStore();
      Assert.True(statusStore.TryBeginRun());
      var generator = new LevelGenerator(EnRunKind.Area, FastConfig(), statusStore);

      // Act: Run a single execution.
      generator.Execute();

      // Assert: Exactly one step was made and the run pauses with the rest still pending.
      Assert.Equal(EnGenResult.Paused, statusStore.Current.Result);
      Assert.Equal(1L, PublishedWorld(statusStore).Stats.Steps);
    }

    [Fact]
    public void Execute_WithAllRunKind_RunsAllSteps()
    {
      // Arrange: Claimed full run over a three-step placeholder level.
      var statusStore = new GenerationStatusStore();
      Assert.True(statusStore.TryBeginRun());
      var generator = new LevelGenerator(EnRunKind.All, FastConfig(placeholderSteps: 3), statusStore);

      // Act: Run generation to completion.
      generator.Execute();

      // Assert: Every step was made and the run finished.
      Assert.Equal(EnGenResult.Finished, statusStore.Current.Result);
      Assert.Equal(3L, PublishedWorld(statusStore).Stats.Steps);
    }

    [Fact]
    public void Execute_WithStepKind_RepeatedlyEventuallyFinishes()
    {
      // Arrange: Claimed stepwise run over a three-step placeholder level.
      var statusStore = new GenerationStatusStore();
      Assert.True(statusStore.TryBeginRun());
      var generator = new LevelGenerator(EnRunKind.Step, FastConfig(placeholderSteps: 3), statusStore);

      // Act: Execute one step at a time until no step remains (guard against an endless pause).
      var executions = 0;
      while (statusStore.Current.Result != EnGenResult.Finished && executions < 10)
      {
        generator.Execute();
        executions++;
      }

      // Assert: Three steps total, so the third execution finished the level.
      Assert.Equal(EnGenResult.Finished, statusStore.Current.Result);
      Assert.Equal(3, executions);
      Assert.Equal(3L, PublishedWorld(statusStore).Stats.Steps);
    }

    [Fact]
    public void Execute_WithAllRunKind_AdvancesPhaseFromGraphToMap()
    {
      // Arrange: Claimed full run over a fresh world, which starts in the graph phase.
      var statusStore = new GenerationStatusStore();
      Assert.True(statusStore.TryBeginRun());
      var generator = new LevelGenerator(EnRunKind.All, FastConfig(), statusStore);

      // Act: Run generation to completion.
      generator.Execute();

      // Assert: The successful graph pass moved the world into the map phase.
      Assert.Equal(EnGenResult.Finished, statusStore.Current.Result);
      Assert.Equal(EnPhase.Map, PublishedWorld(statusStore).Phase);
    }

    [Fact]
    public void Execute_AfterPausedStepRun_AdvancesOnlyMapPhaseOnResume()
    {
      // Arrange: A stepwise run whose first execution covers the graph pass plus one map step.
      var statusStore = new GenerationStatusStore();
      Assert.True(statusStore.TryBeginRun());
      var generator = new LevelGenerator(EnRunKind.Step, FastConfig(placeholderSteps: 3), statusStore);
      generator.Execute();
      Assert.Equal(EnGenResult.Paused, statusStore.Current.Result);
      LilWorld partialWorld = PublishedWorld(statusStore);
      Assert.Equal(1L, partialWorld.Stats.Steps);
      Assert.Equal(EnPhase.Map, partialWorld.Phase);

      // Act: Claim and run the remainder as a full run the way Resume does.
      Assert.True(statusStore.TryBeginRun());
      generator.SetRunKind(EnRunKind.All);
      generator.Execute();

      // Assert: The resumed run stays in the map phase; the graph pass ran only once.
      Assert.Equal(EnGenResult.Finished, statusStore.Current.Result);
      LilWorld finishedWorld = PublishedWorld(statusStore);
      Assert.Equal(EnPhase.Map, finishedWorld.Phase);
      Assert.Equal(3L, finishedWorld.Stats.Steps);
    }
  }

  /// <summary>Tests <see cref="LevelGenerator.Execute"/> failure reporting and cancellation-token handling.</summary>
  [Collection("LevelGeneratorTests")]
  public sealed class FailureAndCancellationTests
  {
    [Fact]
    public void Execute_WithFailAtStartDebugConfig_PublishesError()
    {
      // Arrange: Generator configured to fail immediately at start.
      var statusStore = new GenerationStatusStore();
      var config = CnfLevelGeneration.Default with
      {
        Debug = new CnfDebug { FailAtStart = true },
      };
      var generator = new LevelGenerator(EnRunKind.Step, config, statusStore);

      // Act: Run generation. The exception must not propagate; it is reported via status.
      generator.Execute();

      // Assert: Status reports error carrying the deliberate failure, and is not stuck running.
      Assert.Equal(EnGenResult.Error, statusStore.Current.Result);
      var error = Assert.IsType<Exception>(statusStore.Current.Error);
      Assert.Equal("Deliberate failure at start.", error.Message);
    }

    [Fact]
    public void Execute_WithPreCancelledToken_PublishesCancelledWithoutError()
    {
      // Arrange: A claimed run and a token cancelled before Execute is entered.
      var statusStore = new GenerationStatusStore();
      Assert.True(statusStore.TryBeginRun());
      var generator = new LevelGenerator(EnRunKind.All, FastConfig(), statusStore);
      using var cancellationTokenSource = new CancellationTokenSource();
      cancellationTokenSource.Cancel();

      // Act: Run generation with the already cancelled token.
      generator.Execute(cancellationTokenSource.Token);

      // Assert: The first checkpoint stops the run before any pass and reports cancellation.
      Assert.Equal(EnGenResult.Cancelled, statusStore.Current.Result);
      Assert.Null(statusStore.Current.Error);
    }

    [Fact]
    public void Execute_WithPreCancelledTokenAndFailAtStart_PublishesCancelledNotError()
    {
      // Arrange: Token already cancelled and debug config that would fail at start.
      var statusStore = new GenerationStatusStore();
      Assert.True(statusStore.TryBeginRun());
      var config = CnfLevelGeneration.Default with
      {
        Debug = new CnfDebug { FailAtStart = true, PlaceholderSleep = 0 },
      };
      var generator = new LevelGenerator(EnRunKind.All, config, statusStore);
      using var cancellationTokenSource = new CancellationTokenSource();
      cancellationTokenSource.Cancel();

      // Act: Run generation with the cancelled token.
      generator.Execute(cancellationTokenSource.Token);

      // Assert: The cancellation checkpoint runs before any pass, so cancellation wins.
      Assert.Equal(EnGenResult.Cancelled, statusStore.Current.Result);
      Assert.Null(statusStore.Current.Error);
    }

    [Fact]
    public void Execute_CancelDuringStep_ReturnsAtNextStepBoundary()
    {
      // Arrange: A run whose placeholder steps take 100 ms each (10 000 ms over 100 steps),
      // so sleeping through the whole placeholder duration would far outlast the join window.
      const int SleepMilliseconds = 10_000;
      var statusStore = new GenerationStatusStore();
      Assert.True(statusStore.TryBeginRun());
      var config = CnfLevelGeneration.Default with
      {
        Debug = new CnfDebug { PlaceholderSleep = SleepMilliseconds, PlaceholderSteps = 100 },
      };
      var generator = new LevelGenerator(EnRunKind.All, config, statusStore);
      using var cancellationTokenSource = new CancellationTokenSource();

      // Act: Run generation on a worker thread and cancel while a step is under way.
      var stopwatch = Stopwatch.StartNew();
      var worker = new Thread(() => generator.Execute(cancellationTokenSource.Token))
      {
        IsBackground = true,
        Name = "LevelGeneratorTests.CancelProbe",
      };
      worker.Start();
      Thread.Sleep(200); // Give the worker time to enter the placeholder step.
      cancellationTokenSource.Cancel();
      var joined = worker.Join(TimeSpan.FromSeconds(5));
      stopwatch.Stop();

      // Assert: The run stopped at the next step boundary instead of sleeping through the rest.
      Assert.True(joined, $"Generator did not react to cancellation within 5 s (slept {stopwatch.Elapsed}).");
      Assert.True(stopwatch.Elapsed < TimeSpan.FromMilliseconds(SleepMilliseconds),
        $"Generator ran for {stopwatch.Elapsed}, expected well below the {SleepMilliseconds} ms placeholder sleep.");
      Assert.Equal(EnGenResult.Cancelled, statusStore.Current.Result);
      Assert.Null(statusStore.Current.Error);
    }

    [Fact]
    public void Execute_WithUncancelledToken_CompletesNormally()
    {
      // Arrange: Claimed run with a live (never cancelled) token.
      var statusStore = new GenerationStatusStore();
      Assert.True(statusStore.TryBeginRun());
      var generator = new LevelGenerator(EnRunKind.All, FastConfig(), statusStore);
      using var cancellationTokenSource = new CancellationTokenSource();

      // Act: Run generation to completion with the token attached.
      generator.Execute(cancellationTokenSource.Token);

      // Assert: A token nobody cancels has no effect on the outcome.
      Assert.Equal(EnGenResult.Finished, statusStore.Current.Result);
      Assert.Null(statusStore.Current.Error);
    }
  }

  /// <summary>Tests <see cref="LevelGenerator.TryRequestPause"/>: accepted pauses, boundary behavior and terminal-state refusals.</summary>
  [Collection("LevelGeneratorTests")]
  public sealed class PauseTests
  {
    [Fact]
    public void TryRequestPause_BeforeExecute_PublishesPausedWithZeroSteps()
    {
      // Arrange: A claimed run whose generator is asked to pause before any work happens.
      var statusStore = new GenerationStatusStore();
      Assert.True(statusStore.TryBeginRun());
      var generator = new LevelGenerator(EnRunKind.All, FastConfig(), statusStore);

      // Act: Request the pause, then run the claimed execution.
      var accepted = generator.TryRequestPause();
      generator.Execute();

      // Assert: The request was accepted and the run stopped before its first step.
      Assert.True(accepted);
      Assert.Equal(EnGenResult.Paused, statusStore.Current.Result);
      Assert.Null(statusStore.Current.Error);
      Assert.Equal(0L, PublishedWorld(statusStore).Stats.Steps);
    }

    [Fact]
    public void TryRequestPause_BeforeExecute_KeepsPhaseAtGraph()
    {
      // Arrange: A claimed run asked to pause before it touches any generation pass.
      var statusStore = new GenerationStatusStore();
      Assert.True(statusStore.TryBeginRun());
      var generator = new LevelGenerator(EnRunKind.All, FastConfig(), statusStore);

      // Act: Request the pause, then run the claimed execution.
      Assert.True(generator.TryRequestPause());
      generator.Execute();

      // Assert: The graph pass never ran, so the world stays in its initial phase.
      Assert.Equal(EnGenResult.Paused, statusStore.Current.Result);
      Assert.Equal(EnPhase.Prepass, PublishedWorld(statusStore).Phase);
    }

    [Fact]
    public void TryRequestPause_DuringStep_StopsAtNextStepBoundary()
    {
      // Arrange: A run whose placeholder steps take 100 ms each (10 000 ms over 100 steps),
      // so sleeping through the whole placeholder duration would far outlast the join window.
      const int SleepMilliseconds = 10_000;
      var statusStore = new GenerationStatusStore();
      Assert.True(statusStore.TryBeginRun());
      var config = CnfLevelGeneration.Default with
      {
        Debug = new CnfDebug { PlaceholderSleep = SleepMilliseconds, PlaceholderSteps = 100 },
      };
      var generator = new LevelGenerator(EnRunKind.All, config, statusStore);

      // Act: Run generation on a worker thread and pause while a step is under way.
      var stopwatch = Stopwatch.StartNew();
      var worker = new Thread(() => generator.Execute()) { IsBackground = true, Name = "LevelGeneratorTests.PauseProbe" };
      worker.Start();
      Thread.Sleep(200); // Give the worker time to enter the placeholder step.
      var accepted = generator.TryRequestPause();
      var joined = worker.Join(TimeSpan.FromSeconds(5));
      stopwatch.Stop();

      // Assert: The request was accepted for the in-flight run.
      Assert.True(accepted);

      // Assert: The run stopped at the next step boundary instead of sleeping through the rest.
      Assert.True(joined, $"Generator did not react to pause within 5 s (slept {stopwatch.Elapsed}).");
      Assert.True(stopwatch.Elapsed < TimeSpan.FromMilliseconds(SleepMilliseconds),
        $"Generator ran for {stopwatch.Elapsed}, expected well below the {SleepMilliseconds} ms placeholder sleep.");
      Assert.Equal(EnGenResult.Paused, statusStore.Current.Result);
      Assert.Null(statusStore.Current.Error);
      var partialWorld = PublishedWorld(statusStore);
      Assert.True(partialWorld.Stats.Steps > 0 && partialWorld.Stats.Steps < 10,
        $"Expected a partial step count, got {partialWorld.Stats.Steps}.");
    }

    [Fact]
    public void TryRequestPause_OnFinalStep_PublishesFinished()
    {
      // Arrange: A single-step run taking 400 ms; pausing while that step sleeps means the
      // step completes before the pause can be observed, so completion wins over the pause.
      const int SleepMilliseconds = 400;
      var statusStore = new GenerationStatusStore();
      Assert.True(statusStore.TryBeginRun());
      var config = CnfLevelGeneration.Default with
      {
        Debug = new CnfDebug { PlaceholderSleep = SleepMilliseconds, PlaceholderSteps = 1 },
      };
      var generator = new LevelGenerator(EnRunKind.All, config, statusStore);

      // Act: Run generation on a worker thread and pause while the only step is under way.
      var worker = new Thread(() => generator.Execute()) { IsBackground = true, Name = "LevelGeneratorTests.PauseFinalStepProbe" };
      worker.Start();
      Thread.Sleep(100); // The worker is now inside the single step's sleep.
      var accepted = generator.TryRequestPause();
      var joined = worker.Join(TimeSpan.FromSeconds(5));

      // Assert: The request was accepted, but the in-flight step completed the level anyway.
      Assert.True(accepted);
      Assert.True(joined, "Generator did not finish within 5 s.");
      Assert.Equal(EnGenResult.Finished, statusStore.Current.Result);
      Assert.Null(statusStore.Current.Error);
      Assert.Equal(1L, PublishedWorld(statusStore).Stats.Steps);
    }

    [Fact]
    public void TryRequestPause_WithPreCancelledToken_PublishesPausedNotCancelled()
    {
      // Arrange: A pause requested before the run gets going and a token that is also cancelled.
      var statusStore = new GenerationStatusStore();
      Assert.True(statusStore.TryBeginRun());
      var generator = new LevelGenerator(EnRunKind.All, FastConfig(), statusStore);
      using var cancellationTokenSource = new CancellationTokenSource();
      cancellationTokenSource.Cancel();
      Assert.True(generator.TryRequestPause());

      // Act: Run generation; both the pause and the cancellation are pending at the first checkpoint.
      generator.Execute(cancellationTokenSource.Token);

      // Assert: Pause has precedence over cancellation, so the run pauses instead of cancelling.
      Assert.Equal(EnGenResult.Paused, statusStore.Current.Result);
      Assert.Null(statusStore.Current.Error);
      Assert.Equal(0L, PublishedWorld(statusStore).Stats.Steps);
    }

    [Fact]
    public void TryRequestPause_AfterFinished_ReturnsFalse()
    {
      // Arrange: A run that already completed all of its steps.
      var statusStore = new GenerationStatusStore();
      Assert.True(statusStore.TryBeginRun());
      var generator = new LevelGenerator(EnRunKind.All, FastConfig(), statusStore);
      generator.Execute();
      Assert.Equal(EnGenResult.Finished, statusStore.Current.Result);

      // Act: Attempting to pause the finished run.
      var accepted = generator.TryRequestPause();

      // Assert: There is nothing left to pause, and the terminal state is not disturbed.
      Assert.False(accepted);
      Assert.Equal(EnGenResult.Finished, statusStore.Current.Result);
    }

    [Fact]
    public void TryRequestPause_AfterCancelledRun_ReturnsFalse()
    {
      // Arrange: A run that already reported cancellation.
      var statusStore = new GenerationStatusStore();
      Assert.True(statusStore.TryBeginRun());
      var generator = new LevelGenerator(EnRunKind.All, FastConfig(), statusStore);
      using var cancellationTokenSource = new CancellationTokenSource();
      cancellationTokenSource.Cancel();
      generator.Execute(cancellationTokenSource.Token);
      Assert.Equal(EnGenResult.Cancelled, statusStore.Current.Result);

      // Act: Attempting to pause the cancelled run.
      var accepted = generator.TryRequestPause();

      // Assert: Cancelled is terminal, so there is nothing left to pause.
      Assert.False(accepted);
      Assert.Equal(EnGenResult.Cancelled, statusStore.Current.Result);
    }

    [Fact]
    public void Execute_AfterPauseRequestedRun_RunsToCompletion()
    {
      // Arrange: A run that was paused through an explicit request.
      var statusStore = new GenerationStatusStore();
      Assert.True(statusStore.TryBeginRun());
      var generator = new LevelGenerator(EnRunKind.All, FastConfig(placeholderSteps: 3), statusStore);
      Assert.True(generator.TryRequestPause());
      generator.Execute();
      Assert.Equal(EnGenResult.Paused, statusStore.Current.Result);
      Assert.Equal(0L, PublishedWorld(statusStore).Stats.Steps);

      // Act: Claim and execute the next run the way Resume does.
      Assert.True(statusStore.TryBeginRun());
      generator.Execute();

      // Assert: The consumed pause request does not leak into the next run, which finishes.
      Assert.Equal(EnGenResult.Finished, statusStore.Current.Result);
      Assert.Equal(3L, PublishedWorld(statusStore).Stats.Steps);
    }
  }
}
