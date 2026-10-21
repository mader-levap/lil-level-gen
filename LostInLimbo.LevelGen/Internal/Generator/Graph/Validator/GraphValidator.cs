using System;
using System.Collections.Generic;
using LostInLimbo.LevelGen.Api;
using LostInLimbo.LevelGen.Data.Result.Graph;

namespace LostInLimbo.LevelGen.Internal.Generator.Graph.Validator;

/// <summary>
/// Verifies that a level graph is beatable - an abstract simulation of a player who starts
/// at the start POI, walks through passages, collects solutions and reaches the exit without
/// ever getting softlocked.
/// </summary>
/// <remarks>
/// <para>
/// The check is a deterministic fixpoint flood-fill. Every node containing a start POI is
/// flooded first and its POIs are collected. Then, over and over, a passage becomes passable
/// once every one of its conditions references a collected POI; a passable passage floods the
/// node on its other end (respecting <see cref="EnPassageDirection"/>), which may collect more
/// solutions and unlock further passages. The loop stops when nothing new floods.
/// </para>
/// <para>
/// Failure reasons are reported from most specific to least specific, so the message points
/// at the root cause: a conditioned passage that can never be satisfied (softlock) first,
/// an unreachable exit second, and leftover unreachable nodes last as the catch-all (e.g. a
/// disconnected dead-end branch while the exit itself is fine). Structural problems
/// (missing nodes, dangling references, duplicate identifiers, nodes over their
/// <see cref="LilNode.MaxEdges"/> hard cap) are reported before any traversal. Each reason
/// is reported only for the first failing check.
/// </para>
/// <para>Instances hold per-call state, so create a new instance per validation
/// (or use <see cref="LevelGraphValidation"/>); one instance must not be shared across
/// concurrent calls. Safe to call in bulk seed loops.</para>
/// </remarks>
/// <param name="graph">Graph to verify.</param>
internal sealed class GraphValidator(LilGraph? graph)
{
  /// <summary>Graph to verify.</summary>
  private LilGraph? _graph = graph;
  /// <summary>All nodes mapped by id.</summary>
  private Dictionary<Guid, LilNode>? _nodesById;
  /// <summary>All start nodes.</summary>
  private HashSet<Guid> _startNodeIds = [];
  /// <summary>All end nodes.</summary>
  private HashSet<Guid> _exitNodeIds = [];
  /// <summary>All PoIs.</summary>
  private HashSet<Guid> _poiIds = [];
  /// <summary>Flooded nodes.</summary>
  private HashSet<Guid> _flooded = [];

  /// <summary>
  /// Validates the given level graph for beatability.
  /// </summary>
  /// <param name="graph">Graph to check; the map, stats and phase of the world are not read.</param>
  /// <returns>
  /// A result whose <see cref="GraphValidationResult.Success"/> is <see langword="true"/> when
  /// the graph is beatable; otherwise a failed result with the reason of the first failed check.
  /// </returns>
  public GraphValidationResult Validate()
  {
    GraphValidationResult? result = CheckFundamental();
    if (result != null) return result;

    result = Setup();
    if (result != null) return result;
    result = CheckEdges();
    if (result != null) return result;
    result = CheckMaxEdges();
    if (result != null) return result;
    result = CheckTraversals();
    if (result != null) return result;
    result = CheckExits();
    if (result != null) return result;

    return GraphValidationResult.Succeed();

  }

  /// <summary>
  /// Checks fundamentals.
  /// </summary>
  /// <returns>Null if success, otherwise failure.</returns>
  private GraphValidationResult? CheckFundamental()
  {
    if (_graph is null) return GraphValidationResult.Fail("Graph is null.");
    if (_graph.Nodes.Count == 0) return GraphValidationResult.Fail("Graph has no nodes.");
    return null;
  }

  /// <summary>
  /// Set ups internal data. Note it can fail here too.
  /// </summary>
  /// <returns>Null if success, otherwise failure.</returns>
  private GraphValidationResult? Setup()
  {
    _nodesById = new Dictionary<Guid, LilNode>(_graph!.Nodes.Count);
    foreach (var node in _graph.Nodes)
    {
      if (!_nodesById.TryAdd(node.Id, node)) return GraphValidationResult.Fail($"Duplicate node id {node.Id}.");
    }

    foreach (var node in _graph.Nodes)
    {
      foreach (var poi in node.Pois)
      {
        if (!_poiIds.Add(poi.Id)) return GraphValidationResult.Fail($"Duplicate POI id {poi.Id}.");

        if (poi.Type == EnPoiType.Start) _startNodeIds.Add(node.Id);
        else if (poi.Type == EnPoiType.Exit) _exitNodeIds.Add(node.Id);
      }
    }

    if (_startNodeIds.Count == 0) return GraphValidationResult.Fail("Graph has no Start PoI.");
    if (_exitNodeIds.Count == 0) return GraphValidationResult.Fail("Graph has no Exit PoI.");

    return null;
  }

  /// <summary>
  /// Check edges.
  /// </summary>
  /// <returns>Null if success, otherwise failure.</returns>
  private GraphValidationResult? CheckEdges()
  {
    foreach (var edge in _graph!.Edges)
    {
      if (!_nodesById!.ContainsKey(edge.From))
        return GraphValidationResult.Fail($"Edge {edge.Id} references missing node {edge.From}.");

      if (!_nodesById!.ContainsKey(edge.To))
        return GraphValidationResult.Fail($"Edge {edge.Id} references missing node {edge.To}.");

      if (edge.From == edge.To)
        return GraphValidationResult.Fail($"Edge {edge.Id} connects node to itself.");

      foreach (var condition in edge.Conditions)
      {
        if (!_poiIds.Contains(condition.RequiresPoi))
          return GraphValidationResult.Fail($"Condition on edge {edge.Id} references missing POI {condition.RequiresPoi}.");
      }
    }
    return null;
  }

  /// <summary>
  /// Checks that no node exceeds its <see cref="LilNode.MaxEdges"/> hard cap; a negative
  /// cap means no limit. Counts every relationship touching the node, from either end.
  /// </summary>
  /// <remarks>Runs after <see cref="CheckEdges"/>, so every edge endpoint is known to exist.</remarks>
  /// <returns>Null if success, otherwise failure.</returns>
  private GraphValidationResult? CheckMaxEdges()
  {
    var degrees = new Dictionary<Guid, int>(_graph!.Nodes.Count);
    foreach (var node in _graph.Nodes)
    {
      degrees[node.Id] = 0;
    }

    foreach (var edge in _graph.Edges)
    {
      degrees[edge.From]++;
      degrees[edge.To]++;
    }

    foreach (var node in _graph.Nodes)
    {
      if (node.MaxEdges >= 0 && degrees[node.Id] > node.MaxEdges)
        return GraphValidationResult.Fail(
          $"Node {node.Id} has {degrees[node.Id]} relationships, over its MaxEdges cap of {node.MaxEdges}.");
    }

    return null;
  }

  /// <summary>
  /// Traversal pass: flood from every start node until no new node becomes reachable.
  /// </summary>
  /// <returns>Null if success, otherwise failure.</returns>
  private GraphValidationResult? CheckTraversals()
  {
    var collectedPois = new HashSet<Guid>();

    foreach (var nodeId in _startNodeIds!)
      FloodNode(_flooded, collectedPois, nodeId);

    var changed = true;
    while (changed)
    {
      changed = false;
      foreach (var edge in _graph!.Edges)
      {
        if (!IsPassable(edge, collectedPois)) continue;

        // Forward direction: From -> To, valid for both passage directions.
        if (_flooded.Contains(edge.From) && FloodNode(_flooded, collectedPois, edge.To)) changed = true;

        // Backward direction: only open passages can be walked against their From -> To sense.
        if (edge.Direction == EnPassageDirection.Bidirectional
            && _flooded.Contains(edge.To)
            && FloodNode(_flooded, collectedPois, edge.From))
          changed = true;
      }
    }

    // Outcome pass, most specific failure first (see type remarks).
    foreach (var edge in _graph!.Edges)
    {
      if (edge.Conditions.Count > 0 && !IsPassable(edge, collectedPois))
        return GraphValidationResult.Fail($"Conditioned edge {edge.Id} can never be satisfied.");
    }
    return null;
  }

  /// <summary>
  /// Check exits.
  /// </summary>
  /// <returns>Null if success, otherwise failure.</returns>
  private GraphValidationResult? CheckExits()
  {
    var exitReachable = false;
    foreach (var nodeId in _exitNodeIds!)
    {
      if (_flooded.Contains(nodeId))
      {
        exitReachable = true;
        break;
      }
    }

    if (!exitReachable) return GraphValidationResult.Fail("Exit PoI is not reachable from Start.");

    var unreachableCount = _graph!.Nodes.Count - _flooded.Count;
    if (unreachableCount > 0)
      return GraphValidationResult.Fail($"{unreachableCount} node(s) unreachable from Start.");
    return null;
  }

  /// <summary>Floods a node (marks it reachable) and collects its POIs; returns false when the node
  /// was already flooded. Collecting on flood models the player picking up everything
  /// inside a place as soon as the place becomes accessible.</summary>
  /// <param name="flooded">Set of ids of flooded nodes.</param>
  /// <param name="collectedPois">Set of ids of found PoIs.</param>
  /// <param name="nodeId">Node identifier.</param>
  /// <returns>True if flood successful, otherwise false.</returns>
  private bool FloodNode(HashSet<Guid> flooded, HashSet<Guid> collectedPois, Guid nodeId)
  {
    if (!flooded.Add(nodeId)) return false;

    foreach (var poi in _nodesById![nodeId].Pois)
      collectedPois.Add(poi.Id);

    return true;
  }

  /// <summary>
  /// Checks whether a passage can be used given the POIs collected so far.
  /// </summary>
  /// <param name="edge">Passage to check.</param>
  /// <param name="collectedPois">Identifiers of POIs inside already flooded nodes.</param>
  /// <returns>True when every condition of the passage references a collected POI.</returns>
  private static bool IsPassable(LilEdge edge, HashSet<Guid> collectedPois)
  {
    foreach (var condition in edge.Conditions)
    {
      if (!collectedPois.Contains(condition.RequiresPoi)) return false;
    }
    return true;
  }
}
