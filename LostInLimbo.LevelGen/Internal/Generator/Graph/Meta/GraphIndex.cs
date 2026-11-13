using System;
using System.Collections.Generic;
using LostInLimbo.LevelGen.Data.Result.Graph;

namespace LostInLimbo.LevelGen.Internal.Generator.Graph.Meta;

/// <summary>
/// Index-based lookups over a level graph shared by the finalization processors: stable visit
/// orders, node index by identifier and strict edge-endpoint resolution.
/// </summary>
/// <remarks>
/// Indices are positions in <see cref="LilGraph.Nodes"/> and <see cref="LilGraph.Edges"/> at the
/// time of the call. The graph is only read. Deterministic: visit orders are sorted by
/// identifier, so they never depend on insertion or hash order.
/// </remarks>
internal static class GraphIndex
{
  /// <summary>
  /// Produces the node indices in ascending <see cref="LilNode.Id"/> order.
  /// </summary>
  /// <param name="graph">Graph to order.</param>
  /// <returns>Node indices sorted by identifier; empty for a node-less graph.</returns>
  public static List<int> NodeVisitOrder(LilGraph graph)
  {
    var order = Range(graph.Nodes.Count);
    order.Sort((left, right) => graph.Nodes[left].Id.CompareTo(graph.Nodes[right].Id));
    return order;
  }

  /// <summary>
  /// Produces the edge indices in ascending <see cref="LilEdge.Id"/> order.
  /// </summary>
  /// <param name="graph">Graph to order.</param>
  /// <returns>Edge indices sorted by identifier; empty for an edge-less graph.</returns>
  public static List<int> EdgeVisitOrder(LilGraph graph)
  {
    var order = Range(graph.Edges.Count);
    order.Sort((left, right) => graph.Edges[left].Id.CompareTo(graph.Edges[right].Id));
    return order;
  }

  /// <summary>
  /// Maps every node identifier to its index in <see cref="LilGraph.Nodes"/>.
  /// </summary>
  /// <param name="graph">Graph to index.</param>
  /// <returns>Node index per identifier.</returns>
  /// <exception cref="ArgumentException">Two nodes share an identifier.</exception>
  public static Dictionary<Guid, int> IndexNodesById(LilGraph graph)
  {
    var indexById = new Dictionary<Guid, int>(graph.Nodes.Count);
    for (var i = 0; i < graph.Nodes.Count; i++)
    {
      indexById.Add(graph.Nodes[i].Id, i);
    }

    return indexById;
  }

  /// <summary>
  /// Resolves both endpoints of an edge to node indices.
  /// </summary>
  /// <param name="indexById">Node index per identifier, from <see cref="IndexNodesById"/>.</param>
  /// <param name="edge">Edge to resolve.</param>
  /// <returns>Node indices of <see cref="LilEdge.From"/> and <see cref="LilEdge.To"/>.</returns>
  /// <exception cref="InvalidOperationException">The edge references a missing node.</exception>
  public static (int From, int To) ResolveEndpoints(Dictionary<Guid, int> indexById, LilEdge edge)
  {
    if (!indexById.TryGetValue(edge.From, out var from))
      throw new InvalidOperationException($"Edge {edge.Id} references unknown node {edge.From}.");
    if (!indexById.TryGetValue(edge.To, out var to))
      throw new InvalidOperationException($"Edge {edge.Id} references unknown node {edge.To}.");
    return (from, to);
  }

  /// <summary>
  /// Creates the list <c>0, 1, ..., count - 1</c>.
  /// </summary>
  /// <param name="count">Number of indices.</param>
  /// <returns>Ascending indices.</returns>
  private static List<int> Range(int count)
  {
    var indices = new List<int>(count);
    for (var i = 0; i < count; i++)
    {
      indices.Add(i);
    }

    return indices;
  }
}
