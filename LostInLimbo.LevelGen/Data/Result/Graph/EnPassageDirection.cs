namespace LostInLimbo.LevelGen.Data.Result.Graph;

/// <summary>
/// Direction of a passage (<see cref="LilEdge"/>) between two places.
/// </summary>
/// <remarks>Values are explicit because saved worlds store them (see <see cref="Api.Serializers.LevelGenWorldSerializer"/>): never change or reuse a value; add new members with new values.</remarks>
public enum EnPassageDirection
{
  /// <summary>A passage can be used in both directions. Most common case.</summary>
  Bidirectional = 0,
  /// <summary>A passage can be used only from source (<see cref="LilEdge.From"/>) to destination (<see cref="LilEdge.To"/>).</summary>
  OneWay = 1,
}
