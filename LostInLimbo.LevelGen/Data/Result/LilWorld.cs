using LostInLimbo.LevelGen.Data.Result.Graph;
using LostInLimbo.LevelGen.Data.Result.Map;
using LostInLimbo.LevelGen.Data.Result.Stats;

namespace LostInLimbo.LevelGen.Data.Result;

/// <summary>
/// Result of generation.
/// </summary>
/// <remarks>
/// Pure, mutable data. Must be serializable.
/// <para>
/// <see cref="Api.LevelGenSession.World"/> returns a private deep copy on every call, so a
/// consumer may modify it freely without affecting the generator or other callers. A
/// <c>with</c> expression copies only top-level references; use <see cref="LilWorldCopy.Copy"/>
/// for a fully detached copy.
/// </para>
/// </remarks>
public sealed record LilWorld
{
  /// <summary>Current phase of generation.</summary>
  public EnPhase Phase { get; set; } = EnPhase.Prepass;

  /// <summary>Abstract representation of the level.</summary>
  public LilGraph Graph { get; set; } = new();

  /// <summary>Concrete representation of the map that can be used for generating meshes and objects in game.</summary>
  public LilMap Map { get; set; } = new();

  /// <summary>Statistics.</summary>
  public LilStats Stats { get; set; } = new();
}
