using System;
using LostInLimbo.LevelGen.Data.Result.Graph;

namespace LostInLimbo.LevelGen.Internal.Generator.Graph.Meta;

/// <summary>
/// POI lookups on a <see cref="LilNode"/>, shared by builders, processors and the validator.
/// </summary>
/// <remarks>
/// Extension methods: called as <c>node.HasPoi(type)</c>, yet they add nothing to the public
/// <see cref="LilNode"/> contract. POIs are scanned in list order, so results are deterministic.
/// </remarks>
internal static class LilNodeExtensions
{
  /// <summary>
  /// Checks whether a node holds at least one POI of the given type.
  /// </summary>
  /// <param name="node">Node to check.</param>
  /// <param name="type">POI type to look for.</param>
  /// <returns>True when such a POI exists.</returns>
  /// <exception cref="ArgumentNullException"><paramref name="node"/> is null.</exception>
  public static bool HasPoi(this LilNode node, EnPoiType type)
  {
    return node.FindPoi(type) != null;
  }

  /// <summary>
  /// Finds the first POI of the given type in a node.
  /// </summary>
  /// <param name="node">Node to search.</param>
  /// <param name="type">POI type to look for.</param>
  /// <returns>The first matching POI in list order, or <see langword="null"/> when there is none.</returns>
  /// <exception cref="ArgumentNullException"><paramref name="node"/> is null.</exception>
  public static LilPoi? FindPoi(this LilNode node, EnPoiType type)
  {
    if (node is null) throw new ArgumentNullException(nameof(node));

    foreach (var poi in node.Pois)
    {
      if (poi.Type == type) return poi;
    }

    return null;
  }
}
