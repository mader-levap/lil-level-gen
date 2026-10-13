using LostInLimbo.LevelGen.Data.Result.Graph;
using LostInLimbo.LevelGen.Data.Result.Map;

namespace LostInLimbo.LevelGen.Data.Result;

/// <summary>
/// Result of generation.
/// </summary>
/// <remarks>
/// Pure data.
/// </remarks>
public sealed record LilWorld
{
  /// <summary>Abstract representation of level.</summary>
  public LilGraph Graph { get; set; } = new();

  /// <summary>Concrete representation of map that can be used for generating meshes and object in game.</summary>
  public LilMap Map { get; set; } = new();
}
