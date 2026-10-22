using System;

namespace LostInLimbo.LevelGen.Api;

/// <summary>
/// Outcome of a level graph validation pass: a success flag plus a human-readable
/// description of the failed check when validation did not succeed.
/// </summary>
/// <remarks>
/// Instances are immutable and can only be created through <see cref="Succeed"/> and
/// <see cref="Fail"/>, so an inconsistent combination (failure without a reason) cannot exist.
/// </remarks>
public sealed record GraphValidationResult
{
  /// <summary>
  /// Initializes a new instance of the <see cref="GraphValidationResult"/> record.
  /// </summary>
  /// <param name="success">Value for <see cref="Success"/>.</param>
  /// <param name="failureReason">Value for <see cref="FailureReason"/>.</param>
  private GraphValidationResult(bool success, string? failureReason)
  {
    Success = success;
    FailureReason = failureReason;
  }

  /// <summary><see langword="true"/> when the graph passed every validation check.</summary>
  public bool Success { get; }

  /// <summary>
  /// Human-readable description of the first failed check;
  /// <see langword="null"/> when <see cref="Success"/> is <see langword="true"/>.
  /// </summary>
  public string? FailureReason { get; }

  /// <summary>Creates a successful result, which never carries a failure reason.</summary>
  /// <returns>A result whose <see cref="Success"/> is <see langword="true"/>.</returns>
  public static GraphValidationResult Succeed() => new(success: true, failureReason: null);

  /// <summary>Creates a failed result carrying the reason of the failed check.</summary>
  /// <param name="failureReason">Non-empty description of why validation failed.</param>
  /// <returns>A result whose <see cref="Success"/> is <see langword="false"/>.</returns>
  /// <exception cref="ArgumentException"><paramref name="failureReason"/> is null, empty or whitespace.</exception>
  public static GraphValidationResult Fail(string failureReason)
  {
    if (string.IsNullOrWhiteSpace(failureReason))
      throw new ArgumentException("Failure reason must not be empty.", nameof(failureReason));

    return new GraphValidationResult(success: false, failureReason: failureReason);
  }
}
