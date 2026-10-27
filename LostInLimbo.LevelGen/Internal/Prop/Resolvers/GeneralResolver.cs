using System;
using LostInLimbo.LevelGen.Data.Config;
using LostInLimbo.LevelGen.Data.Prop;
using LostInLimbo.LevelGen.Internal.Utils;

namespace LostInLimbo.LevelGen.Internal.Prop.Resolvers;

/// <summary>
/// Resolves general configuration.
/// </summary>
/// <param name="propMain">Property dataset.</param>
internal sealed class GeneralResolver(PropMain propMain, PropData propData)
{
  /// <summary>Salt for the general resolver sub-seed; keeps this pass off other passes' streams.</summary>
  private const long SaltGeneralResolver = 0x63666767656E; // "cfggen"

  /// <summary>Property dataset.</summary>
  private readonly PropMain _propMain = propMain;
  /// <summary>Supporting data.</summary>
  private PropData _propData = propData;

  /// <summary>Random for sub-seed.</summary>
  private readonly SeededRandom _random = new(SeedDerivation.Derive(propData.Seed, SaltGeneralResolver));

  /// <summary>
  /// Resolve general configuration based on given property dataset.
  /// </summary>
  /// <returns>Resolved general configuration.</returns>
  public CnfGeneral Execute()
  {
    return new CnfGeneral
    {
      Seed = ResolveSeed(),
      Degeneration = ResolveDegeneration(),
    };
  }

  /// <summary>
  /// Resolve seed from the text seed, or from the current time if no text seed is given.
  /// </summary>
  /// <returns>Seed.</returns>
  /// <remarks>
  /// Null, empty and whitespace-only text seeds all count as "no text seed given".
  /// The clock fallback is intentional config-generation behavior (see <see cref="PropMain.TextSeed"/>):
  /// the timestamp is baked into <see cref="CnfGeneral.Seed"/> here, before generation starts,
  /// so generation itself stays deterministic for the resolved seed.
  /// </remarks>
  private long ResolveSeed()
  {
    var textSeed = _propMain.TextSeed;
    return string.IsNullOrWhiteSpace(textSeed)
      ? DateTime.UtcNow.Ticks
      : SeedDerivation.FromText(textSeed);
  }

  /// <summary>
  /// Resolve degeneration level. Scales strictly with difficulty, though there is still high variance.
  /// </summary>
  /// <remarks>
  /// Treats <see cref="PropMain.Difficulty"/> as step: each step has its own range from - to and actual
  /// degeneration is chosen from that range. Lower value is capped at 0.0.
  /// </remarks>
  /// <returns>Degeneration level.</returns>
  private float ResolveDegeneration()
  {
    float baseValue = _propMain.Difficulty * 10.0f;
    float from = Math.Max(0.0f, baseValue - 20.0f);
    float to = baseValue + 20.0f;
    return _random.NextFloat(from, to);
  }
}