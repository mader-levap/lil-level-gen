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
  /// <summary>Property dataset.</summary>
  private readonly PropMain _propMain = propMain;
  /// <summary>Supporting data.</summary>
  private PropData _propData = propData;
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
      ChokepointChance = _random.NextFloat(0.2f, 0.95f),
      MaxExtraDegree = _random.NextInt(2, 6),
      MaxExtraDegreeStar = _random.NextInt(4, 9),
    };
  }
}