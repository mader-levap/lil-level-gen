namespace LostInLimbo.LevelGen.Data.Result.Stats;

/// <summary>
/// Current phase of a generation run.
/// </summary>
public enum EnPhase
{
  /// <summary>Currently generating prepass data.
  /// Completes within a single execution, but does not increment <see cref="LilStats.Steps"/>.</summary>
  Prepass,
  /// <summary>Currently generating the graph.
  /// Completes within a single execution, but does not increment <see cref="LilStats.Steps"/>.</summary>
  Graph,
  /// <summary>Currently generating the map.
  /// The only phase that increments <see cref="LilStats.Steps"/>.</summary>
  Map,
  /// <summary>Currently finalizing generation.
  /// Completes within a single execution, but does not increment <see cref="LilStats.Steps"/>.</summary>
  Finalization,
  /// <summary>Level generation is finished.</summary>
  Completed,
}