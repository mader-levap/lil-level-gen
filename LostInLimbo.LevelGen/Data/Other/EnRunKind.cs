namespace LostInLimbo.LevelGen.Data.Other;

/// <summary>
/// Kind of run.
/// </summary>
public enum EnRunKind
{
  /// <summary>Generate entire level.</summary>
  All,
  /// <summary>Advance generation by single area.</summary>
  Area,
  /// <summary>Advance generation by single step (smallest unit of work that gives consistent data after completion). Useful for debugging in editor.</summary>
  Step,
}