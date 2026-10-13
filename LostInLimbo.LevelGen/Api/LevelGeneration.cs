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

  /// <summary>Generator instance owned by the current or last run.</summary>
  private static LevelGenerator? _generator;

  /// <summary>
  /// Begin level generation based on given config. Will discard all and any partial data,
  /// including the world built by any previous run.
  /// </summary>
  /// <param name="enRunKind">Kind of run.</param>
  /// <param name="config">Configuration data for level generation.</param>
  /// <returns>True if successfully started generation, otherwise false.</returns>
  public static bool Start(EnRunKind enRunKind, CnfLevelGeneration config)
  {
    // Construct before claiming the run so configuration errors cannot leave status stuck at "running".
    var generator = new LevelGenerator(enRunKind, config, _statusStore);
    if (!_statusStore.TryBeginRun()) return false;

    _generator = generator;
    RunOnNewThread(generator);
    return true;
  }

  /// <summary>
  /// Continue level generation. Works only if generation is paused (run kind was not All).
  /// </summary>
  /// <param name="enRunKind">Kind of run.</param>
  /// <returns>True if successfully resumed generation, otherwise false.</returns>
  public static bool Continue(EnRunKind enRunKind)
  {
    if (_statusStore.Current.Result != EnGenResult.Paused) return false;
    _generator ??= new LevelGenerator(enRunKind, CnfLevelGeneration.Default, _statusStore);
    if (!_statusStore.TryBeginRun()) return false;

    RunOnNewThread(_generator);
    return true;
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
  /// The world is valid to consume only when <see cref="Status"/> reports
  /// <see cref="EnGenResult.Finished"/>; after <see cref="EnGenResult.Error"/> it is partial
  /// and must not be used. The generator mutates it in place on the worker thread, but
  /// observing the finished status snapshot guarantees the final data is visible to the caller.
  /// </remarks>
  /// <returns>
  /// World of the current or last run; <see langword="null"/> if generation was never started.
  /// </returns>
  public static LilWorld? World()
  {
    return _generator?.World;
  }

  /// <summary>
  /// Runs <see cref="LevelGenerator.Execute"/> on a dedicated background thread so that
  /// callers of <see cref="Start"/> or <see cref="Continue"/> return immediately.
  /// Background thread will not keep the process alive on shutdown.
  /// </summary>
  /// <param name="generator">Generator to execute.</param>
  private static void RunOnNewThread(LevelGenerator generator)
  {
    var thread = new Thread(generator.Execute)
    {
      IsBackground = true,
      Name = "LostInLimbo.LevelGenerator",
    };
    thread.Start();
  }
}
