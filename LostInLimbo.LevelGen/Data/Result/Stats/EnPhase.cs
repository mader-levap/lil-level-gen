namespace LostInLimbo.LevelGen.Data.Result.Stats;

/// <summary>
/// Current phase of a generation run.
/// </summary>
/// <remarks>Values are explicit because saved worlds store them (see <see cref="Api.Serializers.LevelGenWorldSerializer"/>): never change or reuse a value; add new members with new values.</remarks>
public enum EnPhase
{
  /// <summary>Currently generating prepass data.
  /// Completes within a single execution, but does not increment <see cref="LilStats.Steps"/>.</summary>
  Prepass = 0,
  /// <summary>Currently generating the graph.
  /// Completes within a single execution, but does not increment <see cref="LilStats.Steps"/>.</summary>
  Graph = 1,
  /// <summary>Currently generating the map.
  /// The only phase that increments <see cref="LilStats.Steps"/>.</summary>
  Map = 2,
  /// <summary>Currently finalizing generation.
  /// Completes within a single execution, but does not increment <see cref="LilStats.Steps"/>.</summary>
  Finalization = 3,
  /// <summary>Level generation is finished.</summary>
  Completed = 4,
}