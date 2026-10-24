using System;
using LostInLimbo.LevelGen.Data.Config.Graph.Validator;

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

  // Defaults.

  /// <summary>Default instance.</summary>
  public static CnfMap Default { get; } = new();

  // Fields.

  // actual fields here

  /// <summary>
  /// Validator options.
  /// </summary>
  public CnfValidMapOpt ValidatorOpts { get; init; } = CnfValidMapOpt.Default;

  // Helpers.

  /// <summary>
  /// Checks that this map generation config can be used for map generation.
  /// </summary>
  public void Validate()
  {
    if (ValidatorOpts is null)
      throw new ArgumentException("ValidatorOpts config must not be null.", nameof(ValidatorOpts));
  }
}