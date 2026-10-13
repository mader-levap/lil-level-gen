using System;
using System.Threading;
using LostInLimbo.LevelGen.Data.Config;
using LostInLimbo.LevelGen.Data.Other;
using LostInLimbo.LevelGen.Data.Result;
using LostInLimbo.LevelGen.Internal.Generator;
using LostInLimbo.LevelGen.Internal.Status;

namespace LostInLimbo.LevelGen.Api;

/// <summary>
/// <para>Entry point for entire level generator. Serves as layer between consumer and internals. There can be only one generation running at once.</para>
/// <para>All jobs here are run asynchronously. Therefore, all methods return immediately.</para>
/// </summary>
public static class LevelGeneration
{
  /// <summary>
  /// Single source of truth for generation status. Owned here; generators receive it
  /// and publish their transitions from the worker thread.
  /// </summary>
  private static readonly GenerationStatusStore _statusStore = new();

  /// <summary>Generator instance owned by the current or last run. Volatile so <see cref="World"/> sees the latest assignment from any thread.</summary>
  private static volatile LevelGenerator? _generator;

  /// <summary>
  /// Begin level generation based on given config. Will discard all and any partial data,
  /// including the world built by any previous run.
  /// </summary>
  /// <param name="enRunKind">Kind of run.</param>
  /// <param name="config">Configuration data for level generation.</param>
  /// <returns>True if successfully started generation, otherwise false.</returns>
  /// <exception cref="ArgumentNullException">Config is <see langword="null"/>.</exception>
  /// <exception cref="ArgumentOutOfRangeException">A numeric config setting is out of range.</exception>
  /// <exception cref="ArgumentException">The configuration violates its own invariants.</exception>
  public static bool Start(EnRunKind enRunKind, CnfLevelGeneration config)
  {
    // Construct before claiming the run so configuration errors cannot leave status stuck at "running".
    var generator = new LevelGenerator(enRunKind, config, _statusStore);
    if (!_statusStore.TryBeginRun()) return false;

    _generator = generator;
    return TryRunOnNewThread(generator);
  }

  /// <summary>
  /// Resume level generation, applying the given run kind to the next segment.
  /// Works only if generation is paused (previous run kind was not All).
  /// </summary>
  /// <param name="enRunKind">Kind of run.</param>
  /// <returns>True if successfully resumed generation, otherwise false.</returns>
  public static bool Resume(EnRunKind enRunKind)
  {
    // Capture-or-construct before claiming so constructor errors cannot leave status stuck at "running".
    var generator = _generator ?? new LevelGenerator(enRunKind, CnfLevelGeneration.Default, _statusStore);
    if (!_statusStore.TryBeginResume()) return false;

    // The run is claimed, so no worker can be executing this generator yet: safe to update the kind
    // before Thread.Start releases it to the worker.
    generator.SetRunKind(enRunKind);
    _generator = generator;
    return TryRunOnNewThread(generator);
  }

  /// <summary>
  /// Get status of generation.
  /// </summary>
  /// <returns>Status of generation.</returns>
  public static GenerationStatus Status()
  {
    return _statusStore.Current;
  }

  /// <summary>
  /// Get the world built by the current or last run.
  /// </summary>
  /// <remarks>
  /// The generator mutates the world in place on the worker thread while generation is
  /// <see cref="EnGenResult.Running"/>, so this method returns it only for stable states:
  /// <see cref="EnGenResult.Paused"/> (partial results of an Area or Step run) and
  /// <see cref="EnGenResult.Finished"/> (complete world). It returns <see langword="null"/>
  /// while running, after <see cref="EnGenResult.Error"/> (partial world must not be used),
  /// and before the first run. Observing a stable status snapshot guarantees the final data
  /// is visible to the caller.
  /// </remarks>
  /// <returns>
  /// World of the current or last run when its state is stable, otherwise <see langword="null"/>.
  /// </returns>
  public static LilWorld? World()
  {
    var result = _statusStore.Current.Result;
    return result == EnGenResult.Paused || result == EnGenResult.Finished
      ? _generator?.World
      : null;
  }

  /// <summary>
  /// Test support: restores the pristine never-ran state - status back to
  /// <see cref="EnGenResult.None"/> with no error, and no generator, so <see cref="World"/>
  /// returns <see langword="null"/>. Not part of the public API; only test assemblies see
  /// it through <c>InternalsVisibleTo</c>.
  /// </summary>
  /// <exception cref="InvalidOperationException">A run is currently in progress.</exception>
  internal static void Reset()
  {
    // A worker only exists while the status is Running; resetting under it would let the
    // worker publish its terminal state over the fresh snapshot.
    if (_statusStore.Current.Result == EnGenResult.Running)
      throw new InvalidOperationException("Cannot reset while generation is running.");

    _statusStore.Publish(GenerationStatus.Default);
    _generator = null;
  }

  /// <summary>
  /// Runs <see cref="LevelGenerator.Execute"/> on a dedicated background thread so that
  /// callers of <see cref="Start"/> or <see cref="Resume"/> return immediately.
  /// Background thread will not keep the process alive on shutdown.
  /// </summary>
  /// <remarks>
  /// If the thread cannot be created or started after the run was already claimed, the failure
  /// is published as <see cref="EnGenResult.Error"/> with the exception captured in
  /// <see cref="GenerationStatus.Error"/> and this method returns <see langword="false"/>.
  /// Status therefore can never remain stuck at <see cref="EnGenResult.Running"/> because of
  /// a launch failure.
  /// </remarks>
  /// <param name="generator">Generator to execute.</param>
  /// <returns>
  /// <see langword="true"/> if the worker thread was started;
  /// <see langword="false"/> if launching failed (status reports the error).
  /// </returns>
  private static bool TryRunOnNewThread(LevelGenerator generator)
  {
    try
    {
      var thread = new Thread(generator.Execute)
      {
        IsBackground = true,
        Name = "LostInLimbo.LevelGenerator",
      };
      thread.Start();
      return true;
    }
    catch (Exception exception)
    {
      _statusStore.Publish(new GenerationStatus { Result = EnGenResult.Error, Error = exception });
      return false;
    }
  }
}
