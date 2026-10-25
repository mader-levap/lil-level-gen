using System;

namespace LostInLimbo.LevelGen.Internal.Generator.Graph;

/// <summary>
/// Result of <see cref="GraphGenerator.Execute"/>: the outcome of the run plus the reason of
/// a failed outcome.
/// </summary>
/// <param name="Outcome">What happened during the run; see <see cref="EnGenerationOutcome"/>.</param>
/// <param name="FailureReason">
/// Why the run failed. Non-<see langword="null"/> if and only if
/// <paramref name="Outcome"/> is not <see cref="EnGenerationOutcome.Success"/>.
/// </param>
internal sealed record GraphRunResult(EnGenerationOutcome Outcome, string? FailureReason)
{
  /// <summary>Creates a successful result.</summary>
  /// <returns>Result with <see cref="EnGenerationOutcome.Success"/> and no failure reason.</returns>
  public static GraphRunResult Succeed() => new(EnGenerationOutcome.Success, FailureReason: null);

  /// <summary>Creates a result for a failed graph build.</summary>
  /// <param name="failureReason">Why the build failed. Must not be empty.</param>
  /// <returns>Result with <see cref="EnGenerationOutcome.BuildFailed"/>.</returns>
  /// <exception cref="ArgumentException">The failure reason is null, empty or whitespace.</exception>
  public static GraphRunResult FailBuild(string failureReason)
    => new(EnGenerationOutcome.BuildFailed, RequireReason(failureReason));

  /// <summary>Creates a result for a graph rejected by validation.</summary>
  /// <param name="failureReason">Why validation failed. Must not be empty.</param>
  /// <returns>Result with <see cref="EnGenerationOutcome.ValidationFailed"/>.</returns>
  /// <exception cref="ArgumentException">The failure reason is null, empty or whitespace.</exception>
  public static GraphRunResult FailValidation(string failureReason)
    => new(EnGenerationOutcome.ValidationFailed, RequireReason(failureReason));

  /// <summary>Guards the failure reason of a failed outcome.</summary>
  /// <param name="failureReason">Reason to check.</param>
  /// <returns>The reason, unchanged.</returns>
  /// <exception cref="ArgumentException">The reason is null, empty or whitespace.</exception>
  private static string RequireReason(string failureReason)
  {
    if (string.IsNullOrWhiteSpace(failureReason))
      throw new ArgumentException("Failure reason must not be empty.", nameof(failureReason));

    return failureReason;
  }
}
