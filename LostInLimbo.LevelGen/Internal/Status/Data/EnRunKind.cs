namespace LostInLimbo.LevelGen.Internal.Status.Data;

/// <summary>
/// Kind of run.
/// </summary>
public enum EnRunKind
{
  /// <summary>Generate entire level. Can be stopped early by an explicit pause request and resumed later.</summary>
  All,
  /// <summary>
  /// Advance generation by single area. NOTE: while generation is still a placeholder, areas do
  /// not exist yet, so this behaves like <see cref="Step"/> (advances a single step, then pauses).
  /// </summary>
  Area,
  /// <summary>Advance generation by single step (smallest unit of work that gives consistent data after completion). Useful for debugging in editor.</summary>
  Step,
}