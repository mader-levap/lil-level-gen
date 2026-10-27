using LostInLimbo.LevelGen.Data.Config.Graph;
using LostInLimbo.LevelGen.Data.Prop;

namespace LostInLimbo.LevelGen.Internal.Prop;

/// <summary>
/// Resolves graph configuration.
/// </summary>
/// <param name="propMain">Property dataset.</param>
internal sealed class GraphResolver(PropMain propMain)
{
  /// <summary>Property dataset.</summary>
  private readonly PropMain _propMain = propMain;

  /// <summary>
  /// Resolve graph configuration based on given property dataset.
  /// </summary>
  /// <returns>Resolved graph configuration.</returns>
  public CnfGraph Execute()
  {
    // PLACEHOLDER: for now return default.
    return CnfGraph.Default;
  }
}