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
/// <see cref="Pois"/>, <see cref="Reach"/> and <see cref="OpenReach"/> are mutable collections filled in place by
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
  /// whose conditioned passages every route from that start to this node must use - so the
  /// player is guaranteed to hold them on arrival. A key that some route can avoid is not
  /// listed. An empty list means no solution is unavoidable (some route may still use
  /// conditioned passages, as long as an alternative avoids each of them - see
  /// <see cref="OpenReach"/> for "reachable without any solution"); a start POI with no
  /// entry at all means this node cannot be reached from that start (and a node unreachable
  /// from every start keeps an empty dictionary).
  /// </para>
  /// <para>
  /// The value list is sorted ascending by POI identifier, so the same graph always produces
  /// the same data; the order carries no other meaning. Never <see langword="null"/>.
  /// </para>
  /// </summary>
  public Dictionary<Guid, List<Guid>> Reach { get; init; } = new();

  /// <summary>
  /// <para>
  /// Identifiers of the start POIs from which this node can be reached using only passages
  /// without conditions - no solution needed at all. Filled by the metadata pass alongside
  /// <see cref="Reach"/>.
  /// </para>
  /// <para>
  /// Sorted ascending; empty when every route from every start needs some solution, or when the
  /// node is unreachable. Every start listed here also has an empty <see cref="Reach"/> list,
  /// but not the other way round. Never <see langword="null"/>.
  /// </para>
  /// </summary>
  public List<Guid> OpenReach { get; init; } = new();
}
