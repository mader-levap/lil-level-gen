using System;

namespace LostInLimbo.LevelGen.Data.Config;

/// <summary>
/// Debug configuration data.
/// </summary>
/// <remarks>
/// Used in tests and development for debugging. Not used in actual game.
/// </remarks>
public sealed record CnfDebug
{
  /// <summary>Default placeholder sleep in milliseconds. Matches the built-in placeholder duration.</summary>
  public const int DefaultPlaceholderSleep = 3000;

  //

  /// <summary>Default instance.</summary>
  public static CnfDebug Default { get; } = new();

  /// <summary>
  /// If true, throw exception already at start.
  /// </summary>
  public bool FailAtStart { get; init; } = false;

  /// <summary>
  /// How long the placeholder generation pretends to work, in milliseconds.
  /// 0 means it returns immediately. Debug only; real passes will not sleep.
  /// </summary>
  public int PlaceholderSleep { get; init; } = DefaultPlaceholderSleep;

  //

  /// <summary>
  /// Checks that this config can be used.
  /// </summary>
  /// <exception cref="ArgumentOutOfRangeException">A numeric setting is out of range.</exception>
  public void Validate()
  {
    if (PlaceholderSleep < 0)
      throw new ArgumentOutOfRangeException(nameof(PlaceholderSleep), PlaceholderSleep, "Placeholder sleep must be >= 0 milliseconds.");
  }
}
