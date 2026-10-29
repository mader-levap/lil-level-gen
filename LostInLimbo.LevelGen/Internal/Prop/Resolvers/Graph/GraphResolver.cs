using System;
using LostInLimbo.LevelGen.Data.Config.Graph;
using LostInLimbo.LevelGen.Data.Config.Graph.Builder;
using LostInLimbo.LevelGen.Data.Config.Graph.Proc;
using LostInLimbo.LevelGen.Data.Config.Graph.Validator;
using LostInLimbo.LevelGen.Data.Prop;
using LostInLimbo.LevelGen.Internal.Utils;

namespace LostInLimbo.LevelGen.Internal.Prop.Resolvers.Graph;

/// <summary>
/// Resolves graph configuration.
/// </summary>
/// <param name="propMain">Property dataset.</param>
/// <param name="propData">Internal data supporting config generation.</param>
internal sealed class GraphResolver(PropMain propMain, PropData propData)
{
  /// <summary>Salt for the graph resolver sub-seed; keeps this pass off other passes' streams.</summary>
  private const long SaltGraphResolver = 0x6366676772617068; // "cfggraph"

  /// <summary>Property dataset.</summary>
  private readonly PropMain _propMain = propMain;
  /// <summary>Supporting data.</summary>
  private PropData _propData = propData;

  /// <summary>Random for sub-seed.</summary>
  private readonly SeededRandom _random = new(SeedDerivation.Derive(propData.Seed, SaltGraphResolver));

  /// <summary>
  /// Resolve graph configuration based on given property dataset.
  /// </summary>
  /// <returns>Resolved graph configuration.</returns>
  public CnfGraph Execute()
  {
    int size = ResolveSize();
    return CnfGraph.Default with
    {
      Size = size,
      Connectivity = ResolveConnectivity(size),
      Complexity = ResolveComplexity(size),
      GraphBuilder = ResolveGraphBuilder(),
      ProcOpts = ResolveProcOpts(size),
      ValidatorOpts = ResolveValidatorOpts(),
    };
  }

  // Field resolvers.

  /// <summary>
  /// Resolve size of the graph.
  /// </summary>
  /// <returns>Size of the graph.</returns>
  private int ResolveSize()
  {
    int from = 2;
    int to = _propMain.Difficulty + 3;
    return _random.NextInt(from, to);
  }

  /// <summary>
  /// Resolve connectivity of the graph (amount of extra relationships).
  /// </summary>
  /// <param name="size">Size of graph.</param>
  /// <returns>Connectivity of the graph.</returns>
  private int ResolveConnectivity(int size)
  {
    // Need to calc amount of extra relationships depending on difficulty and size.
    int from = 0;
    int to = _propMain.Difficulty + size;
    return _random.NextInt(from, to);
  }

  /// <summary>
  /// Resolve complexity of the graph (amount of conditional passages).
  /// </summary>
  /// <param name="size">Size of graph.</param>
  /// <returns>Complexity of the graph.</returns>
  private int ResolveComplexity(int size)
  {
    // Need to calc amount of conditional passages depending on difficulty and constrained by size.
    int from = 0; // it is ok, we have other obstacles like objectives 
    int to = Math.Min(size, _propMain.Difficulty/2+1);
    return _random.NextInt(from, to);
  }

  // Graph builder.

  /// <summary>
  /// Resolve graph builder and its options.
  /// </summary>
  /// <returns>Graph builder and its options.</returns>
  private CnfGraphBuilder ResolveGraphBuilder()
  {
    EnGraphBuilder enGraphBuilder = PickGraphBuilder();
    return new CnfGraphBuilder {
      Builder = enGraphBuilder,
      CnfSimple = enGraphBuilder == EnGraphBuilder.Simple ?
        new BuilderSimpleResolver(_propMain, _propData, _random).Execute() : null,
      CnfLinear = enGraphBuilder == EnGraphBuilder.Linear ?
        new BuilderLinearResolver(_propMain, _propData, _random).Execute() : null,
    };
  }

  /// <summary>
  /// Pick random graph builder from available graph builders, except <c>EnGraphBuilder.OneNode</c>.
  /// </summary>
  /// <returns>Graph builder.</returns>
  private EnGraphBuilder PickGraphBuilder()
  {
    int count = Enum.GetValues(typeof(EnGraphBuilder)).Length;
    return (EnGraphBuilder)_random.NextInt(1, count); // we exclude first member
  }

  // Processor options.

  /// <summary>
  /// Resolve processor options.
  /// </summary>
  /// <param name="size">Size of graph.</param>
  /// <returns>Processor options.</returns>
  private CnfProcOpt ResolveProcOpts(int size)
  {
    return new CnfProcOpt {
      RelSecrets = CnfProcRelSecretsOpt.Default,
      NodeObjectives = ResolveObjectives(size),
      NodeRelSize = CnfProcNodeRelSizeOpt.Default,
      NodeStyle = CnfProcNodeStyleOpt.Default,
    };
  }

  // Objectives.

  /// <summary>
  /// Resolve objectives options.
  /// </summary>
  /// <param name="size">Size of graph.</param>
  /// <returns>Objectives options.</returns>
  private CnfProcNodeObjectivesOpt ResolveObjectives(int size)
  {
    int from = 1;
    int to = _propMain.Difficulty/2+1;
    return CnfProcNodeObjectivesOpt.Default with
    {
      GoalAmount = _random.NextInt(from, to),
      OptGoalAmount = _random.NextInt(0, size),
    };
  }

  // Validator options.

  /// <summary>
  /// Resolve validator options.
  /// </summary>
  /// <returns>Validator options.</returns>
  private CnfValidGraphOpt ResolveValidatorOpts()
  {
    return CnfValidGraphOpt.Default with
    {
      Enabled = true,
      AllowOneWayDeadEnds = false,
    };
  }
}