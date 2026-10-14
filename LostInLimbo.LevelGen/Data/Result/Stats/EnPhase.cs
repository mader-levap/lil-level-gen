namespace LostInLimbo.LevelGen.Data.Result.Stats;

/// <summary>
/// Current phase of generation run.
/// </summary>
public enum EnPhase
{
  /// <summary>Currently generating graph. Counts as single step.</summary>
  Graph,
  /// <summary>Currently generating map.</summary>
  Map,
  /// <summary>Finalize generation. Counts as single step.</summary>
  Finalization,
}