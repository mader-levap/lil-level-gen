using System;
using LostInLimbo.LevelGen.Data.Config;
using LostInLimbo.LevelGen.Data.Prop;
using LostInLimbo.LevelGen.Internal.Prop.Data;
using LostInLimbo.LevelGen.Internal.Utils.Rng;

namespace LostInLimbo.LevelGen.Internal.Prop.Resolvers;

/// <summary>
/// Resolves general configuration.
/// </summary>
/// <param name="propMain">Property dataset.</param>
/// <param name="propData">Internal data supporting config generation.</param>
internal sealed class GeneralResolver(PropMain propMain, PropData propData)
{
  /// <summary>Salt for the general resolver sub-seed; keeps this pass off other passes' streams.</summary>
  private const long SaltGeneralResolver = 0x63666767656E; // "cfggen"

  /// <summary>Center of the degeneration range per difficulty step.</summary>
  private const float DegenerationPerDifficulty = 10.0f;

  /// <summary>Half-width of the degeneration range around its center.</summary>
  private const float DegenerationSpread = 20.0f;

  /// <summary>Lowest allowed degeneration; the range is clamped to it.</summary>
  private const float MinDegeneration = 0.0f;

  /// <summary>Property dataset.</summary>
  private readonly PropMain _propMain = propMain;
  /// <summary>Supporting data.</summary>
  private readonly PropData _propData = propData;

  /// <summary>Random for sub-seed.</summary>
  private readonly SeededRandom _random = new(SeedDerivation.Derive(propData.Seed, SaltGeneralResolver));

  /// <summary>
  /// Resolve general configuration based on given property dataset.
  /// </summary>
  /// <returns>Resolved general configuration.</returns>
  public CnfGeneral Execute()
  {
    return CnfGeneral.Default with
    {
      Seed = ResolveSeed(),
      Degeneration = ResolveDegeneration(),
    };
  }

  // Field resolvers.

  /// <summary>
  /// Resolve the world seed. It is resolved once by <see cref="ConfigGenerator"/> (text-seed hash
  /// or clock, see <see cref="PropMain.TextSeed"/>) and passed in through
  /// <see cref="PropData.WorldSeed"/>; this resolver never reads the clock itself.
  /// </summary>
  /// <returns>Seed.</returns>
  private long ResolveSeed()
  {
    return _propData.WorldSeed;
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
    float baseValue = _propMain.Difficulty * DegenerationPerDifficulty;
    float from = Math.Max(MinDegeneration, baseValue - DegenerationSpread);
    float to = baseValue + DegenerationSpread;
    return _random.NextFloat(from, to);
  }
}