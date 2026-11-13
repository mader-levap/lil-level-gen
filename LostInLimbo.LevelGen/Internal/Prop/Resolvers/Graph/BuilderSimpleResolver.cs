using LostInLimbo.LevelGen.Data.Config.Graph.Builder;
using LostInLimbo.LevelGen.Data.Prop;
using LostInLimbo.LevelGen.Internal.Prop.Data;
using LostInLimbo.LevelGen.Internal.Utils.Rng;

namespace LostInLimbo.LevelGen.Internal.Prop.Resolvers.Graph;

/// <summary>
/// Resolves simple graph builder configuration.
/// </summary>
/// <param name="propMain">Property dataset.</param>
/// <param name="propData">Internal data supporting config generation.</param>
/// <param name="random">Random for sub-seed.</param>
internal sealed class BuilderSimpleResolver(PropMain propMain, PropData propData, SeededRandom random)
{
  // Draw ranges: each pair is [Min, MaxExclusive).

  /// <summary>Smallest drawn <see cref="CnfGraphBuilderSimple.ChokepointChance"/> (inclusive).</summary>
  private const float ChokepointChanceMin = 0.2f;
  /// <summary>Upper bound of the drawn <see cref="CnfGraphBuilderSimple.ChokepointChance"/> (exclusive).</summary>
  private const float ChokepointChanceMaxExclusive = 0.95f;

  /// <summary>Smallest drawn <see cref="CnfGraphBuilderSimple.MaxExtraDegree"/> (inclusive).</summary>
  private const int MaxExtraDegreeMin = 2;
  /// <summary>Upper bound of the drawn <see cref="CnfGraphBuilderSimple.MaxExtraDegree"/> (exclusive).</summary>
  private const int MaxExtraDegreeMaxExclusive = 6;

  /// <summary>Smallest drawn <see cref="CnfGraphBuilderSimple.MaxExtraDegreeStar"/> (inclusive).</summary>
  private const int MaxExtraDegreeStarMin = 4;
  /// <summary>Upper bound of the drawn <see cref="CnfGraphBuilderSimple.MaxExtraDegreeStar"/> (exclusive).</summary>
  private const int MaxExtraDegreeStarMaxExclusive = 9;

  /// <summary>Property dataset.</summary>
  private readonly PropMain _propMain = propMain;
  /// <summary>Supporting data.</summary>
  private readonly PropData _propData = propData;
  /// <summary>Random for sub-seed.</summary>
  private readonly SeededRandom _random = random;

  /// <summary>
  /// Resolve simple builder configuration.
  /// </summary>
  /// <returns>Simple builder configuration.</returns>
  public CnfGraphBuilderSimple Execute()
  {
    return CnfGraphBuilderSimple.Default with
    {
      ChokepointChance = _random.NextFloat(ChokepointChanceMin, ChokepointChanceMaxExclusive),
      MaxExtraDegree = _random.NextInt(MaxExtraDegreeMin, MaxExtraDegreeMaxExclusive),
      MaxExtraDegreeStar = _random.NextInt(MaxExtraDegreeStarMin, MaxExtraDegreeStarMaxExclusive),
    };
  }
}