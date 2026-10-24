namespace LostInLimbo.LevelGen.Internal.Generator.Graph.Validator;

/// <summary>
/// Options controlling how <see cref="GraphValidator"/> treats one-way dead ends.
/// </summary>
internal sealed record GraphValidatorOpt
{
  /// <summary>
  /// Whether entering a one-way passage whose far side cannot reach the Exit is allowed.
  /// When <see langword="false"/>, such a one-way is always reported as a softlock, even if
  /// the dead end holds no important POI and the level stays winnable without crossing it.
  /// Defaults to <see langword="true"/> to preserve existing validation behavior.
  /// </summary>
  public bool AllowOneWayDeadEnds { get; init; } = true;

  /// <summary>Default options: one-way dead ends are allowed.</summary>
  public static GraphValidatorOpt Default { get; } = new();
}
