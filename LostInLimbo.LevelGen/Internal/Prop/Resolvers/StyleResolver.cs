using LostInLimbo.LevelGen.Data.Config.Style;
using LostInLimbo.LevelGen.Data.Prop;

namespace LostInLimbo.LevelGen.Internal.Prop.Resolvers;

/// <summary>
/// Resolves style configuration.
/// </summary>
/// <param name="propMain">Property dataset.</param>
internal sealed class StyleResolver(PropMain propMain, PropData propData)
{
  /// <summary>Property dataset.</summary>
  private readonly PropMain _propMain = propMain;
  /// <summary>Supporting data.</summary>
  private PropData _propData = propData;

  /// <summary>
  /// Resolve style configuration based on given property dataset.
  /// </summary>
  /// <returns>Resolved style configuration.</returns>
  public CnfStyle Execute()
  {
    // PLACEHOLDER: for now return default.
    return CnfStyle.Default;
  }
}