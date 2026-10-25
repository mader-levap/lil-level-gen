namespace LostInLimbo.LevelGen.Data.Status;

/// <summary>
/// Lifecycle state and result of a generation run.
/// </summary>
/// <remarks>
/// Lifecycle: <see cref="None"/> (never ran) → <see cref="Running"/> → <see cref="Paused"/> (optional) →
/// <see cref="Finished"/>, <see cref="ValidationError"/>, <see cref="Error"/> or <see cref="Cancelled"/>.
/// <para>
/// <see cref="Cancelled"/>, <see cref="ValidationError"/> and <see cref="Error"/> are terminal just
/// like <see cref="Finished"/>: only a fresh <c>Start</c> can run again after them. Partial data
/// of a cancelled run is discarded.
/// </para>
/// <para>
/// Unlike <see cref="Error"/>, <see cref="ValidationError"/> exposes the world built up to the
/// failing check (see <c>LevelGenerationSession.World</c>), so a consumer can inspect the graph
/// and debug the issue; the reason itself is reported through <see cref="GenerationStatus.FailureReason"/>.
/// </para>
/// </remarks>
public enum EnGenResult
{
  /// <summary>Generation has not run yet.</summary>
  None,
  /// <summary>Generation is running at this moment.</summary>
  Running,
  /// <summary>Generation paused. Can happen if the run kind is not <c>All</c> or if a pause was explicitly requested. Incomplete, but consistent map data is available.</summary>
  Paused,
  /// <summary>Generation finished successfully. Completed map data is available.</summary>
  Finished,
  /// <summary>
  /// Generation failed on validation. Graph or graph and map data is available, though it may be
  /// incomplete, inconsistent or errorenous. For debugging; see
  /// <see cref="GenerationStatus.FailureReason"/> for the failing check.
  /// </summary>
  ValidationError,
  /// <summary>Generation failed. See <see cref="GenerationStatus.Error"/> for details.</summary>
  Error,
  /// <summary>Generation was cancelled.</summary>
  Cancelled,
}
