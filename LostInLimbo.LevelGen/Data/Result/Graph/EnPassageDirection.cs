namespace LostInLimbo.LevelGen.Data.Result.Graph;

/// <summary>
/// Direction of a passage (<see cref="LilEdge"/>) between two places.
/// </summary>
/// <remarks>
/// Named <c>Passage</c> to distinguish from spatial direction enums in <c>Data.Utils.Geometry</c>.
/// </remarks>
public enum EnPassageDirection
{
  /// <summary>Passage can be used in both directions. Most common case.</summary>
  Bidirectional,
  /// <summary>Passage can be used only from source (<see cref="LilEdge.From"/>) to destination (<see cref="LilEdge.To"/>).</summary>
  OneWay,
}
