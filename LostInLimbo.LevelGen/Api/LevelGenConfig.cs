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
  /// Deterministic when <see cref="PropMain.TextSeed"/> is set: the same property dataset always generates
  /// identical configuration. Without a text seed, the clock is used and every call generates different
  /// configuration (within constraints defined in properties).
  /// </remarks>
  /// <param name="propMain">Property dataset.</param>
  /// <returns>Generated configuration.</returns>
  public static CnfLevelGeneration Execute(PropMain propMain)
  {
    ConfigGenerator configGenerator = new(propMain);
    return configGenerator.Execute();
  }
}
