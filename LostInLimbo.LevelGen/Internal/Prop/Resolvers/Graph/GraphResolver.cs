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

  // Draw ranges below are [from, to); a "+ 1" turns an inclusive maximum into an exclusive bound.

  /// <summary>Smallest drawn connectivity (inclusive): no extra relationships.</summary>
  private const int MinConnectivity = 0;

  /// <summary>Smallest drawn complexity (inclusive): no conditional passages; objectives still add challenge.</summary>
  private const int MinComplexity = 0;

  /// <summary>Difficulty levels per additional allowed conditional passage.</summary>
  private const int ComplexityDifficultyDivisor = 2;

  /// <summary>Smallest drawn amount of secret nodes (inclusive).</summary>
  private const int MinSecretAmount = 0;

  /// <summary>Divisor applied to <c>Difficulty + Size</c> to get the largest drawn amount of secret nodes.</summary>
  private const int SecretAmountDivisor = 2;

  /// <summary>Chance that a secret nests behind an earlier secret.</summary>
  private const float ChanceSecretInSecret = 0.1f;

  /// <summary>Chance that a secret gets separate entry passage and exit passage.</summary>
  private const float ChanceTwoPassages = 0.05f;

  /// <summary>Smallest drawn amount of mandatory goals (inclusive).</summary>
  private const int MinGoalAmount = 1;

  /// <summary>Difficulty levels per additional allowed mandatory goal.</summary>
  private const int GoalAmountDifficultyDivisor = 2;

  /// <summary>Smallest drawn amount of optional goals (inclusive).</summary>
  private const int MinOptGoalAmount = 0;

  /// <summary>Group count when <see cref="PropMain.ManyThemes"/> is off: a single theme.</summary>
  private const int SingleThemeGroupCount = 1;

  /// <summary>Smallest group count when <see cref="PropMain.ManyThemes"/> is on.</summary>
  private const int MinManyThemesGroupCount = 2;

  /// <summary>Numerator of the share of the graph size used as the largest group count.</summary>
  private const int ManyThemesGroupShareNumerator = 2;

  /// <summary>Denominator of the share of the graph size used as the largest group count.</summary>
  private const int ManyThemesGroupShareDenominator = 3;

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
    int from = MinConnectivity;
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
    int from = MinComplexity;
    int to = Math.Min(_propData.Size, _propMain.Difficulty / ComplexityDifficultyDivisor + 1);
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
      NodeRelSize = ResolveRelSize(),
      NodeStyle = ResolveStyle(),
    };
  }

  // Processors.

  /// <summary>
  /// Resolve secrets options.
  /// </summary>
  /// <returns>Secrets options.</returns>
  private CnfProcRelSecretsOpt ResolveSecrets()
  {
    int amountFrom = MinSecretAmount;
    int amountTo = (_propMain.Difficulty + _propData.Size) / SecretAmountDivisor + 1;

    return CnfProcRelSecretsOpt.Default with
    {
      Amount = _random.NextInt(amountFrom, amountTo),
      ChanceSecretInSecret = ChanceSecretInSecret,
      ChanceTwoPassages = ChanceTwoPassages,
    };
  }

  /// <summary>
  /// Resolve objectives options.
  /// </summary>
  /// <returns>Objectives options.</returns>
  private CnfProcNodeObjectivesOpt ResolveObjectives()
  {
    int from = MinGoalAmount;
    int to = _propMain.Difficulty / GoalAmountDifficultyDivisor + 1;

    return CnfProcNodeObjectivesOpt.Default with
    {
      GoalAmount = _random.NextInt(from, to),
      OptGoalAmount = _random.NextInt(MinOptGoalAmount, _propData.Size),
    };
  }

  /// <summary>
  /// Resolve relative size options.
  /// </summary>
  /// <returns>Relative size options.</returns>
  private CnfProcNodeRelSizeOpt ResolveRelSize()
  {
    return CnfProcNodeRelSizeOpt.Default with
    {
      // TODO
    };
  }

  /// <summary>
  /// Resolve style options.
  /// </summary>
  /// <returns>Style options.</returns>
  private CnfProcNodeStyleOpt ResolveStyle()
  {
    int groupCount = SingleThemeGroupCount;
    if (_propMain.ManyThemes)
    {
      int groupCountMax = Math.Max(
        MinManyThemesGroupCount,
        _propData.Size * ManyThemesGroupShareNumerator / ManyThemesGroupShareDenominator + 1);
      groupCount = _random.NextInt(MinManyThemesGroupCount, groupCountMax);
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
