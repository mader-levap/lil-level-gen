namespace LostInLimbo.LevelGen.Data.Result.Stats;

/// <summary>
/// Current phase of a generation run.
/// </summary>
public enum EnPhase
{
  /// <summary>Currently generating prepass data. Counts as a single step.</summary>
  Prepass,
  /// <summary>Currently generating the graph. Counts as a single step.</summary>
  Graph,
  /// <summary>Currently generating the map.</summary>
  Map,
  /// <summary>Finalizing generation. Counts as a single step.</summary>
  Finalization,
}