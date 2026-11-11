using System;

namespace LostInLimbo.LevelGen.Data.Status;

/// <summary>
/// Provides the current status of generation and other related data that allow tracking progress.
/// </summary>
/// <remarks>
/// Immutable snapshot: replace the whole instance instead of mutating it, so the status can be
/// handed to another thread safely.
/// <para>Lifecycle: <see cref="EnGenResult.None"/> (not run yet) → <see cref="EnGenResult.Running"/> →
/// <see cref="EnGenResult.Paused"/> (optional) → <see cref="EnGenResult.Finished"/>,
/// <see cref="EnGenResult.ValidationError"/>, <see cref="EnGenResult.Error"/> or
/// <see cref="EnGenResult.Cancelled"/>.</para>
/// </remarks>
public sealed record GenerationStatus
{
  // Defaults.

  /// <summary>Default instance: never ran, no error.</summary>
  public static GenerationStatus Default { get; } = new();

  // Fields.

  /// <summary>
  /// Current lifecycle state and result of generation.
  /// </summary>
  public EnGenResult Result { get; init; } = EnGenResult.None;

  /// <summary>
  /// Completion progress: from 0.0 to 1.0.
  /// </summary>
  /// <remarks>
  /// For consumers, so they can show progress bar directly using this value. For example, 0.37 would mean generation is 37% complete.
  /// </remarks>
  public float Completion { get; init; } = 0.0f;

  /// <summary>
  /// Exception that terminated generation.
  /// Invariant: non-<see langword="null"/> if and only if <see cref="Result"/> is <see cref="EnGenResult.Error"/>.
  /// Stays <see langword="null"/> for <see cref="EnGenResult.Cancelled"/>, which is a requested
  /// outcome, not a failure, and for <see cref="EnGenResult.ValidationError"/>, which reports its
  /// cause through <see cref="FailureReason"/> instead of an exception.
  /// </summary>
  public Exception? Error { get; init; }

  /// <summary>
  /// Reason why generation failed validation, as reported by the failing validator.
  /// Invariant: non-<see langword="null"/> if and only if <see cref="Result"/> is
  /// <see cref="EnGenResult.ValidationError"/>. Stays <see langword="null"/> for every other
  /// result, including <see cref="EnGenResult.Error"/>, whose cause is carried by
  /// <see cref="Error"/> instead.
  /// </summary>
  public string? FailureReason { get; init; }
}
