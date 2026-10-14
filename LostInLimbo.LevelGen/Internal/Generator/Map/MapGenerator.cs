using System;
using System.Threading;
using LostInLimbo.LevelGen.Data.Config;
using LostInLimbo.LevelGen.Data.Other;
using LostInLimbo.LevelGen.Data.Result;
using LostInLimbo.LevelGen.Internal.Status;

namespace LostInLimbo.LevelGen.Internal.Generator.Map;

/// <summary>
/// <para>Generates map data. Modifies <c>LilMap</c>.</para>
/// </summary>
internal sealed class MapGenerator
{
  /// <summary>Configuration data for level generation.</summary>
  private readonly CnfLevelGeneration _config;
  /// <summary>Shared, thread-safe status sink owned by the API layer.</summary>
  private readonly GenerationStatusStore _statusStore;

  /// <summary>World: partial or complete result of generation.</summary>
  private LilWorld _world;
  
  /// <summary>Total number of placeholder steps of this generator's configuration.</summary>
  private int _totalSteps => _config.Debug?.PlaceholderSteps ?? CnfDebug.DefaultPlaceholderSteps;

  /// <summary>
  /// Prepares map generator.
  /// </summary>
  /// <param name="config">Configuration data for level generation. Must not be <see langword="null"/> and must be valid.</param>
  /// <param name="statusStore">Store receiving status snapshots during execution.</param>
  /// <param name="world">World to modify.</param>
  public MapGenerator(CnfLevelGeneration config, GenerationStatusStore statusStore, LilWorld world)
  {
    _config = config;
    _statusStore = statusStore;
    _world = world;
  }

  /// <summary>
  /// Executes one step of map generation.
  /// </summary>
  /// <returns>True if step was successful, otherwise false.</returns>
  public bool ExecuteStep()
  {
    var sleepMilliseconds = _config.Debug?.PlaceholderSleep ?? CnfDebug.DefaultPlaceholderSleep;
    var stepSleepMilliseconds = sleepMilliseconds / _totalSteps;

    // Imitation of the work of a single step; deliberately not token-aware.
    Thread.Sleep(stepSleepMilliseconds);

    _world.Stats.Steps++;
    return true;  
  }
}