using LostInLimbo.LevelGen.Data.Config;
using LostInLimbo.LevelGen.Data.Result;
using LostInLimbo.LevelGen.Internal.Status;

namespace LostInLimbo.LevelGen.Internal.Generator.Graph;

/// <summary>
/// <para>Generates abstract graph representing level and its features.</para>
/// </summary>
internal sealed class GraphGenerator
{
  /// <summary>Configuration data for level generation.</summary>
  private readonly CnfLevelGeneration _config;
  /// <summary>Shared, thread-safe status sink owned by the API layer.</summary>
  private readonly GenerationStatusStore _statusStore;

  /// <summary>World: partial or complete result of generation.</summary>
  private LilWorld _world;

  /// <summary>
  /// Prepares graph generator. Assumes the config was already validated by the API layer
  /// (see <see cref="CnfLevelGeneration.Validate"/>) before the run was claimed.
  /// </summary>
  /// <param name="config">Configuration data for level generation. Must not be <see langword="null"/> and must be valid.</param>
  /// <param name="statusStore">Store receiving status snapshots during execution.</param>
  /// <param name="world">World to modify.</param>
  public GraphGenerator(CnfLevelGeneration config, GenerationStatusStore statusStore, LilWorld world)
  {
    _config = config;
    _statusStore = statusStore;
    _world = world;
  }

  /// <summary>
  /// Creates whole graph. It is treated as single step.
  /// </summary>
  /// <returns>True if graph generation was successful, otherwise false.</returns>
  public bool Execute()
  {
    // TODO
    return true;  
  }
}