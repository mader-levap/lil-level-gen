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

namespace LostInLimbo.LevelGen.Internal.Prop;

/// <summary>
/// Main class handling config generation.
/// </summary>
/// <param name="propMain">Property dataset.</param>
internal sealed class ConfigGenerator
{
  /// <summary>Property dataset.</summary>
  private readonly PropMain _propMain;
  /// <summary>Supporting data.</summary>
  private PropData _propData;

  public ConfigGenerator(PropMain propMain)
  {
    _propMain = propMain;
    _propMain.Validate();
    
    _propData = ResolvePropData();
  }

  public ConfigGenerator(PropMain propMain, PropData propData)
  {
    _propMain = propMain;
    _propMain.Validate();
    
    _propData = propData;
  }

  /// <summary>
  /// Resolve supporting data.
  /// </summary>
  /// <returns>Supporting data.</returns>
  private PropData ResolvePropData()
  {
    return new PropData
    {
      Seed = DateTime.UtcNow.Ticks - 1000000, // Prevent collision with actual config seed.
    };
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
