using LostInLimbo.LevelGen.Data.Config.Map;
using LostInLimbo.LevelGen.Data.Prop;

namespace LostInLimbo.LevelGen.Internal.Prop.Resolvers;

/// <summary>
/// Resolves map configuration.
/// </summary>
/// <param name="propMain">Property dataset.</param>
internal sealed class MapResolver(PropMain propMain, PropData propData)
{
  /// <summary>Property dataset.</summary>
  private readonly PropMain _propMain = propMain;
  /// <summary>Supporting data.</summary>
  private PropData _propData = propData;

  /// <summary>
  /// Resolve map configuration based on given property dataset.
  /// </summary>
  /// <returns>Resolved map configuration.</returns>
  public CnfMap Execute()
  {
    // PLACEHOLDER: for now return default.
    return CnfMap.Default;
  }
}