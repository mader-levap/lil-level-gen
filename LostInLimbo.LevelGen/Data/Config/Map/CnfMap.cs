using System;

namespace LostInLimbo.LevelGen.Data.Config.Map;

/// <summary>
/// <para>Map configuration data.</para>
/// </summary>
/// <remarks>
/// <para>
/// Currently placeholder.
/// </para>
/// </remarks>
public sealed record CnfMap
{
  // Constants here

  //

  /// <summary>Default instance.</summary>
  public static CnfMap Default { get; } = new();

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