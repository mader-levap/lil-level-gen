using System;
using System.Collections.Generic;
using LostInLimbo.LevelGen.Data.Result.Graph;

namespace LostInLimbo.LevelGen.Internal.Generator.Graph.Meta;

/// <summary>
/// Key-door flood over a level graph: from a start node, crosses every passage whose conditions
/// are all held, optionally picking up POIs on the way, until nothing new is reached.
/// </summary>
/// <remarks>
/// <para>
/// Passage rules mirror the validator: one-way passages lead From -> To only, bidirectional
/// ones both ways, conditions apply in every direction and secret passages and nodes count as
/// ordinary ones. Self-loops and edges with a missing endpoint are skipped, never thrown. When
/// two nodes share an identifier, the first one wins.
/// </para>
/// <para>
/// The node index and adjacency are built once per instance, so several floods over the same
/// graph share them. The graph is only read, never modified. Deterministic: results are sets
/// that do not depend on node or edge order.
/// </para>
/// </remarks>
internal sealed class GraphFlood
{
  /// <summary>Node index by identifier.</summary>
  private readonly Dictionary<Guid, LilNode> _nodeById;

  /// <summary>Incident passages per traversable node, pre-filtered by direction.</summary>
  private readonly Dictionary<Guid, List<LilEdge>> _adjacency;

  /// <summary>
  /// Indexes the nodes and passages of <paramref name="graph"/>.
  /// </summary>
  /// <param name="graph">Graph to flood; read only.</param>
  /// <exception cref="ArgumentNullException"><paramref name="graph"/> is <see langword="null"/>.</exception>
  public GraphFlood(LilGraph graph)
  {
    if (graph is null) throw new ArgumentNullException(nameof(graph));

    _nodeById = new Dictionary<Guid, LilNode>(graph.Nodes.Count);
    foreach (var node in graph.Nodes)
    {
      _nodeById.TryAdd(node.Id, node);
    }

    _adjacency = new Dictionary<Guid, List<LilEdge>>(graph.Nodes.Count);
    foreach (var edge in graph.Edges)
    {
      if (edge.From == edge.To) continue;
      if (!_nodeById.ContainsKey(edge.From) || !_nodeById.ContainsKey(edge.To)) continue;

      // The forward direction From -> To is always allowed, backward only when bidirectional.
      AddIncident(edge.From, edge);
      if (edge.Direction == EnPassageDirection.Bidirectional)
      {
        AddIncident(edge.To, edge);
      }
    }
  }

  /// <summary>Gets the indexed node with the given identifier.</summary>
  /// <param name="nodeId">Identifier of an indexed node (e.g. one returned by <see cref="Run"/>).</param>
  /// <returns>The node.</returns>
  public LilNode GetNode(Guid nodeId) => _nodeById[nodeId];

  /// <summary>
  /// Floods from the given start node.
  /// </summary>
  /// <param name="startId">Identifier of an indexed start node.</param>
  /// <param name="initialHeld">POIs held before the flood starts; never null.</param>
  /// <param name="collectible">POIs picked up when their node is reached; others are ignored. Never null.</param>
  /// <param name="blockedPoi">POI treated as never held, even when initially held or collectible; null blocks nothing.</param>
  /// <returns>The reached node identifiers (including the start) and the POIs held at the end.</returns>
  public (HashSet<Guid> Reached, HashSet<Guid> Held) Run(
    Guid startId,
    IEnumerable<Guid> initialHeld,
    HashSet<Guid> collectible,
    Guid? blockedPoi)
  {
    var reached = new HashSet<Guid>();
    var held = new HashSet<Guid>();
    foreach (var poiId in initialHeld)
    {
      if (poiId != blockedPoi) held.Add(poiId);
    }

    var queue = new Queue<Guid>();
    // Passages that were blocked by a missing POI; retried whenever a new POI arrives.
    var waiting = new List<(LilEdge Edge, Guid NextId)>();

    Arrive(startId);
    // Everything held so far was held before any passage was checked.
    var heldAtLastRetry = held.Count;
    while (true)
    {
      while (queue.Count > 0)
      {
        var nodeId = queue.Dequeue();
        if (!_adjacency.TryGetValue(nodeId, out var incident)) continue;

        foreach (var edge in incident)
        {
          // Incidence direction is pre-filtered, so the other endpoint is always walkable.
          var nextId = edge.From == nodeId ? edge.To : edge.From;
          if (reached.Contains(nextId)) continue;
          if (HoldsAll(held, edge.Conditions)) Arrive(nextId);
          else waiting.Add((edge, nextId));
        }
      }

      // Fixpoint: nothing gained since the last retry means no waiting passage can open.
      // Held POIs only grow, so the loop ends after at most one retry per collectible POI.
      if (held.Count == heldAtLastRetry) break;
      heldAtLastRetry = held.Count;

      var retry = waiting;
      waiting = [];
      foreach (var (edge, nextId) in retry)
      {
        if (reached.Contains(nextId)) continue;
        if (HoldsAll(held, edge.Conditions)) Arrive(nextId);
        else waiting.Add((edge, nextId));
      }
    }

    return (reached, held);

    // Marks a node reached, picks up its collectible POIs and schedules it for expansion.
    void Arrive(Guid nodeId)
    {
      reached.Add(nodeId);
      foreach (var poi in _nodeById[nodeId].Pois)
      {
        if (poi.Id != blockedPoi && collectible.Contains(poi.Id)) held.Add(poi.Id);
      }

      queue.Enqueue(nodeId);
    }
  }

  /// <summary>Checks whether every condition of a passage is held.</summary>
  /// <param name="held">Held POIs.</param>
  /// <param name="conditions">Conditions of the passage; an open passage always passes.</param>
  /// <returns>True when all conditions are held.</returns>
  private static bool HoldsAll(HashSet<Guid> held, List<LilCondition> conditions)
  {
    foreach (var condition in conditions)
    {
      if (!held.Contains(condition.RequiresPoi)) return false;
    }

    return true;
  }

  /// <summary>Appends an edge to the incident list of a node.</summary>
  /// <param name="nodeId">Node the edge is incident to.</param>
  /// <param name="edge">Edge to add.</param>
  private void AddIncident(Guid nodeId, LilEdge edge)
  {
    if (!_adjacency.TryGetValue(nodeId, out var list))
    {
      list = [];
      _adjacency[nodeId] = list;
    }

    list.Add(edge);
  }
}
