namespace LostInLimbo.LevelGen.Data.Other;

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
  /// <summary>Generation paused. Can happen if run kind is not All.</summary>
  Paused,
  /// <summary>Generation finished successfully.</summary>
  Finished,
  /// <summary>Generation failed. See <see cref="GenerationStatus.Error"/> for details.</summary>
  Error,
  /// <summary>
  /// Generation was cancelled through its <see cref="System.Threading.CancellationToken"/>
  /// before it could finish or pause. Not an error: <see cref="GenerationStatus.Error"/> stays
  /// <see langword="null"/> and the partial world must not be used.
  /// </summary>
  Cancelled,
}
