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

  /// <summary>Smallest drawn complexity (inclusive) at difficulty 0: no conditional passages;
  /// objectives still add challenge.</summary>
  private const int MinComplexity = 0;

  /// <summary>Smallest drawn complexity (inclusive) at any difficulty above 0: at least one obstacle.</summary>
  private const int MinComplexityAboveZeroDifficulty = 1;

  // Connectivity and complexity upper bounds grow with sqrt(Size), scaled by
  // (BaseFactor + DifficultyFactor * Difficulty / MaxDifficulty). See SqrtSizeBound.
  // The complexity lower bound uses SqrtSizeFloor: no base term and rounded down, so it
  // rises more slowly than the upper bound.

  /// <summary>Connectivity bound per sqrt(Size) at difficulty 0.</summary>
  private const double ConnectivityBaseFactor = 1.0;
  /// <summary>Connectivity bound per sqrt(Size) added at the highest difficulty.</summary>
  private const double ConnectivityDifficultyFactor = 2.0;

  /// <summary>Complexity bound per sqrt(Size) at difficulty 0.</summary>
  private const double ComplexityBaseFactor = 0.5;
  /// <summary>Complexity bound per sqrt(Size) added at the highest difficulty.</summary>
  private const double ComplexityDifficultyFactor = 1.5;
  /// <summary>Complexity lower bound per sqrt(Size) at the highest difficulty (see <see cref="SqrtSizeFloor"/>).</summary>
  private const double ComplexityLowerDifficultyFactor = 1.0;

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

  /// <summary>Many-themes group count bound per sqrt(Size) at difficulty 0 (see <see cref="SqrtSizeBound"/>).</summary>
  private const double ManyThemesGroupCountBaseFactor = 1.0;
  /// <summary>Many-themes group count bound per sqrt(Size) added at the highest difficulty; 0 keeps it difficulty-independent.</summary>
  private const double ManyThemesGroupCountDifficultyFactor = 0.0;

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
    var config = CnfGraph.Default with
    {
      Size = _propData.Size,
      Connectivity = ResolveConnectivity(),
      Complexity = ResolveComplexity(),
      GraphBuilder = ResolveGraphBuilder(),
      ProcOpts = ResolveProcOpts(),
      ValidatorOpts = ResolveValidatorOpts(),
    };

    // Capped after all draws, so the random stream does not depend on the builder.
    return config with
    {
      Connectivity = CapConnectivity(config.Connectivity, config.GraphBuilder.Builder, config.Size),
    };
  }

  #region Field resolvers

  /// <summary>
  /// Resolve connectivity of the graph (amount of extra relationships), drawn uniformly from
  /// <see cref="MinConnectivity"/> up to the inclusive bound
  /// <c>ceil(sqrt(Size) * (1 + 2 * Difficulty / MaxDifficulty))</c>.
  /// </summary>
  /// <remarks>
  /// The bound grows with the square root of the size, so small graphs keep a usable range
  /// while large graphs do not get flooded with extras (a linear bound cannot do both).
  /// The drawn value is later capped at what the builder can build, see <see cref="CapConnectivity"/>.
  /// </remarks>
  /// <returns>Connectivity of the graph.</returns>
  private int ResolveConnectivity()
  {
    int max = SqrtSizeBound(ConnectivityBaseFactor, ConnectivityDifficultyFactor);
    return _random.NextInt(MinConnectivity, max + 1);
  }

  /// <summary>
  /// Caps Connectivity at the number of extra relationships the builder can ever create.
  /// </summary>
  /// <remarks>
  /// Simple joins every node pair at most once, so beyond the <c>size - 1</c> relationships of
  /// the spanning skeleton only <c>(size - 1) * (size - 2) / 2</c> pairs remain (0 for two
  /// nodes, 1 for three). Other builders are not capped: Linear may add parallel passages, and
  /// its degree caps are drawn separately. Degree caps can still leave Connectivity unspent;
  /// it stays a soft constraint.
  /// </remarks>
  /// <param name="connectivity">Drawn Connectivity.</param>
  /// <param name="builder">Resolved graph builder.</param>
  /// <param name="size">Node count of the graph.</param>
  /// <returns>Connectivity, never above the builder's maximum; never negative.</returns>
  private static int CapConnectivity(int connectivity, EnGraphBuilder builder, int size)
  {
    if (builder != EnGraphBuilder.Simple) return connectivity;

    var pairsBeyondSkeleton = size < 2 ? 0 : (long)(size - 1) * (size - 2) / 2;
    return (int)Math.Min(connectivity, pairsBeyondSkeleton);
  }

  /// <summary>
  /// Resolve complexity of the graph (amount of conditional passages), drawn uniformly from an
  /// inclusive range:
  /// <list type="bullet">
  /// <item>Upper bound <c>ceil(sqrt(Size) * (0.5 + 1.5 * Difficulty / MaxDifficulty))</c>, capped
  /// at <c>Size - 1</c> (a graph has no more cuts to guard).</item>
  /// <item>Lower bound <see cref="MinComplexity"/> at difficulty 0; otherwise
  /// <c>floor(sqrt(Size) * 1.0 * Difficulty / MaxDifficulty)</c>, at least
  /// <see cref="MinComplexityAboveZeroDifficulty"/>. Never above the upper bound.</item>
  /// </list>
  /// </summary>
  /// <remarks>
  /// Scales with the square root of the size for the same reason as connectivity. The lower
  /// bound rises more slowly than the upper one, so the range widens with difficulty instead
  /// of collapsing. Every difficulty above 0 gets at least one obstacle; the smallest size
  /// difficulty 1 can draw (2) still has one cut, so the minimum is always reachable.
  /// </remarks>
  /// <returns>Complexity of the graph.</returns>
  private int ResolveComplexity()
  {
    int max = Math.Min(
      Math.Max(MinComplexity, _propData.Size - 1),
      SqrtSizeBound(ComplexityBaseFactor, ComplexityDifficultyFactor));

    int min = _propMain.Difficulty == 0
      ? MinComplexity
      : Math.Max(MinComplexityAboveZeroDifficulty, SqrtSizeFloor(ComplexityLowerDifficultyFactor));
    min = Math.Min(min, max); // Safety clamp; only matters for a degenerate Size below 2.

    return _random.NextInt(min, max + 1);
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

  /// <summary>
  /// Computes <c>floor(sqrt(Size) * difficultyFactor * Difficulty / MaxDifficulty)</c>: a lower
  /// bound that starts at 0 and grows with difficulty.
  /// </summary>
  /// <remarks>Deterministic across platforms for the same reason as <see cref="SqrtSizeBound"/>.</remarks>
  /// <param name="difficultyFactor">Bound per sqrt(Size) at the highest difficulty.</param>
  /// <returns>Inclusive lower bound; at least 0.</returns>
  private int SqrtSizeFloor(double difficultyFactor)
  {
    double difficultyShare = (double)_propMain.Difficulty / PropMain.MaxDifficulty;
    return (int)Math.Floor(Math.Sqrt(_propData.Size) * difficultyFactor * difficultyShare);
  }

  #endregion

  #region Graph builder

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

  #endregion

  #region Processor options

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

  #endregion

  #region Processors

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
  /// Resolve style options. With <see cref="PropMain.ManyThemes"/> off the group count is
  /// <see cref="SingleThemeGroupCount"/>; with it on, the count is drawn uniformly from
  /// <see cref="MinManyThemesGroupCount"/> up to the inclusive bound <c>ceil(sqrt(Size))</c>
  /// (at least the minimum). That bound grows with the square root of the size like the other
  /// bounds, does not depend on difficulty, and never goes above Size.
  /// </summary>
  /// <returns>Style options.</returns>
  private CnfProcNodeStyleOpt ResolveStyle()
  {
    int groupCount = SingleThemeGroupCount;
    if (_propMain.ManyThemes)
    {
      int groupCountMax = Math.Max(
        MinManyThemesGroupCount,
        SqrtSizeBound(ManyThemesGroupCountBaseFactor, ManyThemesGroupCountDifficultyFactor));
      groupCount = _random.NextInt(MinManyThemesGroupCount, groupCountMax + 1);
    }

    return CnfProcNodeStyleOpt.Default with
    {
      GroupCount = groupCount,
    };
  }

  #endregion

  #region Validator options

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

  #endregion
}
