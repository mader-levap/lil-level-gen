using System;
using System.Collections.Generic;

namespace LostInLimbo.LevelGen.Data.Result.Graph;

/// <summary>
/// Single place in the level graph.
/// </summary>
/// <remarks>
/// <para>
/// Nodes represent places. Note "places" is deliberately vague; it can be a small 1x1 room
/// or multiple large areas. It is all up to the map generator and the config specific for
/// the map. The player has access to everything within a given place.
/// </para>
/// <para>
/// Immutable once created and added to <see cref="LilGraph.Nodes"/>. Invariant (enforced by
/// <see cref="LostInLimbo.LevelGen.Api.LevelGraphValidation"/>): <see cref="Id"/> must not be
/// <see cref="Guid.Empty"/>; it is assigned by the generator.
/// </para>
/// <para>
/// <see cref="Pois"/> and <see cref="Reach"/> are mutable collections filled in place by
/// generator passes after the node is created: POIs by the graph builders and processors,
/// reachability metadata by the metadata pass.
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

  /// <summary>Maximum allowed number of edges. A negative number means no limit. This is a hard cap.</summary>
  public required int MaxEdges { get; init; }

  /// <summary>If true, this is a secret node.</summary>
  public bool Secret { get; init; } = false;

  /// <summary>Styling data.</summary>
  public LilStyle Style { get; init; } = new();

  /// <summary>
  /// Points of interest contained in this place. May be empty (plain pass-through place).
  /// A node can contain multiple POIs, e.g. two keys in the same room.
  /// </summary>
  public List<LilPoi> Pois { get; init; } = new();

  /// <summary>
  /// <para>
  /// Reachability metadata of this node, filled by the metadata pass
  /// (see <c>LostInLimbo.LevelGen.Internal.Generator.Graph.Meta.GraphMetadata</c>).
  /// Key is the identifier of a start POI, value is the list of solution POI identifiers
  /// that must be collected before this node can be reached from that start - the smallest
  /// such list over all passages from that start. An empty list means the node is reachable
  /// from that start without meeting any condition; a start POI with no entry at all means
  /// this node cannot be reached from that start (and a node unreachable from every start
  /// keeps an empty dictionary).
  /// </para>
  /// <para>
  /// The value list is sorted ascending by POI identifier, so the same graph always produces
  /// the same data; the order carries no other meaning. Never <see langword="null"/>.
  /// </para>
  /// </summary>
  public Dictionary<Guid, List<Guid>> Reach { get; init; } = new();
}
