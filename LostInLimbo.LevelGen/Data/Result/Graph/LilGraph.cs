using System.Collections.Generic;

namespace LostInLimbo.LevelGen.Data.Result.Graph;

/// <summary>
/// <para>Abstract representation of level. Contains level design: nodes are places and relationships are passages between places.</para>
/// <para>Map generator translates abstract graph into actual level.</para>
/// <para>
/// Nodes:
/// <list type="bullet">
/// <item>Represent places. Note "places" is deliberately vague; it can be small room or multiple large areas.</item>
/// <item>Player has access to everything within given place.</item>
/// <item>Place can contain zero, one or many POIs (Point of Interests). It can be <c>start</c>, <c>exit</c>, <c>goal</c>, <c>solution</c> and more.</item>
/// <item>Example of <c>solution</c>: key/button/lever used to open door.</item>
/// </list>
/// </para>
/// <para>
/// Relationships:
/// <list type="bullet">
/// <item>Represent passages between places. It can be single door or entire width of open level.</item>
/// <item>Passage can be open or conditional.</item>
/// <item>Passage can be bidirectional (most common case) or one-way.</item>
/// <item>Example of <c>condition</c>: door that requires key.</item>
/// </list>
/// </para>
/// <para>Algorithm that generates graph needs to ensure graph is possible to solve: for example, key (<c>solution</c>) is always before door that requires it (<c>condition</c>). No softlock.</para>
/// </summary>
/// <remarks>
/// Mutable containers: generator builds the graph in place by appending nodes and edges.
/// Nodes and edges themselves are immutable once created (see <see cref="LilNode"/>,
/// <see cref="LilEdge"/>). Iteration order of these lists is not significant; consumers
/// must sort explicitly before using order in generation logic (see AGENTS.md).
/// </remarks>
public sealed record LilGraph
{
  /// <summary>All nodes (places) of this level graph. May be empty before graph generation runs.</summary>
  public List<LilNode> Nodes { get; set; } = new();

  /// <summary>
  /// All edges (passages) of this level graph. May be empty before graph generation runs.
  /// Every <see cref="LilEdge.From"/> and <see cref="LilEdge.To"/> should reference an
  /// existing node in <see cref="Nodes"/> (documented, not yet enforced).
  /// </summary>
  public List<LilEdge> Edges { get; set; } = new();
}