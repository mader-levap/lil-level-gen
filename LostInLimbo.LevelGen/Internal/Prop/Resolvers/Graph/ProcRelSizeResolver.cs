using System;
using LostInLimbo.LevelGen.Data.Config.Graph;
using LostInLimbo.LevelGen.Data.Config.Graph.Proc;
using LostInLimbo.LevelGen.Internal.Prop.Data;
using LostInLimbo.LevelGen.Internal.Utils.Rng;

namespace LostInLimbo.LevelGen.Internal.Prop.Resolvers.Graph;

/// <summary>
/// Resolves the relative-size processor options (<see cref="CnfProcNodeRelSizeOpt"/>).
/// </summary>
/// <remarks>
/// <para>
/// A weighted profile roll decides the overall character of a graph, then the profile draws
/// its parameters: a single size for every node (very rare), only two sizes (rare), something
/// close to <see cref="CnfProcNodeRelSizeOpt.Default"/> (common) or freely varied options
/// (most common), which range from barely different sizes to wildly different ones.
/// </para>
/// <para>
/// Only the graph size matters: larger graphs allow more steps, bounded by
/// <c>min(MaxStepsCap, max(3, ceil(sqrt(Size))))</c>. Difficulty is deliberately ignored - the
/// resolver does not even receive the property dataset.
/// </para>
/// <para>
/// Every draw range yields options that pass <see cref="CnfProcNodeRelSizeOpt.Validate"/>:
/// geometric growth of at least 2 and linear growth of at least 1 keep the rounded step
/// values strictly increasing.
/// </para>
/// <para>
/// Deterministic for the same internal seed and size; the draws come from a dedicated
/// sub-seed, so other resolvers keep their streams.
/// </para>
/// </remarks>
/// <param name="propData">Internal data supporting config generation; only the seed and size are read.</param>
internal sealed class ProcRelSizeResolver(PropData propData)
{
  #region Constants

  /// <summary>Salt for the relative-size resolver sub-seed; keeps its draws off other resolvers' streams.</summary>
  private const long SaltProcRelSizeResolver = 0x63666772656C737A; // "cfgrelsz"

  // Profile weights, out of ProfileWeightTotal.

  /// <summary>Weight of <see cref="EnProfile.SingleSize"/>.</summary>
  private const int WeightSingleSize = 5;
  /// <summary>Weight of <see cref="EnProfile.TwoSizes"/>.</summary>
  private const int WeightTwoSizes = 10;
  /// <summary>Weight of <see cref="EnProfile.NearDefault"/>.</summary>
  private const int WeightNearDefault = 30;
  /// <summary>Weight of <see cref="EnProfile.Varied"/>.</summary>
  private const int WeightVaried = 55;
  /// <summary>Sum of all profile weights.</summary>
  private const int ProfileWeightTotal = WeightSingleSize + WeightTwoSizes + WeightNearDefault + WeightVaried;

  // Step counts.

  /// <summary>Hard cap of steps, however large the graph is.</summary>
  private const int MaxStepsCap = 8;
  /// <summary>Smallest step bound; even the smallest graphs allow the default three steps.</summary>
  private const int MinStepsBound = 3;
  /// <summary>Step count of <see cref="EnProfile.SingleSize"/>.</summary>
  private const int SingleSizeSteps = 1;
  /// <summary>Step count of <see cref="EnProfile.TwoSizes"/>.</summary>
  private const int TwoSizesSteps = 2;
  /// <summary>Step count of <see cref="EnProfile.NearDefault"/>.</summary>
  private const int NearDefaultSteps = 3;
  /// <summary>Smallest step count of <see cref="EnProfile.Varied"/> (inclusive).</summary>
  private const int VariedStepsMin = 3;

  // Near-default draw ranges: each pair is [Min, MaxExclusive).

  /// <summary>Smallest near-default geometric growth (inclusive).</summary>
  private const float NearDefaultGrowthMin = 2.0f;
  /// <summary>Upper bound of the near-default geometric growth (exclusive).</summary>
  private const float NearDefaultGrowthMaxExclusive = 2.5f;
  /// <summary>Smallest near-default skew (inclusive).</summary>
  private const float NearDefaultSkewMin = 0.2f;
  /// <summary>Upper bound of the near-default skew (exclusive).</summary>
  private const float NearDefaultSkewMaxExclusive = 0.4f;
  /// <summary>Smallest near-default relationship and POI bias (inclusive).</summary>
  private const float NearDefaultBiasMin = 0.4f;
  /// <summary>Upper bound of the near-default relationship and POI bias (exclusive).</summary>
  private const float NearDefaultBiasMaxExclusive = 0.8f;
  /// <summary>Smallest step value of the near-default profile.</summary>
  private const int NearDefaultMinSize = 1;

  // Varied draw ranges: each pair is [Min, MaxExclusive).

  /// <summary>Chance of the linear curve; the geometric curve takes the rest.</summary>
  private const float ChanceLinearCurve = 0.5f;
  /// <summary>Smallest geometric growth (inclusive); 2 keeps rounded steps strictly increasing.</summary>
  private const float GeometricGrowthMin = 2.0f;
  /// <summary>Upper bound of the geometric growth (exclusive).</summary>
  private const float GeometricGrowthMaxExclusive = 3.0f;
  /// <summary>Smallest linear growth (inclusive); 1 keeps rounded steps strictly increasing.</summary>
  private const float LinearGrowthMin = 1.0f;
  /// <summary>Upper bound of the linear growth (exclusive).</summary>
  private const float LinearGrowthMaxExclusive = 4.0f;
  /// <summary>Smallest drawn step value (inclusive).</summary>
  private const int MinSizeMin = 1;
  /// <summary>Upper bound of the drawn smallest step value (exclusive).</summary>
  private const int MinSizeMaxExclusive = 5;
  /// <summary>Smallest drawn skew (inclusive).</summary>
  private const float SkewMin = 0f;
  /// <summary>Upper bound of the drawn skew (exclusive).</summary>
  private const float SkewMaxExclusive = 1f;
  /// <summary>Smallest drawn relationship and POI bias (inclusive).</summary>
  private const float BiasMin = 0f;
  /// <summary>Upper bound of the drawn relationship and POI bias (exclusive).</summary>
  private const float BiasMaxExclusive = 1.5f;

  #endregion

  /// <summary>Overall character of the relative sizes of one graph.</summary>
  private enum EnProfile
  {
    /// <summary>One step: every node has the same size.</summary>
    SingleSize,
    /// <summary>Two steps with varied parameters.</summary>
    TwoSizes,
    /// <summary>Three geometric steps with parameters close to the default.</summary>
    NearDefault,
    /// <summary>Three or more steps with freely varied parameters.</summary>
    Varied,
  }

  /// <summary>Supporting data; only the size is read.</summary>
  private readonly PropData _propData = propData;
  /// <summary>Random for sub-seed.</summary>
  private readonly SeededRandom _random = new(SeedDerivation.Derive(propData.Seed, SaltProcRelSizeResolver));

  /// <summary>
  /// Resolve relative-size processor options. Deterministic for the same seed and size.
  /// </summary>
  /// <returns>Relative-size options that always pass validation.</returns>
  public CnfProcNodeRelSizeOpt Execute()
  {
    return PickProfile() switch
    {
      EnProfile.SingleSize => CnfProcNodeRelSizeOpt.Default with { Steps = SingleSizeSteps },
      EnProfile.TwoSizes => DrawVaried(TwoSizesSteps),
      EnProfile.NearDefault => DrawNearDefault(),
      _ => DrawVaried(_random.NextInt(VariedStepsMin, ResolveMaxSteps() + 1)),
    };
  }

  #region Helpers

  /// <summary>Rolls the profile by its weight.</summary>
  /// <returns>The picked profile.</returns>
  private EnProfile PickProfile()
  {
    var roll = _random.NextInt(ProfileWeightTotal);
    if (roll < WeightSingleSize) return EnProfile.SingleSize;
    roll -= WeightSingleSize;
    if (roll < WeightTwoSizes) return EnProfile.TwoSizes;
    roll -= WeightTwoSizes;
    return roll < WeightNearDefault ? EnProfile.NearDefault : EnProfile.Varied;
  }

  /// <summary>
  /// Computes the largest step count for the graph size:
  /// <c>min(MaxStepsCap, max(3, ceil(sqrt(Size))))</c>.
  /// </summary>
  /// <remarks><see cref="Math.Sqrt"/> is correctly rounded, so the bound is platform-stable.</remarks>
  /// <returns>Inclusive step bound.</returns>
  private int ResolveMaxSteps()
  {
    var sqrtBound = (int)Math.Ceiling(Math.Sqrt(_propData.Size));
    return Math.Min(MaxStepsCap, Math.Max(MinStepsBound, sqrtBound));
  }

  /// <summary>Draws options close to the default: three geometric steps starting at 1.</summary>
  /// <returns>Near-default options.</returns>
  private CnfProcNodeRelSizeOpt DrawNearDefault()
  {
    return CnfProcNodeRelSizeOpt.Default with
    {
      Steps = NearDefaultSteps,
      Curve = EnRelSizeCurve.Geometric,
      MinSize = NearDefaultMinSize,
      Growth = _random.NextFloat(NearDefaultGrowthMin, NearDefaultGrowthMaxExclusive),
      Skew = _random.NextFloat(NearDefaultSkewMin, NearDefaultSkewMaxExclusive),
      BiasRelationships = _random.NextFloat(NearDefaultBiasMin, NearDefaultBiasMaxExclusive),
      BiasPois = _random.NextFloat(NearDefaultBiasMin, NearDefaultBiasMaxExclusive),
    };
  }

  /// <summary>
  /// Draws freely varied options: any curve, growth, smallest size, skew and biases.
  /// </summary>
  /// <param name="steps">Step count to use; at least 2.</param>
  /// <returns>Varied options.</returns>
  private CnfProcNodeRelSizeOpt DrawVaried(int steps)
  {
    var curve = _random.NextBool(ChanceLinearCurve) ? EnRelSizeCurve.Linear : EnRelSizeCurve.Geometric;
    var growth = curve == EnRelSizeCurve.Linear
      ? _random.NextFloat(LinearGrowthMin, LinearGrowthMaxExclusive)
      : _random.NextFloat(GeometricGrowthMin, GeometricGrowthMaxExclusive);

    return CnfProcNodeRelSizeOpt.Default with
    {
      Steps = steps,
      Curve = curve,
      Growth = growth,
      MinSize = _random.NextInt(MinSizeMin, MinSizeMaxExclusive),
      Skew = _random.NextFloat(SkewMin, SkewMaxExclusive),
      BiasRelationships = _random.NextFloat(BiasMin, BiasMaxExclusive),
      BiasPois = _random.NextFloat(BiasMin, BiasMaxExclusive),
    };
  }

  #endregion
}
