namespace LostInLimbo.LevelGen.Data.Status;

/// <summary>
/// Kind of run.
/// </summary>
public enum EnRunKind
{
  /// <summary>Generate the entire level. Can be stopped early by an explicit pause request and resumed later.</summary>
  All,
  /// <summary>
  /// Advance generation by a single area. NOTE: while generation is still a placeholder, areas do
  /// not exist yet, so this behaves like <see cref="Step"/> (advances a single step, then pauses).
  /// </summary>
  Area,
  /// <summary>
  /// Advance generation by a single step (the smallest unit of work that gives consistent data
  /// after completion). Useful for debugging in the editor. NOTE: pending prepass and graph
  /// phases complete within the same execution; only the map phase is stepped.
  /// </summary>
  Step,
}