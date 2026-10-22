using System;
using System.Threading;
using LostInLimbo.LevelGen.Data.Config;
using LostInLimbo.LevelGen.Data.Result;
using LostInLimbo.LevelGen.Internal.Generator.Prepass;

namespace LostInLimbo.LevelGen.Internal.Generator.Map;

/// <summary>
/// <para>Generates map data. Modifies <c>LilMap</c>.</para>
/// NOTE: right now it is a placeholder.
/// </summary>
internal sealed class MapGenerator
{
  /// <summary>Configuration data for level generation.</summary>
  private readonly CnfLevelGeneration _config;
  /// <summary>Preparatory data based on config.</summary>
  private readonly PrepData _prepData;

  /// <summary>World: partial or complete result of generation.</summary>
  private LilWorld _world;
  
  /// <summary>Total number of placeholder steps of this generator's configuration.</summary>
  private int _totalSteps => _config.Debug?.PlaceholderSteps ?? CnfDebug.DefaultPlaceholderSteps;

  /// <summary>
  /// Prepares the map generator.
  /// </summary>
  /// <param name="config">Configuration data for level generation. Must not be <see langword="null"/> and must be valid.</param>
  /// <param name="prepData">Preparatory data based on config.</param>
  /// <param name="world">World to modify.</param>
  public MapGenerator(CnfLevelGeneration config, PrepData prepData, LilWorld world)
  {
    _config = config;
    _prepData = prepData;
    _world = world;
  }

  /// <summary>
  /// Executes one step of map generation.
  /// </summary>
  /// <returns>True if the step was successful, otherwise false.</returns>
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