using System;
using System.Threading;

using LostInLimbo.LevelGen.Data.Config;
using LostInLimbo.LevelGen.Data.Other;
using LostInLimbo.LevelGen.Data.Result;
using LostInLimbo.LevelGen.Internal.Status;

namespace LostInLimbo.LevelGen.Internal.Generator;

/// <summary>
/// <para>Main class managing entire generation and its passes. Should be run in its own thread.</para>
/// <para>Updates status during execution, so consumer can show progress.</para>
/// </summary>
/// <remarks>
/// NOTE: right now it is placeholder that pretends to do something: it advances a fixed number
/// of steps, each imitating work by sleeping (see <see cref="MainLoop"/>).
/// Mutates <see cref="World"/> in place on the worker thread; consumers must only read it
/// after status reports <see cref="EnGenResult.Finished"/>.
/// </remarks>
internal sealed class LevelGenerator
{
  /// <summary>Kind of run. Mutated only between runs via <see cref="SetRunKind"/>.</summary>
  private EnRunKind _enRunKind;
  /// <summary>Configuration data for level generation.</summary>
  private readonly CnfLevelGeneration _config;
  /// <summary>Shared, thread-safe status sink owned by the API layer.</summary>
  private readonly GenerationStatusStore _statusStore;

  /// <summary>Total number of placeholder steps of this generator's configuration.</summary>
  private int TotalPlaceholderSteps => _config.Debug?.PlaceholderSteps ?? CnfDebug.DefaultPlaceholderSteps;

  /// <summary>
  /// Result of generation. Placeholder: not populated yet. Will be built up in place
  /// by generation passes during <see cref="Execute"/>.
  /// </summary>
  public LilWorld World { get; } = new();

  /// <summary>
  /// Prepares level generator.
  /// </summary>
  /// <param name="enRunKind">Kind of run.</param>
  /// <param name="config">Configuration data for level generation.</param>
  /// <param name="statusStore">Store receiving status snapshots during execution.</param>
  /// <exception cref="ArgumentNullException">A parameter is <see langword="null"/>.</exception>
  /// <exception cref="ArgumentOutOfRangeException">A numeric config setting is out of range.</exception>
  /// <exception cref="ArgumentException">The configuration violates its own invariants.</exception>
  public LevelGenerator(EnRunKind enRunKind, CnfLevelGeneration config, GenerationStatusStore statusStore)
  {
    if (config is null) throw new ArgumentNullException(nameof(config));
    if (statusStore is null) throw new ArgumentNullException(nameof(statusStore));

    // Validate here so every construction path rejects bad config before a run is claimed.
    config.Validate();

    _enRunKind = enRunKind;
    _config = config;
    _statusStore = statusStore;
  }

  /// <summary>
  /// Sets the kind of the next run. Called by the API layer when resuming a paused generation
  /// with a different run kind than the original run used. Must only be called after the run
  /// has been claimed, so no other thread can be executing this generator at the time.
  /// </summary>
  /// <param name="enRunKind">Kind of run.</param>
  internal void SetRunKind(EnRunKind enRunKind)
  {
    _enRunKind = enRunKind;
  }

  /// <summary>
  /// Starts or resumes generation.
  /// </summary>
  /// <remarks>
  /// A caught exception does not propagate: this method is meant to run on a background thread,
  /// where an unhandled exception would terminate the host process. Instead the failure is
  /// reported through the status store as <see cref="EnGenResult.Error"/> with the exception
  /// captured in <see cref="GenerationStatus.Error"/>.
  /// <para>
  /// Cancellation is not a failure: when <paramref name="cancellationToken"/> is cancelled the run
  /// stops at its next checkpoint and reports <see cref="EnGenResult.Cancelled"/> with a
  /// <see langword="null"/> error instead.
  /// </para>
  /// </remarks>
  /// <param name="cancellationToken">
  /// Token governing this run. Checked before generation starts and then only at step
  /// boundaries - the sleep of a running placeholder step is never interrupted.
  /// </param>
  public void Execute(CancellationToken cancellationToken = default)
  {
    _statusStore.Publish(new GenerationStatus { Result = EnGenResult.Running });
    try
    {
      // First checkpoint: a token cancelled before the run got going ends it right away.
      cancellationToken.ThrowIfCancellationRequested();

      EnGenResult GenResult = RunGeneration(cancellationToken);
      _statusStore.Publish(new GenerationStatus { Result = GenResult }); // Finished or Paused here.
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
    {
      // Only our own token maps to Cancelled; an unrelated OperationCanceledException
      // still falls through to the generic handler below and reports an error.
      _statusStore.Publish(new GenerationStatus { Result = EnGenResult.Cancelled });
    }
    catch (Exception exception)
    {
      _statusStore.Publish(new GenerationStatus { Result = EnGenResult.Error, Error = exception });
    }
  }

  /// <summary>
  /// Placeholder for generation passes: advances the placeholder step loop, where every step
  /// imitates real work by sleeping. Number of steps comes from <see cref="CnfDebug.PlaceholderSteps"/>.
  /// </summary>
  /// <param name="cancellationToken">Token checked at every step boundary.</param>
  /// <returns>Generation result. Finished when all steps are done, otherwise Paused.</returns>
  private EnGenResult RunGeneration(CancellationToken cancellationToken)
  {
    if (_config.Debug?.FailAtStart == true) throw new Exception("Deliberate failure at start.");

    MainLoop(cancellationToken);

    // Complete generation means the level is done; otherwise a Step/Area run stopped early
    // and the run can be resumed to continue with the remaining steps.
    return IsGenerationComplete() ? EnGenResult.Finished : EnGenResult.Paused;
  }

  /// <summary>
  /// <para>Placeholder for generation passes. Advances generation by a bounded number of steps.</para>
  /// <para>
  /// Loops while generation is not complete yet (see <see cref="IsGenerationComplete"/>) and the
  /// run kind still allows more work: <c>EnRunKind.All</c> runs until generation completes, <c>Area</c>
  /// and <c>Step</c> run at most one step per execution.
  /// </para>
  /// <para>
  /// Each step first checks the token (the only cancellation checkpoint) and then sleeps for
  /// <c>PlaceholderSleep / PlaceholderSteps</c> milliseconds as an imitation of the work done in
  /// a single step. The sleep itself is a plain, uninterruptible <see cref="Thread.Sleep(int)"/>;
  /// cancellation is therefore observed only after the current step completes. Completed steps
  /// are counted in <see cref="Data.Result.Stats.LilStats.Steps"/> of <see cref="World"/>, which
  /// survives pausing and resuming because the API keeps this generator instance alive.
  /// </para>
  /// </summary>
  /// <param name="cancellationToken">Token checked at every step boundary.</param>
  private void MainLoop(CancellationToken cancellationToken)
  {
    var totalSteps = TotalPlaceholderSteps;
    var sleepMilliseconds = _config.Debug?.PlaceholderSleep ?? CnfDebug.DefaultPlaceholderSleep;
    var stepSleepMilliseconds = sleepMilliseconds / totalSteps;

    // Maximum number of steps this execution may add; real passes will budget work differently.
    var maxStepsThisRun = _enRunKind == EnRunKind.All ? int.MaxValue : 1;

    var stepsRun = 0;
    while (stepsRun < maxStepsThisRun && !IsGenerationComplete())
    {
      // Cancellation checkpoint: a token cancelled before the next step starts ends the run.
      cancellationToken.ThrowIfCancellationRequested();

      // Imitation of the work of a single step; deliberately not token-aware.
      Thread.Sleep(stepSleepMilliseconds);

      World.Stats.Steps++;
      stepsRun++;
    }
  }

  /// <summary>
  /// Placeholder for the real completion criterion. The real generator will use a more
  /// complex check (layout complete, all areas placed, ...) and will not know the total
  /// number of steps in advance.
  /// </summary>
  private bool IsGenerationComplete()
  {
    return World.Stats.Steps >= TotalPlaceholderSteps;
  }
}
