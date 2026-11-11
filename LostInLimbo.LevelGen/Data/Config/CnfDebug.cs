using System;
using LostInLimbo.LevelGen.Data.Result.Stats;

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
  /// If true, graph validation reports failure after a successful build, which ends the run
  /// with <see cref="Data.Status.EnGenResult.ValidationError"/> and exposes the built graph.
  /// Independent of <see cref="Graph.Validator.CnfValidGraphOpt.Enabled"/>: the deliberate
  /// failure is reported even when validation is disabled.
  /// </summary>
  public bool FailGraphValidation { get; init; } = false;

  /// <summary>
  /// If not null, end generation after given phase finishes as if it was paused (at beginning of next phase).
  /// </summary>
  /// <remarks>
  /// <para>
  /// The run reports <see cref="Status.EnGenResult.Paused"/> and publishes its world, whose
  /// phase is the next one (e.g. <see cref="EnPhase.Map"/> with a complete graph and no map
  /// steps for <see cref="EnPhase.Graph"/>). Lets tests skip the work of later phases.
  /// </para>
  /// <para>
  /// The stop fires once, when the phase finishes: resuming continues normally. A validation
  /// failure still wins, as the phase did not finish. <see cref="EnPhase.Finalization"/> and
  /// <see cref="EnPhase.Completed"/> have no effect - the run finishes as usual.
  /// </para>
  /// </remarks>
  public EnPhase? EndAtPhase { get; init; } = null;

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
  /// <exception cref="ArgumentOutOfRangeException">A numeric setting is out of range or <see cref="EndAtPhase"/> is not a defined phase.</exception>
  public void Validate()
  {
    if (PlaceholderSleep < 0)
      throw new ArgumentOutOfRangeException(nameof(PlaceholderSleep), PlaceholderSleep, "Placeholder sleep must be >= 0 milliseconds.");

    if (PlaceholderSteps < 1)
      throw new ArgumentOutOfRangeException(nameof(PlaceholderSteps), PlaceholderSteps, "Placeholder steps must be >= 1.");

    if (EndAtPhase is { } endAtPhase && !Enum.IsDefined(typeof(EnPhase), endAtPhase))
      throw new ArgumentOutOfRangeException(nameof(EndAtPhase), endAtPhase, "End-at phase must be a defined phase or null.");
  }
}
