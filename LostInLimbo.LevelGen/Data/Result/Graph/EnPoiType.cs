namespace LostInLimbo.LevelGen.Data.Result.Graph;

/// <summary>
/// Type of point of interest (POI) inside a node.
/// </summary>
/// <remarks>
/// A POI marks what a place is for in the level design. Example of a <see cref="Solution"/>:
/// a key/button/lever used to open a door.
/// </remarks>
public enum EnPoiType
{
  /// <summary>Place where the player starts.</summary>
  Start,
  /// <summary>Place where the player ends the level.</summary>
  Exit,
  /// <summary>Place that advances the level objective.</summary>
  Goal,
  /// <summary>Place that contains some other goal that is not necessary to beat the level.</summary>
  OptionalGoal,
  /// <summary>Place that contains something required elsewhere, e.g. key/button/lever for a conditional door.</summary>
  Solution,
}
