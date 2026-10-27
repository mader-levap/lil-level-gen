using System;

namespace LostInLimbo.LevelGen.Data.Config;

/// <summary>
/// General configuration data.
/// </summary>
/// <remarks>
/// Data here has a critical impact on generation.
/// </remarks>
public sealed record CnfGeneral
{
  /// <summary>Default seed.</summary>
  public const long DefaultSeed = 0L;

  /// <summary>Default degeneration degree - no degeneration at all.</summary>
  public const float DefaultDegeneration = 0.0f;

  // Defaults.

  /// <summary>Default instance.</summary>
  public static CnfGeneral Default { get; } = new();

  // Fields.

  /// <summary>
  /// World seed from which all other seeds are derived deterministically.
  /// Two runs with the same configuration produce identical output.
  /// </summary>
  public long Seed { get; init; } = DefaultSeed;

  /// <summary>
  /// Level of degeneration. The more the world is degenerated, the worse are the effects on map generation.
  /// 0.0 means no corruption.
  /// </summary>
  public float Degeneration { get; init; } = DefaultDegeneration;

  // Helpers.

  /// <summary>
  /// Checks that this config can be used for generation.
  /// </summary>
  /// <exception cref="ArgumentOutOfRangeException">The number is outside the range.</exception>
  public void Validate()
  {
    if (!float.IsFinite(Degeneration) || Degeneration < 0.0f)
      throw new ArgumentOutOfRangeException(nameof(Degeneration), Degeneration, "Degeneration must be a finite number >= 0.");
  }
}
