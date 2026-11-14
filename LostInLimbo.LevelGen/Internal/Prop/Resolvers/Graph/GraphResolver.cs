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

  // Connectivity and complexity bounds grow with sqrt(Size), scaled by
  // (BaseFactor + DifficultyFactor * Difficulty / MaxDifficulty). See SqrtSizeBound.

  /// <summary>Connectivity bound per sqrt(Size) at difficulty 0.</summary>
  private const double ConnectivityBaseFactor = 1.0;
  /// <summary>Connectivity bound per sqrt(Size) added at the highest difficulty.</summary>
  private const double ConnectivityDifficultyFactor = 2.0;

  /// <summary>Complexity bound per sqrt(Size) at difficulty 0.</summary>
  private const double ComplexityBaseFactor = 0.5;
  /// <summary>Complexity bound per sqrt(Size) added at the highest difficulty.</summary>
  private const double ComplexityDifficultyFactor = 1.5;

  /// <summary>Smallest drawn amount of secret nodes (inclusive).</summary>
  private const int MinSecretAmount = 0;

  /// <summary>Secret amount bound per sqrt(Size) at difficulty 0 (see <see cref="SqrtSizeBound"/>).</summary>
  private const double SecretAmountBaseFactor = 0.5;
  /// <summary>Secret amount bound per sqrt(Size) added at the highest difficulty.</summary>
  private const double SecretAmountDifficultyFactor = 1.0;

  /// <summary>Chance that a secret nests behind an earlier secret.</summary>
  private const float ChanceSecretInSecret = 0.1f;

  /// <summary>Chance that a secret gets separate entry passage and exit passage.</summary>
  private const float ChanceTwoPassages = 0.05f;

  /// <summary>Smallest drawn amount of mandatory goals (inclusive).</summary>
  private const int MinGoalAmount = 1;

  /// <summary>Goal amount bound per sqrt(Size) at difficulty 0 (see <see cref="SqrtSizeBound"/>).</summary>
  private const double GoalAmountBaseFactor = 0.5;
  /// <summary>Goal amount bound per sqrt(Size) added at the highest difficulty.</summary>
  private const double GoalAmountDifficultyFactor = 1.0;

  /// <summary>Smallest drawn amount of optional goals (inclusive).</summary>
  private const int MinOptGoalAmount = 0;

  /// <summary>Optional goal amount bound per sqrt(Size) at difficulty 0.</summary>
  private const double OptGoalAmountBaseFactor = 1.0;
  /// <summary>Optional goal amount bound per sqrt(Size) added at the highest difficulty; 0 keeps it difficulty-independent.</summary>
  private const double OptGoalAmountDifficultyFactor = 0.0;

  /// <summary>Group count when <see cref="PropMain.ManyThemes"/> is off: a single theme.</summary>
  private const int SingleThemeGroupCount = 1;

  /// <summary>Smallest group count when <see cref="PropMain.ManyThemes"/> is on.</summary>
  private const int MinManyThemesGroupCount = 2;

  /// <summary>Numerator of the share of the graph size used as the largest group count.</summary>
  private const int ManyThemesGroupShareNumerator = 2;

  /// <summary>Denominator of the share of the graph size used as the largest group count.</summary>
  private const int ManyThemesGroupShareDenominator = 3;

  /// <summary>
  /// Builders the resolver picks from, with equal chance. <see cref="EnGraphBuilder.OneNode"/> is
  /// excluded (a trivial single-node level). A new builder joins the pick only when added
  /// here; keep the order stable, as the index of the draw selects the builder.
  /// </summary>
  private static readonly EnGraphBuilder[] EligibleBuilders = [EnGraphBuilder.Simple, EnGraphBuilder.Linear];

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
  /// Resolve connectivity of the graph (amount of extra relationships), drawn uniformly from
  /// <see cref="MinConnectivity"/> up to the inclusive bound
  /// <c>ceil(sqrt(Size) * (1 + 2 * Difficulty / MaxDifficulty))</c>.
  /// </summary>
  /// <remarks>
  /// The bound grows with the square root of the size, so small graphs keep a usable range
  /// while large graphs do not get flooded with extras (a linear bound cannot do both).
  /// </remarks>
  /// <returns>Connectivity of the graph.</returns>
  private int ResolveConnectivity()
  {
    int max = SqrtSizeBound(ConnectivityBaseFactor, ConnectivityDifficultyFactor);
    return _random.NextInt(MinConnectivity, max + 1);
  }

  /// <summary>
  /// Resolve complexity of the graph (amount of conditional passages), drawn uniformly from
  /// <see cref="MinComplexity"/> up to the inclusive bound
  /// <c>ceil(sqrt(Size) * (0.5 + 1.5 * Difficulty / MaxDifficulty))</c>, capped at
  /// <c>Size - 1</c> (a graph has no more cuts to guard).
  /// </summary>
  /// <remarks>Scales with the square root of the size for the same reason as connectivity.</remarks>
  /// <returns>Complexity of the graph.</returns>
  private int ResolveComplexity()
  {
    int max = Math.Min(
      Math.Max(MinComplexity, _propData.Size - 1),
      SqrtSizeBound(ComplexityBaseFactor, ComplexityDifficultyFactor));
    return _random.NextInt(MinComplexity, max + 1);
  }

  /// <summary>
  /// Computes <c>ceil(sqrt(Size) * (baseFactor + difficultyFactor * Difficulty / MaxDifficulty))</c>.
  /// </summary>
  /// <remarks>
  /// Deterministic across platforms: <see cref="Math.Sqrt"/> is correctly rounded by IEEE 754
  /// (unlike <see cref="Math.Pow"/>, which is why no other exponent is used).
  /// </remarks>
  /// <param name="baseFactor">Bound per sqrt(Size) at difficulty 0.</param>
  /// <param name="difficultyFactor">Bound per sqrt(Size) added at the highest difficulty.</param>
  /// <returns>Inclusive upper bound; at least 0.</returns>
  private int SqrtSizeBound(double baseFactor, double difficultyFactor)
  {
    double difficultyShare = (double)_propMain.Difficulty / PropMain.MaxDifficulty;
    return (int)Math.Ceiling(Math.Sqrt(_propData.Size) * (baseFactor + difficultyFactor * difficultyShare));
  }

  // Graph builder.

  /// <summary>
  /// Resolve graph builder and its options.
  /// </summary>
  /// <returns>Graph builder and its options.</returns>
  private CnfGraphBuilder ResolveGraphBuilder()
  {
    EnGraphBuilder enGraphBuilder = PickGraphBuilder();
    return new CnfGraphBuilder
    {
      Builder = enGraphBuilder,
      CnfSimple = enGraphBuilder == EnGraphBuilder.Simple ?
        new BuilderSimpleResolver(_propMain, _propData).Execute() : null,
      CnfLinear = enGraphBuilder == EnGraphBuilder.Linear ?
        new BuilderLinearResolver(_propMain, _propData).Execute() : null,
    };
  }

  /// <summary>
  /// Pick a random graph builder with equal chance from <see cref="EligibleBuilders"/>.
  /// </summary>
  /// <returns>Graph builder.</returns>
  private EnGraphBuilder PickGraphBuilder()
  {
    return EligibleBuilders[_random.NextInt(EligibleBuilders.Length)];
  }

  // Processor options.

  /// <summary>
  /// Resolve processor options.
  /// </summary>
  /// <returns>Processor options.</returns>
  private CnfProcOpt ResolveProcOpts()
  {
    return new CnfProcOpt
    {
      RelSecrets = ResolveSecrets(),
      NodeObjectives = ResolveObjectives(),
      ObstaclePlacer = CnfProcObstaclePlacerOpt.Default, // No difficulty scaling yet.
      NodeRelSize = ResolveRelSize(),
      NodeStyle = ResolveStyle(),
    };
  }

  // Processors.

  /// <summary>
  /// Resolve secrets options. The amount is drawn uniformly from <see cref="MinSecretAmount"/>
  /// up to the inclusive bound <c>ceil(sqrt(Size) * (0.5 + 1.0 * Difficulty / MaxDifficulty))</c>,
  /// which grows with the square root of the size like connectivity and complexity.
  /// </summary>
  /// <returns>Secrets options.</returns>
  private CnfProcRelSecretsOpt ResolveSecrets()
  {
    int amountMax = SqrtSizeBound(SecretAmountBaseFactor, SecretAmountDifficultyFactor);

    return CnfProcRelSecretsOpt.Default with
    {
      Amount = _random.NextInt(MinSecretAmount, amountMax + 1),
      ChanceSecretInSecret = ChanceSecretInSecret,
      ChanceTwoPassages = ChanceTwoPassages,
    };
  }

  /// <summary>
  /// Resolve objectives options. Both amounts grow with the square root of the size, drawn
  /// uniformly up to an inclusive bound: mandatory goals from <see cref="MinGoalAmount"/> to
  /// <c>ceil(sqrt(Size) * (0.5 + 1.0 * Difficulty / MaxDifficulty))</c> (at least one), optional
  /// goals from <see cref="MinOptGoalAmount"/> to <c>ceil(sqrt(Size))</c>.
  /// </summary>
  /// <returns>Objectives options.</returns>
  private CnfProcNodeObjectivesOpt ResolveObjectives()
  {
    int goalMax = Math.Max(MinGoalAmount, SqrtSizeBound(GoalAmountBaseFactor, GoalAmountDifficultyFactor));
    int optGoalMax = SqrtSizeBound(OptGoalAmountBaseFactor, OptGoalAmountDifficultyFactor);

    return CnfProcNodeObjectivesOpt.Default with
    {
      GoalAmount = _random.NextInt(MinGoalAmount, goalMax + 1),
      OptGoalAmount = _random.NextInt(MinOptGoalAmount, optGoalMax + 1),
    };
  }

  /// <summary>
  /// Resolve relative size options.
  /// </summary>
  /// <returns>Relative size options.</returns>
  private CnfProcNodeRelSizeOpt ResolveRelSize()
  {
    // Relative sizes do not depend on difficulty, so only the supporting data is passed.
    return new ProcRelSizeResolver(_propData).Execute();
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
