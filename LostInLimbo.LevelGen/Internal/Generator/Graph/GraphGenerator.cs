using LostInLimbo.LevelGen.Data.Config;
using LostInLimbo.LevelGen.Data.Prepass;
using LostInLimbo.LevelGen.Data.Result;
using LostInLimbo.LevelGen.Internal.Generator.Graph.Builders;

namespace LostInLimbo.LevelGen.Internal.Generator.Graph;

/// <summary>
/// <para>Generates abstract graph representing level and its features. Modifies <c>LilGraph</c>.</para>
/// Dispatches to the <see cref="AGraphBuilder"/> implementation selected by configuration
/// (see <see cref="GraphBuilderFactory"/>).
/// </summary>
internal sealed class GraphGenerator
{
  /// <summary>Configuration data for level generation.</summary>
  private readonly CnfLevelGeneration _config;
  /// <summary>Preparatory data based on config.</summary>
  private readonly PrepData _prepData;

  /// <summary>World: partial or complete result of generation.</summary>
  private LilWorld _world;

  /// <summary>
  /// Prepares graph generator. Assumes the config was already validated by the API layer
  /// (see <see cref="CnfLevelGeneration.Validate"/>) before the run was claimed.
  /// </summary>
  /// <param name="config">Configuration data for level generation. Must not be <see langword="null"/> and must be valid.</param>
  /// <param name="prepData">Preparatory data based on config..</param>
  /// <param name="world">World to modify.</param>
  public GraphGenerator(CnfLevelGeneration config, PrepData prepData, LilWorld world)
  {
    _config = config;
    _prepData = prepData;
    _world = world;
  }

  /// <summary>
  /// Creates whole graph. It is treated as single step.
  /// </summary>
  /// <returns>True if graph generation was successful, otherwise false.</returns>
  public bool Execute()
  {
    AGraphBuilder builder = GraphBuilderFactory.Create(_config.Graph.Builder);
    return builder.Run(_config, _prepData, _world.Graph);
  }
}