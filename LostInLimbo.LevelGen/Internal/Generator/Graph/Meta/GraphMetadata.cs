using System;
using System.Collections.Generic;
using LostInLimbo.LevelGen.Data.Result.Graph;

namespace LostInLimbo.LevelGen.Internal.Generator.Graph.Meta;

/// <summary>
/// Fills <see cref="LilNode.Reach"/> of every node: for each start POI, the solution POI
/// identifiers whose conditioned passages every route from that start to the node must use.
/// </summary>
/// <remarks>
/// <para>
/// Per start POI, a base key-door flood finds every reachable node: starting at the start node,
/// it repeatedly crosses every passage whose conditions are all held, where held means the POI
/// lies in an already reached node, until no new node is reached. Then, for each required POI
/// the base flood obtains, the flood is repeated with that POI treated as never held. Every node
/// reached by the base flood but not by the blocked one cannot be reached without that POI, so
/// the POI is listed for it. Indirect needs follow automatically: blocking a key also makes
/// every key behind it unobtainable.
/// </para>
/// <para>
/// One more flood per start holds nothing at all, so it crosses only passages without
/// conditions; every node it reaches lists that start in <see cref="LilNode.OpenReach"/>
/// (reachable without any solution), sorted ascending once all starts are done.
/// </para>
/// <para>
/// Only POIs that some condition requires are tracked; other POIs can never open a passage.
/// Cost per start is about <c>O(k² × (V + E))</c> for <c>k</c> required POIs - polynomial,
/// unlike the minimal-route-cost formulation, which is NP-hard.
/// </para>
/// <para>
/// Passage rules mirror the validator: one-way passages lead From -> To only, bidirectional
/// ones both ways, conditions apply in every direction and secret passages count as ordinary
/// passages. The pass never verifies the graph - it runs before validation, so a null or empty
/// graph is a no-op and self-loops or dangling edge references are skipped rather than thrown.
/// </para>
/// <para>Deterministic: flood results are sets and lists are filled in ascending POI order.</para>
/// </remarks>
internal sealed class GraphMetadata(LilGraph? graph)
{
  /// <summary>
  /// Shared empty set passed as "required POIs" to the open flood: with nothing worth holding,
  /// only passages without conditions can be crossed. Never mutated.
  /// </summary>
  private static readonly HashSet<Guid> NoPois = new();

  /// <summary>Graph whose nodes receive reach metadata, or null.</summary>
  private readonly LilGraph? _graph = graph;

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
      node.OpenReach.Clear();
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

    var requiredPois = CollectRequiredPois(graph);

    // One independent analysis per start POI, in deterministic node then POI order.
    foreach (var node in graph.Nodes)
    {
      foreach (var poi in node.Pois)
      {
        if (poi.Type == EnPoiType.Start)
        {
          SolveFromStart(node, nodeById, adjacency, requiredPois, poi.Id);
        }
      }
    }

    // Starts are visited in node order, not id order; sort so output is order-independent.
    foreach (var node in graph.Nodes)
    {
      node.OpenReach.Sort();
    }
  }

  /// <summary>
  /// Collects every POI identifier that at least one passage condition requires.
  /// </summary>
  /// <param name="graph">Graph whose passages are scanned.</param>
  /// <returns>Identifiers of all required POIs; empty when no passage has a condition.</returns>
  private static HashSet<Guid> CollectRequiredPois(LilGraph graph)
  {
    var required = new HashSet<Guid>();
    foreach (var edge in graph.Edges)
    {
      foreach (var condition in edge.Conditions)
      {
        required.Add(condition.RequiresPoi);
      }
    }

    return required;
  }

  /// <summary>
  /// Computes the reach entry of every node reachable from one start POI: the base flood
  /// decides reachability, one blocked flood per obtained required POI decides necessity.
  /// </summary>
  /// <param name="startNode">Node containing the start POI.</param>
  /// <param name="nodeById">Node index by identifier.</param>
  /// <param name="adjacency">Incident passages per traversable node.</param>
  /// <param name="requiredPois">POIs required by some condition.</param>
  /// <param name="startPoiId">Identifier of the start POI; the reach dictionary key.</param>
  private static void SolveFromStart(
    LilNode startNode,
    Dictionary<Guid, LilNode> nodeById,
    Dictionary<Guid, List<LilEdge>> adjacency,
    HashSet<Guid> requiredPois,
    Guid startPoiId)
  {
    var (reached, held) = Flood(startNode.Id, nodeById, adjacency, requiredPois, blockedPoi: null);

    // Unreachable nodes keep no entry; reached ones start with an empty requirement list.
    var lists = new Dictionary<Guid, List<Guid>>(reached.Count);
    foreach (var nodeId in reached)
    {
      lists[nodeId] = [];
    }

    // Only obtained keys can matter: an unobtainable key never opened a passage in the base flood.
    var candidates = new List<Guid>(held);
    candidates.Sort();
    foreach (var poiId in candidates)
    {
      var (reachedWithout, _) = Flood(startNode.Id, nodeById, adjacency, requiredPois, poiId);
      foreach (var nodeId in reached)
      {
        // Ascending candidate order keeps every list sorted.
        if (!reachedWithout.Contains(nodeId)) lists[nodeId].Add(poiId);
      }
    }

    foreach (var (nodeId, list) in lists)
    {
      nodeById[nodeId].Reach[startPoiId] = list;
    }

    // Open flood: nothing is ever held, so only passages without conditions are crossed.
    var (reachedOpenly, _) = Flood(startNode.Id, nodeById, adjacency, NoPois, blockedPoi: null);
    foreach (var nodeId in reachedOpenly)
    {
      nodeById[nodeId].OpenReach.Add(startPoiId);
    }
  }

  /// <summary>
  /// Key-door flood from a start node: crosses every passage whose conditions are all held,
  /// collecting required POIs of reached nodes, until nothing new is reached.
  /// </summary>
  /// <param name="startId">Identifier of the start node.</param>
  /// <param name="nodeById">Node index by identifier.</param>
  /// <param name="adjacency">Incident passages per traversable node.</param>
  /// <param name="requiredPois">POIs required by some condition; the only POIs worth holding.</param>
  /// <param name="blockedPoi">POI treated as never held, or null to block nothing.</param>
  /// <returns>The reached node identifiers and the held required POIs.</returns>
  private static (HashSet<Guid> Reached, HashSet<Guid> Held) Flood(
    Guid startId,
    Dictionary<Guid, LilNode> nodeById,
    Dictionary<Guid, List<LilEdge>> adjacency,
    HashSet<Guid> requiredPois,
    Guid? blockedPoi)
  {
    var reached = new HashSet<Guid> { startId };
    var held = new HashSet<Guid>();
    var queue = new Queue<Guid>();
    // Passages that were blocked by a missing key; retried whenever a new key arrives.
    var waiting = new List<(LilEdge Edge, Guid NextId)>();

    Arrive(startId);
    // Key count when waiting passages were last retried (none yet, so zero keys).
    var keysAtLastRetry = 0;
    while (true)
    {
      while (queue.Count > 0)
      {
        var nodeId = queue.Dequeue();
        if (!adjacency.TryGetValue(nodeId, out var incident)) continue;

        foreach (var edge in incident)
        {
          // Incidence direction is pre-filtered, so the other endpoint is always walkable.
          var nextId = edge.From == nodeId ? edge.To : edge.From;
          if (reached.Contains(nextId)) continue;
          if (HoldsAll(held, edge.Conditions)) Arrive(nextId);
          else waiting.Add((edge, nextId));
        }
      }

      // Fixpoint: no key gained since the last retry means no waiting passage can open.
      // Keys only grow, so the loop ends after at most one retry per key.
      if (held.Count == keysAtLastRetry) break;
      keysAtLastRetry = held.Count;

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

    // Marks a node reached, picks up its required POIs and schedules it for expansion.
    void Arrive(Guid nodeId)
    {
      reached.Add(nodeId);
      foreach (var poi in nodeById[nodeId].Pois)
      {
        if (poi.Id != blockedPoi && requiredPois.Contains(poi.Id)) held.Add(poi.Id);
      }

      queue.Enqueue(nodeId);
    }
  }

  /// <summary>Checks whether every condition of a passage is already held.</summary>
  /// <param name="held">POIs held by the flood.</param>
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
