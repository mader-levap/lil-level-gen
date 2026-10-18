using System;

namespace LostInLimbo.LevelGen.Data.Config.Style;

/// <summary>
/// <para>Style configuration data.</para>
/// </summary>
/// <remarks>
/// Currently placeholder.
/// </remarks>
public sealed record CnfStyle
{
  // Constants here

  //

  /// <summary>Default instance.</summary>
  public static CnfStyle Default { get; } = new();

  // actual fields here

  //

  /// <summary>
  /// Checks that this config can be used for generation.
  /// </summary>
  /// <exception cref="ArgumentOutOfRangeException">The builder is not a defined enum value, or a value is out of range.</exception>
  public void Validate()
  {
  }
}