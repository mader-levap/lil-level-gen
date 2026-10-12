using System.Threading;
using LostInLimbo.LevelGen.Data.Config;
using LostInLimbo.LevelGen.Data.Other;

namespace LostInLimbo.LevelGen.Api;

/// <summary>
/// Entry point for entire level generator.
/// </summary>
/// <remarks>
/// It is advisable to run it in separate thread.
/// </remarks>
public static class LevelGeneration
{
  /// <summary>
  /// Current status snapshot. Replaced as a whole (never mutated) so that
  /// <see cref="Status"/> can be polled from another thread safely.
  /// </summary>
  private static GenerationStatus _status = GenerationStatus.Default;

  /// <summary>
  /// Begin level generation based on given config. Will discard all and any partial data.
  /// Will return immediately if level generation is in progress.
  /// </summary>
  /// <param name="enRunKind">Kind of run.</param>
  /// <param name="config">Configuration data for level generation.</param>
  /// <returns>True if there is still something to do, otherwise false.</returns>
  public static bool Start(EnRunKind enRunKind, CnfLevelGeneration config)
  {
    if (_status.Running) return true;
    Volatile.Write(ref _status, new GenerationStatus { Running = true });

    // TODO Placeholder

    Volatile.Write(ref _status, GenerationStatus.Default);
    return false;
  }

  /// <summary>
  /// Continue level generation, if not yet finished.
  /// Will return immediately if level generation is in progress.
  /// </summary>
  /// <param name="enRunKind">Kind of run.</param>
  /// <returns>True if there is still something to do, otherwise false.</returns>
  public static bool Continue(EnRunKind enRunKind)
  {
    if (_status.Running) return true;
    Volatile.Write(ref _status, new GenerationStatus { Running = true });

    // TODO Placeholder

    Volatile.Write(ref _status, GenerationStatus.Default);
    return false;
  }

  /// <summary>
  /// Get status of generation.
  /// </summary>
  /// <returns>Status of generation.</returns>
  public static GenerationStatus Status()
  {
    return Volatile.Read(ref _status);
  }
}