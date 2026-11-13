using System;
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
  /// <exception cref="ArgumentNullException"><paramref name="propMain"/> is <see langword="null"/>.</exception>
  /// <exception cref="ArgumentOutOfRangeException">A property is out of range; see <see cref="PropMain.Validate"/>.</exception>
  public static CnfLevelGeneration Execute(PropMain propMain)
  {
    if (propMain is null) throw new ArgumentNullException(nameof(propMain));

    ConfigGenerator configGenerator = new(propMain);
    return configGenerator.Execute();
  }
}
