using LostInLimbo.LevelGen.Data.Config;
using LostInLimbo.LevelGen.Data.Prop;

namespace LostInLimbo.LevelGen.Internal.Prop;

/// <summary>
/// Resolves general configuration.
/// </summary>
/// <param name="propMain">Property dataset.</param>
internal sealed class GeneralResolver(PropMain propMain)
{
  /// <summary>Property dataset.</summary>
  private readonly PropMain _propMain = propMain;

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
  /// Resolve seed based on text seed or time if no text seed is given.
  /// </summary>
  /// <returns>Seed.</returns>
  private long ResolveSeed()
  {
    return 0L;
  }

  /// <summary>
  /// Resolve degeneration level. Scales strictly with difficulty, though there is still high variance.
  /// </summary>
  /// <returns>Degeneration level.</returns>
  private float ResolveDegeneration()
  {
    return 0.0f;
  }
}