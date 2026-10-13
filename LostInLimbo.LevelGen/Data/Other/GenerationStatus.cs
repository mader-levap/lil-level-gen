using System;

namespace LostInLimbo.LevelGen.Data.Other;

/// <summary>
/// Provides current status of generation and other related data that allow tracking progress.
/// </summary>
/// <remarks>
/// Immutable snapshot: replace the whole instance instead of mutating it, so the status can be
/// handed to another thread safely.
/// <para>Lifecycle: <see cref="EnGenResult.None"/> (not run yet) → <see cref="EnGenResult.Running"/> →
/// <see cref="EnGenResult.Finished"/> or <see cref="EnGenResult.Error"/>.</para>
/// </remarks>
public sealed record GenerationStatus
{
  /// <summary>Default instance: never ran, no error.</summary>
  public static GenerationStatus Default { get; } = new();

  //

  /// <summary>
  /// Current lifecycle state and result of generation.
  /// </summary>
  public EnGenResult Result { get; init; } = EnGenResult.None;

  /// <summary>
  /// Exception that terminated generation.
  /// Invariant: non-<see langword="null"/> if and only if <see cref="Result"/> is <see cref="EnGenResult.Error"/>.
  /// </summary>
  public Exception? Error { get; init; }
}
