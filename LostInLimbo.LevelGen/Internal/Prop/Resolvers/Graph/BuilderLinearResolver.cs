using LostInLimbo.LevelGen.Data.Config.Graph.Builder;
using LostInLimbo.LevelGen.Data.Prop;
using LostInLimbo.LevelGen.Internal.Prop.Data;
using LostInLimbo.LevelGen.Internal.Utils.Rng;

namespace LostInLimbo.LevelGen.Internal.Prop.Resolvers.Graph;

/// <summary>
/// Resolves linear graph builder configuration.
/// </summary>
/// <param name="propMain">Property dataset.</param>
/// <param name="propData">Internal data supporting config generation.</param>
internal sealed class BuilderLinearResolver(PropMain propMain, PropData propData)
{
  /// <summary>Salt for the linear builder resolver sub-seed; keeps its draws off the graph resolver's stream.</summary>
  private const long SaltBuilderLinearResolver = 0x636667626C696E72; // "cfgblinr"

  // Draw ranges: each pair is [Min, MaxExclusive).

  /// <summary>Smallest drawn <see cref="CnfGraphBuilderLinear.MaxEdges"/> (inclusive).</summary>
  private const int MaxEdgesMin = 3;
  /// <summary>Upper bound of the drawn <see cref="CnfGraphBuilderLinear.MaxEdges"/> (exclusive).</summary>
  private const int MaxEdgesMaxExclusive = 7;

  /// <summary>Smallest drawn <see cref="CnfGraphBuilderLinear.MaxExtraDegree"/> (inclusive).</summary>
  private const int MaxExtraDegreeMin = 2;
  /// <summary>Upper bound of the drawn <see cref="CnfGraphBuilderLinear.MaxExtraDegree"/> (exclusive).</summary>
  private const int MaxExtraDegreeMaxExclusive = 5;

  /// <summary>Smallest drawn <see cref="CnfGraphBuilderLinear.MaxConditionPerRel"/> (inclusive).</summary>
  private const int MaxConditionPerRelMin = 1;
  /// <summary>Upper bound of the drawn <see cref="CnfGraphBuilderLinear.MaxConditionPerRel"/> (exclusive).</summary>
  private const int MaxConditionPerRelMaxExclusive = 5;

  /// <summary>Smallest drawn <see cref="CnfGraphBuilderLinear.MaxConditionDistance"/> (inclusive).</summary>
  private const int MaxConditionDistanceMin = 2;
  /// <summary>Upper bound of the drawn <see cref="CnfGraphBuilderLinear.MaxConditionDistance"/> (exclusive).</summary>
  private const int MaxConditionDistanceMaxExclusive = 5;

  /// <summary>Smallest drawn <see cref="CnfGraphBuilderLinear.MaxSkipDistance"/> (inclusive).</summary>
  private const int MaxSkipDistanceMin = 1;
  /// <summary>Upper bound of the drawn <see cref="CnfGraphBuilderLinear.MaxSkipDistance"/> (exclusive).</summary>
  private const int MaxSkipDistanceMaxExclusive = 4;

  /// <summary>Property dataset.</summary>
  private readonly PropMain _propMain = propMain;
  /// <summary>Supporting data.</summary>
  private readonly PropData _propData = propData;
  /// <summary>Random for sub-seed.</summary>
  private readonly SeededRandom _random = new(SeedDerivation.Derive(propData.Seed, SaltBuilderLinearResolver));

  /// <summary>
  /// Resolve linear builder configuration.
  /// </summary>
  /// <returns>Linear builder configuration.</returns>
  public CnfGraphBuilderLinear Execute()
  {
    return CnfGraphBuilderLinear.Default with
    {
      MaxEdges = _random.NextInt(MaxEdgesMin, MaxEdgesMaxExclusive),
      MaxExtraDegree = _random.NextInt(MaxExtraDegreeMin, MaxExtraDegreeMaxExclusive),
      MaxConditionPerRel = _random.NextInt(MaxConditionPerRelMin, MaxConditionPerRelMaxExclusive),
      MaxConditionDistance = _random.NextInt(MaxConditionDistanceMin, MaxConditionDistanceMaxExclusive),
      MaxSkipDistance = _random.NextInt(MaxSkipDistanceMin, MaxSkipDistanceMaxExclusive),
    };
  }
}