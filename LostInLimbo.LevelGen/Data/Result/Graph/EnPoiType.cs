namespace LostInLimbo.LevelGen.Data.Result.Graph;

/// <summary>
/// Type of point of interest (POI) inside a node.
/// </summary>
/// <remarks>
/// A POI marks what a place is for in the level design. Example of a <see cref="Solution"/>:
/// a key/button/lever used to open a door.
/// <para>Values are explicit because saved worlds store them (see <see cref="Api.Serializers.LevelGenWorldSerializer"/>): never change or reuse a value; add new members with new values.</para>
/// </remarks>
public enum EnPoiType
{
  /// <summary>Place where the player starts.</summary>
  Start = 0,
  /// <summary>Place where the player ends the level.</summary>
  Exit = 1,
  /// <summary>Place that advances the level objective.</summary>
  Goal = 2,
  /// <summary>Place that contains some other goal that is not necessary to beat the level.</summary>
  OptionalGoal = 3,
  /// <summary>Place that contains something required elsewhere, e.g. key/button/lever for a conditional door.</summary>
  Solution = 4,
}
