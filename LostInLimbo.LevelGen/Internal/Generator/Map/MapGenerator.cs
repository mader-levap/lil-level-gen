using System.Threading;
using LostInLimbo.LevelGen.Data.Config;
using LostInLimbo.LevelGen.Data.Result;
using LostInLimbo.LevelGen.Internal.Generator.Prepass;

namespace LostInLimbo.LevelGen.Internal.Generator.Map;

/// <summary>
/// <para>Placeholder map pass; currently only advances the step counter (does not modify <c>LilMap</c>).</para>
/// </summary>
/// <remarks>
/// <para>Currently PLACEHOLDER.</para>
/// </remarks>
/// <remarks>
/// Prepares the map generator.
/// </remarks>
/// <param name="config">Configuration data for level generation. Must not be <see langword="null"/> and must be valid.</param>
/// <param name="prepData">Preparatory data based on config.</param>
/// <param name="world">World to modify.</param>
internal sealed class MapGenerator(CnfLevelGeneration config, PrepData prepData, LilWorld world)
{
  /// <summary>Configuration data for level generation.</summary>
  private readonly CnfLevelGeneration _config = config;
  /// <summary>Preparatory data based on config.</summary>
  private readonly PrepData _prepData = prepData;

  /// <summary>World: partial or complete result of generation.</summary>
  private LilWorld _world = world;
  
  /// <summary>Total number of placeholder steps of this generator's configuration.</summary>
  private int _totalSteps => _config.Debug?.PlaceholderSteps ?? CnfDebug.DefaultPlaceholderSteps;

  /// <summary>
  /// Executes one step of map generation.
  /// </summary>
  /// <remarks>
  /// <para>Currently PLACEHOLDER.</para>
  /// <para>
  /// Each step sleeps for <c>PlaceholderSleep / PlaceholderSteps</c> milliseconds as an
  /// imitation of the work done in a single step. The sleep itself is a plain, uninterruptible
  /// <see cref="Thread.Sleep(int)"/>.
  /// Completed steps are counted in <see cref="LilStats.Steps"/> of <see cref="_world"/>,
  /// which survives pausing and resuming because the API keeps this generator instance alive.
  /// </para>
  /// </remarks>
  /// <returns>
  /// The outcome of the run: <see cref="EnGenerationOutcome.Success"/> or
  /// <see cref="EnGenerationOutcome.BuildFailed"/> (map may be partially mutated).
  /// </returns>
  public MapRunResult ExecuteStep()
  {
    var sleepMilliseconds = _config.Debug?.PlaceholderSleep ?? CnfDebug.DefaultPlaceholderSleep;
    var stepSleepMilliseconds = sleepMilliseconds / _totalSteps;

    // Imitation of the work of a single step; deliberately not token-aware, this is handled by LevelGenerator.
    Thread.Sleep(stepSleepMilliseconds);

    _world.Stats.Steps++;
    return MapRunResult.Succeed();  
  }
}