using LostInLimbo.LevelGen.Data.Config.Map;
using LostInLimbo.LevelGen.Data.Prop;
using LostInLimbo.LevelGen.Internal.Utils;

namespace LostInLimbo.LevelGen.Internal.Prop.Resolvers;

/// <summary>
/// Resolves map configuration.
/// </summary>
/// <param name="propMain">Property dataset.</param>
internal sealed class MapResolver(PropMain propMain, PropData propData)
{
  /// <summary>Salt for the map resolver sub-seed; keeps this pass off other passes' streams.</summary>
  private const long SaltMapResolver = 0x6366676D6170; // "cfgmap"

  /// <summary>Property dataset.</summary>
  private readonly PropMain _propMain = propMain;
  /// <summary>Supporting data.</summary>
  private PropData _propData = propData;

  /// <summary>Random for sub-seed.</summary>
  private readonly SeededRandom _random = new(SeedDerivation.Derive(propData.Seed, SaltMapResolver));

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