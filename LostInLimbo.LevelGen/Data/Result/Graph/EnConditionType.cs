namespace LostInLimbo.LevelGen.Data.Result.Graph;

/// <summary>
/// Kind of condition that must be met before a passage (<see cref="LilEdge"/>) can be used.
/// </summary>
/// <remarks>
/// Currently only solution-based conditions exist (door that requires a key). More kinds
/// (e.g. item, state flag) can be added later without changing the edge shape.
/// </remarks>
public enum EnConditionType
{
  /// <summary>Specific solution POI must be collected first (e.g. particular key for a door).</summary>
  RequiresSolution,
}
