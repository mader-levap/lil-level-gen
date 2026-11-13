namespace LostInLimbo.LevelGen.Data.Config.Graph.Validator;

/// <summary>
/// Options controlling graph validation: the validation step at the end of graph generation and
/// <see cref="Api.LevelGraphValidation"/>.
/// </summary>
public sealed record CnfValidGraphOpt
{
  // Defaults.

  /// <summary>Default instance.</summary>
  public static CnfValidGraphOpt Default { get; } = new();

  /// <summary>Default with validation enabled.</summary>
  public static CnfValidGraphOpt DefEnabled { get; } = Default with { Enabled = true };

  // Fields.

  /// <summary>
  /// If true, perform graph validation when graph generation is finished.
  /// </summary>
  public bool Enabled { get; init; } = false;

  /// <summary>
  /// Whether entering a one-way passage whose far side cannot reach the Exit is allowed.
  /// When <see langword="false"/>, such a one-way is always reported as a softlock, even if
  /// the dead end holds no important POI and the level stays winnable without crossing it.
  /// Defaults to <see langword="true"/> to preserve existing validation behavior.
  /// </summary>
  public bool AllowOneWayDeadEnds { get; init; } = true;

  // Helpers.

  /// <summary>
  /// Checks that these options can be used for validation. Every combination of the current
  /// flags is valid; the method exists so the parent config validates every section the same way.
  /// </summary>
  public void Validate()
  {
  }
}
