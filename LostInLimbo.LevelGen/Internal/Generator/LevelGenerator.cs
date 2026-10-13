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
/// NOTE: right now it is placeholder that pretends to do something (sleeps).
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
  /// Token governing this run. Checked before any pass starts and awaited during placeholder
  /// work; future passes receive it as a parameter and check it at pass boundaries.
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
  /// Placeholder for generation passes. Will be replaced by real layout, validation and
  /// placement passes using <see cref="_enRunKind"/> and <see cref="_config"/>.
  /// </summary>
  /// <param name="cancellationToken">Cancellation token.</param>
  /// <returns>Generation result. Can be only Finished or Paused.</returns>
  private EnGenResult RunGeneration(CancellationToken cancellationToken)
  {
    if (_config.Debug?.FailAtStart == true) throw new Exception("Deliberate failure at start.");

    MainLoop(cancellationToken);

    // In future we will be able to finish even with run kind different than All.
    return _enRunKind == EnRunKind.All ? EnGenResult.Finished : EnGenResult.Paused;
  }

  /// <summary>
  /// Main loop of generator.
  /// </summary>
  /// <param name="cancellationToken">Cancellation token.</param>
  private void MainLoop(CancellationToken cancellationToken)
  {
    // Placeholder: pretend to work for the configured duration (debug-overridable, default 3 seconds).
    // Token-aware wait: wakes up as soon as cancellation is requested instead of sleeping through it.
    var sleepMilliseconds = _config.Debug?.PlaceholderSleep ?? CnfDebug.DefaultPlaceholderSleep;
    if (cancellationToken.WaitHandle.WaitOne(sleepMilliseconds))
      cancellationToken.ThrowIfCancellationRequested();    
  }
}
