using System;

namespace LostInLimbo.LevelGen.Data.Config;

/// <summary>
/// Debug configuration data.
/// </summary>
/// <remarks>
/// Used in tests and development for debugging. Not used in the actual game.
/// </remarks>
public sealed record CnfDebug
{
  /// <summary>Default placeholder sleep in milliseconds.</summary>
  public const int DefaultPlaceholderSleep = 0;

  /// <summary>Default number of steps the placeholder generation loop executes.</summary>
  public const int DefaultPlaceholderSteps = 10;

  // Defaults.

  /// <summary>Default instance.</summary>
  public static CnfDebug Default { get; } = new();

  // Fields.

  /// <summary>
  /// If true, throw an exception already at the start.
  /// </summary>
  public bool FailAtStart { get; init; } = false;

  /// <summary>
  /// If true, the graph build reports failure, which fails the whole run.
  /// </summary>
  public bool FailGraphBuild { get; init; } = false;

  /// <summary>
  /// How long the placeholder generation pretends to work, in milliseconds.
  /// 0 means it returns immediately. Debug only; real passes will not sleep.
  /// The value is split evenly across <see cref="PlaceholderSteps"/> steps, so the
  /// per-step sleep is this value divided by the step count (integer division).
  /// </summary>
  public int PlaceholderSleep { get; init; } = DefaultPlaceholderSleep;

  /// <summary>
  /// How many steps the placeholder generation loop runs before finishing.
  /// Each run kind advances a different number of steps: All runs all remaining steps,
  /// Area and Step run a single step and then pause. Must be at least 1.
  /// </summary>
  public int PlaceholderSteps { get; init; } = DefaultPlaceholderSteps;

  // Helpers.

  /// <summary>
  /// Checks that this config can be used.
  /// </summary>
  /// <exception cref="ArgumentOutOfRangeException">A numeric setting is out of range.</exception>
  public void Validate()
  {
    if (PlaceholderSleep < 0)
      throw new ArgumentOutOfRangeException(nameof(PlaceholderSleep), PlaceholderSleep, "Placeholder sleep must be >= 0 milliseconds.");

    if (PlaceholderSteps < 1)
      throw new ArgumentOutOfRangeException(nameof(PlaceholderSteps), PlaceholderSteps, "Placeholder steps must be >= 1.");
  }
}
