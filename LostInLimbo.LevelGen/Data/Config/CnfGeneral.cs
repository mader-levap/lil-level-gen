using System;

namespace LostInLimbo.LevelGen.Data.Config;

/// <summary>
/// General configuration data.
/// </summary>
/// <remarks>
/// Data here has critical impact on generation.
/// </remarks>
public sealed record CnfGeneral
{
  /// <summary>Default seed.</summary>
  public const int DefaultSeed = 0;

  /// <summary>Default corruption degree - no corruption at all.</summary>
  public const float DefaultCorruption = 0.0f;

  //

  /// <summary>Default instance.</summary>
  public static CnfGeneral Default { get; } = new();

  /// <summary>
  /// World seed from which all other seeds are derived deterministically.
  /// Two runs with the same configuration produce identical output.
  /// </summary>
  public long Seed { get; init; } = DefaultSeed;

  /// <summary>
  /// Corruption degree. The more world is corrupted, the worse are effects on map generation.
  /// 0 means no corruption.
  /// </summary>
  public float Corruption { get; init; } = DefaultCorruption;

  //

  /// <summary>
  /// Checks that this config can be used for generation.
  /// </summary>
  /// <exception cref="ArgumentOutOfRangeException">Number is outside range.</exception>
  public void Validate()
  {
    if (!float.IsFinite(Corruption) || Corruption < 0.0f)
      throw new ArgumentOutOfRangeException(nameof(Corruption), Corruption, "Corruption degree must be a finite number >= 0.");
  }
}
