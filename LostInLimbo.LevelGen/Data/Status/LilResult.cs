using System;

namespace LostInLimbo.LevelGen.Data.Status;

/// <summary>
/// Outcome of a level generation attempt: a success flag plus a human-readable
/// description of the reason for failure.
/// </summary>
/// <remarks>
/// Instances are immutable and can only be created through <see cref="Succeed"/> and
/// <see cref="Fail"/>, so an inconsistent combination (failure without a reason) cannot exist.
/// </remarks>
public sealed record LilResult
{
  /// <summary>
  /// Initializes a new instance of the <see cref="LilResult"/> record.
  /// </summary>
  /// <param name="success">Value for <see cref="Success"/>.</param>
  /// <param name="failureReason">Value for <see cref="FailureReason"/>.</param>
  private LilResult(bool success, string? failureReason)
  {
    Success = success;
    FailureReason = failureReason;
  }

  /// <summary><see langword="true"/> when result is considered successful.</summary>
  public bool Success { get; }

  /// <summary>
  /// Human-readable description of reason for failure;
  /// <see langword="null"/> when <see cref="Success"/> is <see langword="true"/>.
  /// </summary>
  public string? FailureReason { get; }

  /// <summary>Creates a successful result, which never carries a failure reason.</summary>
  /// <returns>A result whose <see cref="Success"/> is <see langword="true"/>.</returns>
  public static LilResult Succeed() => new(success: true, failureReason: null);

  /// <summary>Creates a failed result carrying the reason of the failure.</summary>
  /// <param name="failureReason">Non-empty description of failure reason.</param>
  /// <returns>A result whose <see cref="Success"/> is <see langword="false"/>.</returns>
  /// <exception cref="ArgumentException"><paramref name="failureReason"/> is null, empty or whitespace.</exception>
  public static LilResult Fail(string failureReason)
  {
    if (string.IsNullOrWhiteSpace(failureReason))
      throw new ArgumentException("Failure reason must not be empty.", nameof(failureReason));

    return new LilResult(success: false, failureReason: failureReason);
  }
}
