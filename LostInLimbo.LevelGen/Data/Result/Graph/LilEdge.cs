using System;
using System.Collections.Generic;

namespace LostInLimbo.LevelGen.Data.Result.Graph;

/// <summary>
/// Passage between two places (nodes) in the level graph.
/// </summary>
/// <remarks>
/// <para>
/// Relationships represent passages between places. A passage can be a single door or the
/// entire width of an open level; that physical detail belongs to the map, not the graph.
/// </para>
/// <para>
/// <see cref="Conditions"/> being empty means an open (always traversable) passage.
/// Multiple conditions mean all of them must be met, e.g. a door requiring both red and
/// blue keys. See <see cref="LilCondition"/> for how conditions reference POIs.
/// </para>
/// <para>
/// Immutable once created and added to <see cref="LilGraph.Edges"/>. Invariants (documented,
/// not yet enforced): <see cref="Id"/> should not be <see cref="Guid.Empty"/>; <see cref="From"/>
/// and <see cref="To"/> should reference existing nodes and should differ from each other.
/// </para>
/// </remarks>
public sealed record LilEdge
{
  /// <summary>
  /// Unique identifier of this edge. Assigned by the generator; must not be <see cref="Guid.Empty"/>.
  /// </summary>
  public required Guid Id { get; init; }

  /// <summary>Identifier of the source node.</summary>
  public required Guid From { get; init; }
  /// <summary>Identifier of the destination node.</summary>
  public required Guid To { get; init; }

  /// <summary>Direction of this passage.</summary>
  public EnPassageDirection Direction { get; init; } = EnPassageDirection.Bidirectional;

  /// <summary>If true, this relationship can represent only one physical passage.
  /// False means multiple passages can exist (though it is up to map generator).</summary>
  public bool Chokepoint { get; init; } = false;

  /// <summary>If true, this is secret passage.</summary>
  public bool Secret { get; init; } = false;

  /// <summary>
  /// Requirements that must be met before this passage can be used. Empty list means an open
  /// (always traversable) passage. All listed conditions must be met.
  /// </summary>
  public List<LilCondition> Conditions { get; init; } = new();
}
