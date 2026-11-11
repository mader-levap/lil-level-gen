using System;
using System.Collections.Generic;
using System.Threading;
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
/// All floods run through <see cref="GraphFlood"/>, shared with the public
/// <see cref="Api.LevelGraphReach"/> query. Passage rules mirror the validator: one-way passages
/// lead From -> To only, bidirectional ones both ways, conditions apply in every direction and
/// secret passages count as ordinary passages. The pass never verifies the graph - it runs
/// before validation, so a null or empty graph is a no-op and self-loops or dangling edge
/// references are skipped rather than thrown.
/// </para>
/// <para>Deterministic: flood results are sets and lists are filled in ascending POI order.</para>
/// <para>Costly: may take noticeable amount of time for large graphs in certain circumstances.</para>
/// </remarks>
internal sealed class GraphMetadata(LilGraph? graph)
{
  /// <summary>Shared empty POI set: nothing initially held, nothing collectible. Never mutated.</summary>
  private static readonly HashSet<Guid> NoPois = [];

  /// <summary>Graph whose nodes receive reach metadata, or null.</summary>
  private readonly LilGraph? _graph = graph;

  /// <summary>
  /// Generates reach metadata for all nodes of the graph; a null or empty graph is a no-op.
  /// Existing entries are replaced, so calling the pass twice yields the same result.
  /// </summary>
  /// <param name="cancellationToken">Token checked before every flood.</param>
  /// <exception cref="OperationCanceledException">The token was cancelled; metadata may be partial.</exception>
  public void Generate(CancellationToken cancellationToken = default)
  {
    var graph = _graph;
    if (graph is null || graph.Nodes.Count == 0) return;

    // Overwrite instead of accumulate on a repeated run.
    foreach (var node in graph.Nodes)
    {
      node.Reach.Clear();
      node.OpenReach.Clear();
    }

    var flood = new GraphFlood(graph);
    var requiredPois = CollectRequiredPois(graph);

    // One independent analysis per start POI, in deterministic node then POI order.
    foreach (var node in graph.Nodes)
    {
      foreach (var poi in node.Pois)
      {
        if (poi.Type == EnPoiType.Start)
        {
          SolveFromStart(flood, node, requiredPois, poi.Id, cancellationToken);
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
  /// Computes the reach entries of every node reachable from one start POI: the base flood
  /// decides reachability, one blocked flood per obtained required POI decides necessity and
  /// one flood holding nothing decides open reachability.
  /// </summary>
  /// <param name="flood">Flood over the graph.</param>
  /// <param name="startNode">Node containing the start POI.</param>
  /// <param name="requiredPois">POIs required by some condition; the only POIs worth picking up.</param>
  /// <param name="startPoiId">Identifier of the start POI; the reach dictionary key.</param>
  /// <param name="cancellationToken">Token checked before every flood.</param>
  private static void SolveFromStart(
    GraphFlood flood,
    LilNode startNode,
    HashSet<Guid> requiredPois,
    Guid startPoiId,
    CancellationToken cancellationToken)
  {
    cancellationToken.ThrowIfCancellationRequested();
    var (reached, held) = flood.Run(startNode.Id, NoPois, requiredPois, blockedPoi: null);

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
      // k floods per start: the costly part of the pass, so each one is a checkpoint.
      cancellationToken.ThrowIfCancellationRequested();
      var (reachedWithout, _) = flood.Run(startNode.Id, NoPois, requiredPois, poiId);
      foreach (var nodeId in reached)
      { // Ascending candidate order keeps every list sorted.
        if (!reachedWithout.Contains(nodeId)) lists[nodeId].Add(poiId);
      }
    }

    foreach (var (nodeId, list) in lists)
    {
      flood.GetNode(nodeId).Reach[startPoiId] = list;
    }

    // Open flood: nothing is held or collected, so only passages without conditions are crossed.
    var (reachedOpenly, _) = flood.Run(startNode.Id, NoPois, NoPois, blockedPoi: null);
    foreach (var nodeId in reachedOpenly)
    {
      flood.GetNode(nodeId).OpenReach.Add(startPoiId);
    }
  }
}
