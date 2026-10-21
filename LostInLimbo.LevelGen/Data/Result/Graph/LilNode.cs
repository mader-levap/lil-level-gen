using System;
using System.Collections.Generic;

namespace LostInLimbo.LevelGen.Data.Result.Graph;

/// <summary>
/// Single place in the level graph.
/// </summary>
/// <remarks>
/// <para>
/// Nodes represent places. Note "places" is deliberately vague; it can be a small 1x1 room
/// or multiple large areas. It is all up to map generator and config specific for map.
/// Player has access to everything within given place.
/// </para>
/// <para>
/// Immutable once created and added to <see cref="LilGraph.Nodes"/>. Invariant (documented,
/// not yet enforced): <see cref="Id"/> should not be <see cref="Guid.Empty"/>; it is assigned
/// by the generator.
/// </para>
/// </remarks>
public sealed record LilNode
{
  /// <summary>
  /// Unique identifier of this node. Assigned by the generator; must not be <see cref="Guid.Empty"/>.
  /// </summary>
  public required Guid Id { get; init; }

  /// <summary>Size relative to other nodes.</summary>
  public required int RelSize { get; init; }

  /// <summary>Maximum allowed amount of edges. Negative number means no limit. This is hard cap.</summary>
  public required int MaxEdges { get; init; }

  /// <summary>If true, this is secret node.</summary>
  public bool Secret { get; init; } = false;

  /// <summary>Styling data.</summary>
  public LilStyle Style { get; init; } = new();

  /// <summary>
  /// Points of interest contained in this place. May be empty (plain pass-through place).
  /// A node can contain multiple POIs, e.g. two keys in the same room.
  /// </summary>
  public List<LilPoi> Pois { get; init; } = new();
}
