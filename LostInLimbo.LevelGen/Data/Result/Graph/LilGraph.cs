using System.Collections.Generic;

namespace LostInLimbo.LevelGen.Data.Result.Graph;

/// <summary>
/// <para>Abstract representation of the level. Contains the level design: nodes are places and relationships are passages between places.</para>
/// <para>The map generator translates the abstract graph into an actual level.</para>
/// <para>
/// Nodes:
/// <list type="bullet">
/// <item>Represent places. Note "places" is deliberately vague; it can be a small room or multiple large areas.</item>
/// <item>The player has access to everything within a given place.</item>
/// <item>A place can contain zero, one or many POIs (Points of Interest). It can be <c>start</c>, <c>exit</c>, <c>goal</c>, <c>solution</c> and more.</item>
/// <item>Example of a <c>solution</c>: a key/button/lever used to open a door.</item>
/// </list>
/// </para>
/// <para>
/// Relationships:
/// <list type="bullet">
/// <item>Represent passages between places. It can be a single door or the entire width of an open level.</item>
/// <item>A passage can be open or conditional.</item>
/// <item>A passage can be bidirectional (most common case) or one-way.</item>
/// <item>Example of a <c>condition</c>: a door that requires a key.</item>
/// </list>
/// </para>
/// <para>
/// The algorithm that generates the graph needs to ensure the graph is possible to solve: for example,
/// the key (<c>solution</c>) is always before the door that requires it (<c>condition</c>). No softlock.
/// </para>
/// </summary>
/// <remarks>
/// Mutable containers: generator builds the graph in place by appending nodes and edges.
/// Nodes and edges themselves are immutable once created (see <see cref="LilNode"/>,
/// <see cref="LilEdge"/>). Iteration order of these lists is not significant; consumers
/// must sort explicitly before using order in generation logic.
/// </remarks>
public sealed record LilGraph
{
  /// <summary>All nodes (places) of this level graph. May be empty before graph generation runs.</summary>
  public List<LilNode> Nodes { get; set; } = new();

  /// <summary>
  /// All edges (passages) of this level graph. May be empty before graph generation runs.
  /// Every <see cref="LilEdge.From"/> and <see cref="LilEdge.To"/> must reference an
  /// existing node in <see cref="Nodes"/> (enforced by
  /// <see cref="LostInLimbo.LevelGen.Api.LevelGraphValidation"/>).
  /// </summary>
  public List<LilEdge> Edges { get; set; } = new();
}