using LostInLimbo.LevelGen.Data.Config;
using LostInLimbo.LevelGen.Data.Prop;
using LostInLimbo.LevelGen.Internal.Prop;

namespace LostInLimbo.LevelGen.Api;

/// <summary>
/// Generates full configuration dataset based on given property dataset.
/// </summary>
public static class LevelGenConfig
{
  /// <summary>
  /// Create generation configuration based on given property dataset.
  /// </summary>
  /// <remarks>
  /// Important: exactly same property dataset can and WILL generate different configuration (within constraints defined in properties).
  /// It is entire point of config generator.
  /// </remarks>
  /// <param name="propMain">Property dataset.</param>
  /// <returns>Generated configuration.</returns>
  public static CnfLevelGeneration Execute(PropMain propMain)
  {
    ConfigGenerator configGenerator = new(propMain);
    return configGenerator.Execute();
  }
}
