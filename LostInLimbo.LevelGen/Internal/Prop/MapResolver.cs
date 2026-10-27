using LostInLimbo.LevelGen.Data.Config.Map;
using LostInLimbo.LevelGen.Data.Prop;

namespace LostInLimbo.LevelGen.Internal.Prop;

/// <summary>
/// Resolves map configuration.
/// </summary>
/// <param name="propMain">Property dataset.</param>
internal sealed class MapResolver(PropMain propMain)
{
  /// <summary>Property dataset.</summary>
  private readonly PropMain _propMain = propMain;

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