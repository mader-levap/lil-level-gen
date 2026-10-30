using LostInLimbo.LevelGen.Data.Config.Style;
using LostInLimbo.LevelGen.Data.Prop;
using LostInLimbo.LevelGen.Internal.Prop.Data;
using LostInLimbo.LevelGen.Internal.Utils.Rng;

namespace LostInLimbo.LevelGen.Internal.Prop.Resolvers;

/// <summary>
/// Resolves style configuration.
/// </summary>
/// <param name="propMain">Property dataset.</param>
/// <param name="propData">Internal data supporting config generation.</param>
internal sealed class StyleResolver(PropMain propMain, PropData propData)
{
  /// <summary>Salt for the style resolver sub-seed; keeps this pass off other passes' streams.</summary>
  private const long SaltStyleResolver = 0x6366677374796C65; // "cfgstyle"

  /// <summary>Property dataset.</summary>
  private readonly PropMain _propMain = propMain;
  /// <summary>Supporting data.</summary>
  private PropData _propData = propData;

  /// <summary>Random for sub-seed.</summary>
  private readonly SeededRandom _random = new(SeedDerivation.Derive(propData.Seed, SaltStyleResolver));

  /// <summary>
  /// Resolve style configuration based on given property dataset.
  /// </summary>
  /// <returns>Resolved style configuration.</returns>
  public CnfStyle Execute()
  {
    return CnfStyle.Default with
    {
      // For now nothing in style configuration.      
    };
  }
}