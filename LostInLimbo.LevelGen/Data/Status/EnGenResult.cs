namespace LostInLimbo.LevelGen.Data.Status;

/// <summary>
/// Lifecycle state and result of a generation run.
/// </summary>
/// <remarks>
/// Lifecycle: <see cref="None"/> (never ran) → <see cref="Running"/> → <see cref="Paused"/> (optional) →
/// <see cref="Finished"/>, <see cref="Error"/> or <see cref="Cancelled"/>.
/// <para>
/// <see cref="Cancelled"/> is terminal just like <see cref="Error"/> and <see cref="Finished"/>:
/// partial data of a cancelled run is discarded and only a fresh <c>Start</c> can run again.
/// </para>
/// </remarks>
public enum EnGenResult
{
  /// <summary>Generation has not run yet.</summary>
  None,
  /// <summary>Generation is running at this moment.</summary>
  Running,
  /// <summary>Generation paused. Can happen if the run kind is not <c>All</c> or if a pause was explicitly requested.</summary>
  Paused,
  /// <summary>Generation finished successfully.</summary>
  Finished,
  /// <summary>Generation failed. See <see cref="GenerationStatus.Error"/> for details.</summary>
  Error,
  /// <summary>Generation was cancelled.</summary>
  Cancelled,
}
