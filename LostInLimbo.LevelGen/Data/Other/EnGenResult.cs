namespace LostInLimbo.LevelGen.Data.Other;

/// <summary>
/// Lifecycle state and result of a generation run.
/// </summary>
/// <remarks>
/// Lifecycle: <see cref="None"/> (never ran) → <see cref="Running"/> → <see cref="Paused"/> (optional) → <see cref="Finished"/> or <see cref="Error"/>.
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
}
