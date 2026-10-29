namespace LostInLimbo.LevelGen.Data.Config.Graph.Validator;

/// <summary>
/// Options controlling <see cref="MapValidator"/>. Currently PLACEHOLDER.
/// </summary>
public sealed record CnfValidMapOpt
{
  // Defaults.

  /// <summary>Default instance.</summary>
  public static CnfValidMapOpt Default { get; } = new();

  /// <summary>Default with validation enabled.</summary>
  public static CnfValidMapOpt DefEnabled { get; } = Default with { Enabled = true };

  // Fields.

  /// <summary>
  /// If true, perform map validation when map generation is finished.
  /// </summary>
  public bool Enabled { get; init; } = false;
}
