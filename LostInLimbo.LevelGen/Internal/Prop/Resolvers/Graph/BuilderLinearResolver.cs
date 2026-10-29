using LostInLimbo.LevelGen.Data.Config.Graph.Builder;
using LostInLimbo.LevelGen.Data.Prop;
using LostInLimbo.LevelGen.Internal.Utils;

namespace LostInLimbo.LevelGen.Internal.Prop.Resolvers.Graph;

/// <summary>
/// Resolves linear graph builder configuration.
/// </summary>
/// <param name="propMain">Property dataset.</param>
/// <param name="propData">Internal data supporting config generation.</param>
/// <param name="random">Random for sub-seed.</param>
internal sealed class BuilderLinearResolver(PropMain propMain, PropData propData, SeededRandom random)
{
  /// <summary>Property dataset.</summary>
  private readonly PropMain _propMain = propMain;
  /// <summary>Supporting data.</summary>
  private PropData _propData = propData;
  /// <summary>Random for sub-seed.</summary>
  private readonly SeededRandom _random = random;

  /// <summary>
  /// Resolve linear builder configuration.
  /// </summary>
  /// <returns>Linear builder configuration.</returns>
  public CnfGraphBuilderLinear Execute()
  {
    // TODO: set actual random varying values.
    return CnfGraphBuilderLinear.Default with
    {
      MaxEdges = 5,
      MaxExtraDegree = 2,
      MaxConditionPerRel = 3,
      MaxConditionDistance = 3,
      MaxSkipDistance = 2,
    };
  }
}