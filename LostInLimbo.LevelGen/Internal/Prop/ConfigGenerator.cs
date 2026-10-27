using LostInLimbo.LevelGen.Data.Config;
using LostInLimbo.LevelGen.Data.Config.Graph;
using LostInLimbo.LevelGen.Data.Config.Map;
using LostInLimbo.LevelGen.Data.Config.Style;
using LostInLimbo.LevelGen.Data.Prop;

namespace LostInLimbo.LevelGen.Internal.Prop;

/// <summary>
/// Main class handling config generation.
/// </summary>
/// <param name="propMain">Property dataset.</param>
internal sealed class ConfigGenerator(PropMain propMain)
{
  /// <summary>Property dataset.</summary>
  private readonly PropMain _propMain = propMain;

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
    GeneralResolver generalResolver = new(_propMain);
    return generalResolver.Execute();
  }

  /// <summary>
  /// Resolve style configuration.
  /// </summary>
  /// <returns>Generated style configuration.</returns>
  private CnfStyle ResolveStyle()
  {
    StyleResolver styleResolver = new(_propMain);
    return styleResolver.Execute();
  }

  /// <summary>
  /// Resolve graph configuration.
  /// </summary>
  /// <returns>Generated graph configuration.</returns>
  private CnfGraph ResolveGraph()
  {
    GraphResolver graphResolver = new(_propMain);
    return graphResolver.Execute();
  }

  /// <summary>
  /// Resolve map configuration.
  /// </summary>
  /// <returns>Generated map configuration.</returns>
  private CnfMap ResolveMap()
  {
    MapResolver mapResolver = new(_propMain);
    return mapResolver.Execute();
  }
}
