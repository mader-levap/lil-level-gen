using System;
using LostInLimbo.LevelGen.Data.Config;
using LostInLimbo.LevelGen.Data.Config.Graph;
using LostInLimbo.LevelGen.Data.Config.Map;
using LostInLimbo.LevelGen.Data.Config.Style;
using LostInLimbo.LevelGen.Data.Prop;
using LostInLimbo.LevelGen.Internal.Prop.Data;
using LostInLimbo.LevelGen.Internal.Prop.Resolvers;
using LostInLimbo.LevelGen.Internal.Prop.Resolvers.Graph;
using LostInLimbo.LevelGen.Internal.Prop.Resolvers.Map;
using LostInLimbo.LevelGen.Internal.Utils.Rng;

namespace LostInLimbo.LevelGen.Internal.Prop;

/// <summary>
/// Main class handling config generation.
/// </summary>
internal sealed class ConfigGenerator
{
  /// <summary>Salt for the resolver sub-seed; keeps this pass off other passes' streams.</summary>
  private const long SaltResolver = 0x63666767656e6572; // "cfggener"

  /// <summary>
  /// Salt deriving the internal supporting seed from the text seed; keeps it distinct
  /// from <see cref="CnfGeneral.Seed"/>, which is the raw text-seed hash.
  /// </summary>
  private const long SaltPropSeed = 0x70726F7073656564; // "propseed"

  /// <summary>Property dataset.</summary>
  private readonly PropMain _propMain;
  /// <summary>Supporting data.</summary>
  private readonly PropData _propData;

  /// <summary>Random for sub-seed.</summary>
  private readonly SeededRandom _random;

  /// <summary>
  /// Creates a generator whose internal seed comes from <see cref="PropMain.TextSeed"/>,
  /// or from the clock when no text seed is given.
  /// </summary>
  /// <remarks>
  /// With a text seed, the generated configuration is deterministic for the same
  /// property dataset. Without one, every run produces a different configuration.
  /// </remarks>
  /// <param name="propMain">Property dataset. Validated on construction.</param>
  public ConfigGenerator(PropMain propMain)
  {
    _propMain = propMain;
    _propMain.Validate();

    long seed = ResolveSeed();
    _random = new(SeedDerivation.Derive(seed, SaltResolver));
    _propData = ResolvePropData(seed);
  }

  /// <summary>
  /// Creates a generator with an explicit internal seed (test seam).
  /// </summary>
  /// <remarks>
  /// The explicit seed overrides <see cref="PropMain.TextSeed"/> for the internal seed;
  /// the text seed still determines <see cref="CnfGeneral.Seed"/>. Deterministic for
  /// the same inputs when a text seed is given.
  /// </remarks>
  /// <param name="propMain">Property dataset. Validated on construction.</param>
  /// <param name="seed">Internal supporting seed.</param>
  public ConfigGenerator(PropMain propMain, long seed)
  {
    _propMain = propMain;
    _propMain.Validate();

    _random = new(SeedDerivation.Derive(seed, SaltResolver));
    _propData = ResolvePropData(seed);
  }

  /// <summary>
  /// Resolve supporting data.
  /// </summary>
  /// <returns>Supporting data.</returns>
  private PropData ResolvePropData(long seed)
  {
    return new PropData
    {
      Seed = seed,
      Size = ResolveSize(),
    };
  }

  /// <summary>
  /// Resolve internal supporting seed from the text seed, or from the current time if no
  /// text seed is given.
  /// </summary>
  /// <remarks>
  /// Null, empty and whitespace-only text seeds all count as "no text seed given",
  /// matching <see cref="GeneralResolver"/>. The clock fallback is intentional
  /// config-generation behavior; generation itself stays deterministic for the resolved config.
  /// </remarks>
  /// <returns>Seed.</returns>
  private long ResolveSeed()
  {
    var textSeed = _propMain.TextSeed;
    return string.IsNullOrWhiteSpace(textSeed)
      ? DateTime.UtcNow.Ticks
      : SeedDerivation.Derive(SeedDerivation.FromText(textSeed), SaltPropSeed);
  }

  /// <summary>
  /// Resolve size of the graph.
  /// </summary>
  /// <returns>Size of the graph.</returns>
  private int ResolveSize()
  {
    int from = 2;
    int to = _propMain.Difficulty + 3;
    return _random.NextInt(from, to);
  }

  // ///////////////////////////////////////////////////////////////////////////

  /// <summary>
  /// Create generation configuration based on given property dataset.
  /// </summary>
  /// <returns>Generated configuration.</returns>
  public CnfLevelGeneration Execute()
  {
    return CnfLevelGeneration.Default with
    {
      General = ResolveGeneral(),
      Style = ResolveStyle(),
      Graph = ResolveGraph(),
      Map = ResolveMap(),
      // Debug is null
    };
  }

  /// <summary>
  /// Resolve general configuration.
  /// </summary>
  /// <returns>Generated general configuration.</returns>
  private CnfGeneral ResolveGeneral()
  {
    GeneralResolver generalResolver = new(_propMain, _propData);
    return generalResolver.Execute();
  }

  /// <summary>
  /// Resolve style configuration.
  /// </summary>
  /// <returns>Generated style configuration.</returns>
  private CnfStyle ResolveStyle()
  {
    StyleResolver styleResolver = new(_propMain, _propData);
    return styleResolver.Execute();
  }

  /// <summary>
  /// Resolve graph configuration.
  /// </summary>
  /// <returns>Generated graph configuration.</returns>
  private CnfGraph ResolveGraph()
  {
    GraphResolver graphResolver = new(_propMain, _propData);
    return graphResolver.Execute();
  }

  /// <summary>
  /// Resolve map configuration.
  /// </summary>
  /// <returns>Generated map configuration.</returns>
  private CnfMap ResolveMap()
  {
    MapResolver mapResolver = new(_propMain, _propData);
    return mapResolver.Execute();
  }
}
