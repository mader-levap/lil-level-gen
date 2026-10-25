using System;
using System.Collections.Generic;
using System.Linq;
using LostInLimbo.LevelGen.Data.Result.Graph;

namespace LostInLimbo.LevelGen.Internal.Generator.Graph.Meta;

/// <summary>
/// Fills <see cref="LilNode.Reach"/> of every node: for each start POI, the smallest list of
/// solution POI identifiers required to reach that node from that start.
/// </summary>
/// <remarks>
/// <para>
/// Runs one independent label-correcting search per start POI. A search state is
/// <c>(node, used, held)</c> where <c>used</c> is the union of all conditions crossed on the
/// way and <c>held</c> holds every POI of every node visited on the way. A passage can be
/// crossed when its direction allows leaving the current node and all of its conditions are
/// held; crossing adds the condition POIs to <c>used</c> and the far node's POIs to
/// <c>held</c>. Per node the search keeps only non-dominated states (a state is dropped when
/// another one uses a subset of its conditions and holds a superset of its POIs), so the
/// fixpoint terminates even on cyclic graphs. The stored entry per node is the state with the
/// smallest used set (by count, then by ascending identifier), which makes the output
/// independent of edge processing order. See <see cref="LilNode.Reach"/> for the semantics.
/// </para>
/// <para>
/// The pass never verifies the graph - it runs before validation, so a null or empty graph is
/// a no-op and dangling edge or condition references are skipped rather than thrown.
/// </para>
/// <para>Deterministic: the same graph always produces the same metadata.</para>
/// </remarks>
internal sealed class GraphMetadata(LilGraph? graph)
{
  /// <summary>Graph whose nodes receive reach metadata, or null.</summary>
  private LilGraph? _graph = graph;

  /// <summary>Traversal state of the search for a single start POI.</summary>
  /// <param name="NodeId">Node the search currently stands in.</param>
  /// <param name="Used">Conditions crossed so far; the value reported by <see cref="LilNode.Reach"/>.</param>
  /// <param name="Held">POIs of all nodes visited so far, including the current one.</param>
  private sealed record SearchState(Guid NodeId, HashSet<Guid> Used, HashSet<Guid> Held);

  /// <summary>
  /// Generates reach metadata for all nodes of the graph; a null or empty graph is a no-op.
  /// Existing entries are replaced, so calling the pass twice yields the same result.
  /// </summary>
  public void Generate()
  {
    var graph = _graph;
    if (graph is null || graph.Nodes.Count == 0) return;

    // Overwrite instead of accumulate on a repeated run.
    foreach (var node in graph.Nodes)
    {
      node.Reach.Clear();
    }

    // Index nodes and build incident adjacency; degenerate and dangling edges are skipped.
    var nodeById = new Dictionary<Guid, LilNode>(graph.Nodes.Count);
    foreach (var node in graph.Nodes)
    {
      nodeById.TryAdd(node.Id, node);
    }

    var adjacency = new Dictionary<Guid, List<LilEdge>>(graph.Nodes.Count);
    foreach (var edge in graph.Edges)
    {
      if (edge.From == edge.To) continue;
      if (!nodeById.ContainsKey(edge.From) || !nodeById.ContainsKey(edge.To)) continue;

      // One-way passages are incident to their source only, mirroring the validator: the
      // forward direction From -> To is always allowed, backward only when bidirectional.
      AddIncident(adjacency, edge.From, edge);
      if (edge.Direction == EnPassageDirection.Bidirectional)
      {
        AddIncident(adjacency, edge.To, edge);
      }
    }

    // One independent search per start POI, in deterministic node then POI order.
    foreach (var node in graph.Nodes)
    {
      foreach (var poi in node.Pois)
      {
        if (poi.Type == EnPoiType.Start)
        {
          SolveFromStart(node, nodeById, adjacency, poi.Id);
        }
      }
    }
  }

  /// <summary>
  /// Runs the label-correcting search for one start POI and writes the best required list of
  /// every reached node into its reach dictionary.
  /// </summary>
  /// <param name="startNode">Node containing the start POI.</param>
  /// <param name="nodeById">Node index by identifier.</param>
  /// <param name="adjacency">Incident passages per traversable node.</param>
  /// <param name="startPoiId">Identifier of the start POI; the reach dictionary key.</param>
  private static void SolveFromStart(
    LilNode startNode,
    Dictionary<Guid, LilNode> nodeById,
    Dictionary<Guid, List<LilEdge>> adjacency,
    Guid startPoiId)
  {
    // Non-dominated states per reached node.
    var fronts = new Dictionary<Guid, List<SearchState>>();
    var worklist = new Queue<SearchState>();

    // The start node is always reachable from itself without meeting any condition.
    var seed = new SearchState(startNode.Id, [], CollectPois(startNode));
    fronts[startNode.Id] = [seed];
    worklist.Enqueue(seed);

    while (worklist.Count > 0)
    {
      var state = worklist.Dequeue();
      if (!adjacency.TryGetValue(state.NodeId, out var incident)) continue;

      foreach (var edge in incident)
      {
        // Incidence direction is pre-filtered, so the other endpoint is always walkable.
        var nextId = edge.From == state.NodeId ? edge.To : edge.From;
        if (!HoldsAll(state.Held, edge.Conditions)) continue;

        var nextUsed = new HashSet<Guid>(state.Used);
        foreach (var condition in edge.Conditions)
        {
          nextUsed.Add(condition.RequiresPoi);
        }

        // The far node's POIs are collected on arrival, like the validator collects on flood.
        var nextHeld = new HashSet<Guid>(state.Held);
        foreach (var poi in nodeById[nextId].Pois)
        {
          nextHeld.Add(poi.Id);
        }

        var next = new SearchState(nextId, nextUsed, nextHeld);
        if (TryAdmit(fronts, next)) worklist.Enqueue(next);
      }
    }

    // Report the reached nodes; unreachable nodes keep no entry for this start.
    foreach (var (nodeId, front) in fronts)
    {
      List<Guid>? best = null;
      foreach (var state in front)
      {
        var candidate = state.Used.OrderBy(id => id).ToList();
        if (best is null || IsBetter(candidate, best)) best = candidate;
      }

      if (best is not null) nodeById[nodeId].Reach[startPoiId] = best;
    }
  }

  /// <summary>
  /// Admits a state into its node's front when no existing state dominates it, dropping the
  /// states it dominates itself. Rejected states can never yield a better result than an
  /// already stored one, which both bounds the search and keeps its outcome order-independent.
  /// </summary>
  /// <param name="fronts">Non-dominated states per reached node.</param>
  /// <param name="state">State to admit.</param>
  /// <returns>True when the state was added and must be expanded.</returns>
  private static bool TryAdmit(Dictionary<Guid, List<SearchState>> fronts, SearchState state)
  {
    if (!fronts.TryGetValue(state.NodeId, out var front))
    {
      front = [];
      fronts[state.NodeId] = front;
    }

    for (var i = front.Count - 1; i >= 0; i--)
    {
      var existing = front[i];
      if (Dominates(existing, state)) return false;
      if (Dominates(state, existing)) front.RemoveAt(i);
    }

    front.Add(state);
    return true;
  }

  /// <summary>
  /// Checks whether state A is at least as good as state B: it uses a subset of B's
  /// conditions and holds a superset of B's POIs, so every passage B can cross is also
  /// crossable by A and A's results are never worse.
  /// </summary>
  /// <param name="a">Dominating candidate.</param>
  /// <param name="b">Dominated candidate.</param>
  /// <returns>True when A dominates B.</returns>
  private static bool Dominates(SearchState a, SearchState b)
  {
    return a.Used.IsSubsetOf(b.Used) && a.Held.IsSupersetOf(b.Held);
  }

  /// <summary>Checks whether every condition of a passage is already held.</summary>
  /// <param name="held">POIs held by the search state.</param>
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

  /// <summary>
  /// Compares two required lists by the reporting order of <see cref="LilNode.Reach"/>: fewer
  /// solutions first, ties broken by the first differing identifier (ascending).
  /// </summary>
  /// <param name="candidate">Challenger list, sorted ascending.</param>
  /// <param name="best">Current best list, sorted ascending.</param>
  /// <returns>True when <paramref name="candidate"/> is the smaller list.</returns>
  private static bool IsBetter(List<Guid> candidate, List<Guid> best)
  {
    if (candidate.Count != best.Count) return candidate.Count < best.Count;

    for (var i = 0; i < candidate.Count; i++)
    {
      var comparison = candidate[i].CompareTo(best[i]);
      if (comparison != 0) return comparison < 0;
    }

    return false;
  }

  /// <summary>Collects the identifiers of all POIs of a node.</summary>
  /// <param name="node">Node whose POIs are collected.</param>
  /// <returns>The set of POI identifiers; empty when the node has no POIs.</returns>
  private static HashSet<Guid> CollectPois(LilNode node)
  {
    var held = new HashSet<Guid>();
    foreach (var poi in node.Pois)
    {
      held.Add(poi.Id);
    }

    return held;
  }

  /// <summary>Appends an edge to the incident list of a node.</summary>
  /// <param name="adjacency">Incident passages per traversable node.</param>
  /// <param name="nodeId">Node the edge is incident to.</param>
  /// <param name="edge">Edge to add.</param>
  private static void AddIncident(Dictionary<Guid, List<LilEdge>> adjacency, Guid nodeId, LilEdge edge)
  {
    if (!adjacency.TryGetValue(nodeId, out var list))
    {
      list = [];
      adjacency[nodeId] = list;
    }

    list.Add(edge);
  }
}
