using System;
using LostInLimbo.LevelGen.Data.Config.Graph;
using LostInLimbo.LevelGen.Data.Config.Graph.Builder;
using LostInLimbo.LevelGen.Data.Config.Graph.Proc;
using LostInLimbo.LevelGen.Data.Config.Graph.Validator;
using LostInLimbo.LevelGen.Data.Prop;
using LostInLimbo.LevelGen.Internal.Prop.Data;
using LostInLimbo.LevelGen.Internal.Utils.Rng;

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
  private readonly PropData _propData = propData;

  /// <summary>Random for sub-seed.</summary>
  private readonly SeededRandom _random = new(SeedDerivation.Derive(propData.Seed, SaltGraphResolver));

  /// <summary>
  /// Resolve graph configuration based on given property dataset.
  /// </summary>
  /// <returns>Resolved graph configuration.</returns>
  public CnfGraph Execute()
  {
    return CnfGraph.Default with
    {
      Size = _propData.Size,
      Connectivity = ResolveConnectivity(),
      Complexity = ResolveComplexity(),
      GraphBuilder = ResolveGraphBuilder(),
      ProcOpts = ResolveProcOpts(),
      ValidatorOpts = ResolveValidatorOpts(),
    };
  }

  // Field resolvers.

  /// <summary>
  /// Resolve connectivity of the graph (amount of extra relationships).
  /// </summary>
  /// <returns>Connectivity of the graph.</returns>
  private int ResolveConnectivity()
  {
    // Need to calc amount of extra relationships depending on difficulty and size.
    int from = 0;
    int to = _propMain.Difficulty + _propData.Size;
    return _random.NextInt(from, to);
  }

  /// <summary>
  /// Resolve complexity of the graph (amount of conditional passages).
  /// </summary>
  /// <returns>Complexity of the graph.</returns>
  private int ResolveComplexity()
  {
    // Need to calc amount of conditional passages depending on difficulty and constrained by size.
    int from = 0; // it is ok, we have other obstacles like objectives 
    int to = Math.Min(_propData.Size, _propMain.Difficulty/2+1);
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
  /// <returns>Processor options.</returns>
  private CnfProcOpt ResolveProcOpts()
  {
    return new CnfProcOpt {
      RelSecrets = ResolveSecrets(),
      NodeObjectives = ResolveObjectives(),
      NodeRelSize = CnfProcNodeRelSizeOpt.Default,
      NodeStyle = ResolveStyle(),
    };
  }

  // Objectives.

  /// <summary>
  /// Resolve secrets options.
  /// </summary>
  /// <returns>Secrets options.</returns>
  private CnfProcRelSecretsOpt ResolveSecrets()
  {
    int amountFrom = 0;
    int amountTo = (_propMain.Difficulty+_propData.Size)/2 + 1;

    return CnfProcRelSecretsOpt.Default with
    {
      Amount = _random.NextInt(amountFrom, amountTo),
      ChanceSecretInSecret = 0.1f,
      ChanceTwoPassages = 0.05f,
    };
  }

  /// <summary>
  /// Resolve objectives options.
  /// </summary>
  /// <returns>Objectives options.</returns>
  private CnfProcNodeObjectivesOpt ResolveObjectives()
  {
    int from = 1;
    int to = _propMain.Difficulty/2+1;

    return CnfProcNodeObjectivesOpt.Default with
    {
      GoalAmount = _random.NextInt(from, to),
      OptGoalAmount = _random.NextInt(0, _propData.Size),
    };
  }

  /// <summary>
  /// Resolve style options.
  /// </summary>
  /// <returns>Style options.</returns>
  private CnfProcNodeStyleOpt ResolveStyle()
  {
    int groupCount = 1;
    if (_propMain.ManyThemes) {
      int groupCountMax = Math.Max(2, _propData.Size*2/3+1);
      groupCount = _random.NextInt(2, groupCountMax);
    }

    return CnfProcNodeStyleOpt.Default with
    {
      GroupCount = groupCount,
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