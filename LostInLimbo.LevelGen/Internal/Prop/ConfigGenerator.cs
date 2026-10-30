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
/// <param name="propMain">Property dataset.</param>
internal sealed class ConfigGenerator
{
  /// <summary>Salt for the resolver sub-seed; keeps this pass off other passes' streams.</summary>
  private const long SaltResolver = 0x63666767656e6572; // "cfggener"

  /// <summary>Property dataset.</summary>
  private readonly PropMain _propMain;
  /// <summary>Supporting data.</summary>
  private readonly PropData _propData;

  /// <summary>Random for sub-seed.</summary>
  private readonly SeededRandom _random;

  public ConfigGenerator(PropMain propMain)
  {
    _propMain = propMain;
    _propMain.Validate();
    
    long seed = ResolveSeed();
    _random = new(SeedDerivation.Derive(seed, SaltResolver));
    _propData = ResolvePropData(seed);
  }

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
  /// Resolve seed.
  /// </summary>
  /// <returns>Seed.</returns>
  private long ResolveSeed()
  {
    return DateTime.UtcNow.Ticks - 1000000; // Prevent collision with actual config seed.
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
