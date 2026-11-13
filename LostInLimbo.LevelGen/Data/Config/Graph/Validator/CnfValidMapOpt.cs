namespace LostInLimbo.LevelGen.Data.Config.Graph.Validator;

/// <summary>
/// Options controlling map validation in the finalization phase. Currently PLACEHOLDER.
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

  // Helpers.

  /// <summary>
  /// Checks that these options can be used for validation. Every value of the current flag is
  /// valid; the method exists so the parent config validates every section the same way.
  /// </summary>
  public void Validate()
  {
  }
}
