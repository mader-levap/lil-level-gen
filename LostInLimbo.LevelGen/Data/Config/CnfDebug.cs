namespace LostInLimbo.LevelGen.Data.Config;

/// <summary>
/// Debug configuration data.
/// </summary>
/// <remarks>
/// Used in tests and development for debugging. Not used in actual game.
/// </remarks>
public sealed record CnfDebug
{
  /// <summary>Default instance.</summary>
  public static CnfDebug Default { get; } = new();

  /// <summary>
  /// If true, throw exception already at start.
  /// </summary>
  public bool FailAtStart { get; init; } = false;
}