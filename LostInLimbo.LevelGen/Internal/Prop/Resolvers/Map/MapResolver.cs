using LostInLimbo.LevelGen.Data.Config.Graph.Validator;
using LostInLimbo.LevelGen.Data.Config.Map;
using LostInLimbo.LevelGen.Data.Prop;
using LostInLimbo.LevelGen.Internal.Prop.Data;
using LostInLimbo.LevelGen.Internal.Utils.Rng;

namespace LostInLimbo.LevelGen.Internal.Prop.Resolvers.Map;

/// <summary>
/// Resolves map configuration.
/// </summary>
/// <param name="propMain">Property dataset.</param>
/// <param name="propData">Internal data supporting config generation.</param>
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
    return CnfMap.Default with
    {
      // For now nothing in map configuration.
      ValidatorOpts = ResolveValidatorOpts(),
    };
  }

  // Validator options.
  
  /// <summary>
  /// Resolve validator options.
  /// </summary>
  /// <returns>Validator options.</returns>
  private CnfValidMapOpt ResolveValidatorOpts()
  {
    return CnfValidMapOpt.Default with
    {
      Enabled = true,
    };
  }
}