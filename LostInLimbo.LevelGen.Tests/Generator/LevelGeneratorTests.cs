using System.Diagnostics;

using LostInLimbo.LevelGen.Data.Config;
using LostInLimbo.LevelGen.Data.Other;
using LostInLimbo.LevelGen.Data.Result;
using LostInLimbo.LevelGen.Internal.Generator;
using LostInLimbo.LevelGen.Internal.Status;

namespace LostInLimbo.LevelGen.Tests.Generator;

/// <summary>
/// Tests level generator functionality.
/// </summary>
public sealed class LevelGeneratorTests
{
  /// <summary>Config with the placeholder sleep disabled so Execute-based tests run instantly.</summary>
  private static CnfLevelGeneration FastConfig()
  {
    return CnfLevelGeneration.Default with
    {
      Debug = new CnfDebug { PlaceholderSleep = 0 },
    };
  }

  // TESTS

  [Fact]
  public void Constructor_WithNullConfig_ThrowsArgumentNullException()
  {
    // Arrange: Valid run kind and store, but null config.
    var statusStore = new GenerationStatusStore();

    // Act: Constructing with null config.
    // Assert: ArgumentNullException is thrown naming the config parameter.
    var exception = Assert.Throws<ArgumentNullException>(
      () => new LevelGenerator(EnRunKind.All, null!, statusStore));
    Assert.Equal("config", exception.ParamName);
  }

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

  [Fact]
  public void Constructor_CreatesWorld()
  {
    // Arrange: Valid arguments.
    var statusStore = new GenerationStatusStore();

    // Act: Constructing the generator.
    var generator = new LevelGenerator(EnRunKind.All, CnfLevelGeneration.Default, statusStore);

    // Assert: A world instance exists, ready to be built up.
    Assert.NotNull(generator.World);
  }

  [Fact]
  public void Constructor_WithInvalidConfig_ThrowsArgumentOutOfRangeException()
  {
    // Arrange: Valid store and run kind, but config violating its own invariants.
    var statusStore = new GenerationStatusStore();
    var config = CnfLevelGeneration.Default with
    {
      General = new CnfGeneral { Corruption = -1.0f },
    };

    // Act: Constructing with an invalid config.
    // Assert: Validation rejects it before any run could be claimed.
    Assert.Throws<ArgumentOutOfRangeException>(
      () => new LevelGenerator(EnRunKind.All, config, statusStore));
  }

  [Fact]
  public void Execute_KeepsSameWorldInstance()
  {
    // Arrange: Generator and its world before running.
    var statusStore = new GenerationStatusStore();
    var generator = new LevelGenerator(EnRunKind.Step, FastConfig(), statusStore);
    LilWorld worldBefore = generator.World;

    // Act: Run generation to completion.
    generator.Execute();

    // Assert: World is mutated in place, never replaced.
    Assert.Same(worldBefore, generator.World);
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
  public void Execute_CancelDuringPlaceholderSleep_ReturnsBeforeSleepEnds()
  {
    // Arrange: A run that would sleep for 30 seconds if the wait ignored the token.
    const int SleepMilliseconds = 30_000;
    var statusStore = new GenerationStatusStore();
    Assert.True(statusStore.TryBeginRun());
    var config = CnfLevelGeneration.Default with
    {
      Debug = new CnfDebug { PlaceholderSleep = SleepMilliseconds },
    };
    var generator = new LevelGenerator(EnRunKind.All, config, statusStore);
    using var cancellationTokenSource = new CancellationTokenSource();

    // Act: Run generation on a worker thread and cancel while it is under way.
    var stopwatch = Stopwatch.StartNew();
    var worker = new Thread(() => generator.Execute(cancellationTokenSource.Token))
    {
      IsBackground = true,
      Name = "LevelGeneratorTests.CancelProbe",
    };
    worker.Start();
    Thread.Sleep(200); // Give the worker time to enter the placeholder wait.
    cancellationTokenSource.Cancel();
    var joined = worker.Join(TimeSpan.FromSeconds(5));
    stopwatch.Stop();

    // Assert: The placeholder wait woke up early instead of sleeping through the cancellation.
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
