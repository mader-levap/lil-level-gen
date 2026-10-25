using LostInLimbo.LevelGen.Api;
using LostInLimbo.LevelGen.Data.Config;
using LostInLimbo.LevelGen.Data.Config.Graph.Validator;
using LostInLimbo.LevelGen.Data.Result;
using LostInLimbo.LevelGen.Internal.Generator.Graph.Builders;
using LostInLimbo.LevelGen.Internal.Generator.Graph.Validator;
using LostInLimbo.LevelGen.Internal.Generator.Prepass;

namespace LostInLimbo.LevelGen.Internal.Generator.Graph;

/// <summary>
/// <para>Generates an abstract graph representing the level and its features. Modifies <c>LilGraph</c>.</para>
/// <para>
/// Dispatches to the <see cref="AGraphBuilder"/> implementation selected by configuration
/// (see <see cref="GraphBuilderFactory"/>) and optionally calls graph validator.
/// </para>
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
  /// Prepares the graph generator. Assumes the config was already validated by the API layer
  /// (see <see cref="CnfLevelGeneration.Validate"/>) before the run was claimed.
  /// </summary>
  /// <param name="config">Configuration data for level generation. Must not be <see langword="null"/> and must be valid.</param>
  /// <param name="prepData">Preparatory data based on config.</param>
  /// <param name="world">World to modify.</param>
  public GraphGenerator(CnfLevelGeneration config, PrepData prepData, LilWorld world)
  {
    _config = config;
    _prepData = prepData;
    _world = world;
  }

  /// <summary>
  /// Creates the whole graph and optionally validates it. It is treated as a single step.
  /// </summary>
  /// <remarks>
  /// The deliberate debug failure of <see cref="CnfDebug.FailGraphValidation"/> is reported
  /// instead of running the real validator, and independently of whether validation is enabled
  /// (see <see cref="CnfValidGraphOpt.Enabled"/>).
  /// </remarks>
  /// <returns>
  /// The outcome of the run: <see cref="EnGenerationOutcome.Success"/>,
  /// <see cref="EnGenerationOutcome.BuildFailed"/> (graph may be partially mutated) or
  /// <see cref="EnGenerationOutcome.ValidationFailed"/> (built graph is intact), each failed outcome
  /// carrying its reason.
  /// </returns>
  public GraphRunResult Execute()
  {
    AGraphBuilder builder = GraphBuilderFactory.Create(_config.Graph.Builder);
    LilResult graphGenResult = builder.Run(_config, _prepData, _world.Graph);
    if (!graphGenResult.Success)
      return GraphRunResult.FailBuild(graphGenResult.FailureReason!);

    if (_config.Debug?.FailGraphValidation == true)
      return GraphRunResult.FailValidation("Deliberate validation failure.");

    LilResult graphValidationResult = new GraphValidator(_world.Graph, _config.Graph.ValidatorOpts).Validate();
    return graphValidationResult.Success
      ? GraphRunResult.Succeed()
      : GraphRunResult.FailValidation(graphValidationResult.FailureReason!);
  }
}