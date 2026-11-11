using System;
using System.Collections.Generic;
using LostInLimbo.LevelGen.Data.Result.Graph;
using LostInLimbo.LevelGen.Internal.Generator.Graph.Meta;

namespace LostInLimbo.LevelGen.Api;

/// <summary>
/// Public entry point for runtime reachability queries on a level graph: which places the
/// player can walk to from a start POI or any node with exactly the solutions they currently hold.
/// </summary>
/// <remarks>
/// <para>
/// Intended for gameplay decisions that must not let the player bypass obstacles. Call it again
/// whenever the held solutions change.
/// </para>
/// <para>
/// Every combination of held solutions is answered exactly, including places reachable through
/// several alternative sets of solutions - something per-node precomputed metadata such as
/// <see cref="LilNode.Reach"/> cannot express cheaply.
/// </para>
/// <para>
/// Cost is linear in the size of the graph (<c>O(V + E)</c>) per call. The graph is only read.
/// </para>
/// </remarks>
public static class LevelGraphReach
{
  /// <summary>
  /// Returns the nodes reachable from the given start POI when crossing only passages whose
  /// conditions are all among <paramref name="heldPois"/>.
  /// </summary>
  /// <remarks>
  /// <para>
  /// Only <paramref name="heldPois"/> count: solutions lying in reached nodes are not picked up.
  /// Held POIs that no condition requires are ignored. One-way passages lead From -> To only,
  /// bidirectional ones both ways, and conditions apply in every direction. Secret passages and
  /// secret nodes count like ordinary ones, whether discovered or not, so a normal node reachable
  /// only through a secret node is reachable. The result includes secret nodes; filter them out
  /// where they do not apply.
  /// </para>
  /// <para>
  /// With no POIs held, the result equals the nodes whose <see cref="LilNode.OpenReach"/>
  /// contains <paramref name="startPoiId"/>.
  /// </para>
  /// <para>Deterministic: node identifiers are returned in <see cref="LilGraph.Nodes"/> order.</para>
  /// </remarks>
  /// <param name="graph">Level graph to query; not modified.</param>
  /// <param name="startPoiId">Identifier of the <see cref="EnPoiType.Start"/> POI to walk from.</param>
  /// <param name="heldPois">Identifiers of the POIs (solutions) the player holds; empty for none.</param>
  /// <returns>Identifiers of the reachable nodes, always including the node of the start POI.</returns>
  /// <exception cref="ArgumentNullException"><paramref name="graph"/> or <paramref name="heldPois"/> is <see langword="null"/>.</exception>
  /// <exception cref="ArgumentException">No node of <paramref name="graph"/> holds a start POI with identifier <paramref name="startPoiId"/>.</exception>
  public static IReadOnlyList<Guid> GetReachableNodes(LilGraph graph, Guid startPoiId, IReadOnlyCollection<Guid> heldPois)
  {
    if (graph is null) throw new ArgumentNullException(nameof(graph));
    if (heldPois is null) throw new ArgumentNullException(nameof(heldPois));

    var startNode = FindStartNode(graph, startPoiId)
      ?? throw new ArgumentException($"No node holds a start POI with identifier {startPoiId}.", nameof(startPoiId));

    return Flood(graph, startNode.Id, heldPois);
  }

  /// <summary>
  /// Returns the nodes reachable from the given node when crossing only passages whose
  /// conditions are all among <paramref name="heldPois"/>.
  /// </summary>
  /// <remarks>
  /// <para>
  /// Same rules as <see cref="GetReachableNodes(LilGraph, Guid, IReadOnlyCollection{Guid})"/>,
  /// but walking from any node of the graph - it does not need to hold a start POI. Useful for
  /// queries from a place other than the level start.
  /// </para>
  /// <para>
  /// <paramref name="startNode"/> is matched by <see cref="LilNode.Id"/>, not by reference, so a
  /// node taken from a copy of the graph (see <see cref="Data.Result.LilWorldCopy"/>) works too.
  /// </para>
  /// <para>Deterministic: node identifiers are returned in <see cref="LilGraph.Nodes"/> order.</para>
  /// </remarks>
  /// <param name="graph">Level graph to query; not modified.</param>
  /// <param name="startNode">Node to walk from; must belong to <paramref name="graph"/>.</param>
  /// <param name="heldPois">Identifiers of the POIs (solutions) the player holds; empty for none.</param>
  /// <returns>Identifiers of the reachable nodes, always including <paramref name="startNode"/>.</returns>
  /// <exception cref="ArgumentNullException"><paramref name="graph"/>, <paramref name="startNode"/> or <paramref name="heldPois"/> is <see langword="null"/>.</exception>
  /// <exception cref="ArgumentException"><paramref name="graph"/> has no node with the identifier of <paramref name="startNode"/>.</exception>
  public static IReadOnlyList<Guid> GetReachableNodes(LilGraph graph, LilNode startNode, IReadOnlyCollection<Guid> heldPois)
  {
    if (graph is null) throw new ArgumentNullException(nameof(graph));
    if (startNode is null) throw new ArgumentNullException(nameof(startNode));
    if (heldPois is null) throw new ArgumentNullException(nameof(heldPois));

    if (!ContainsNode(graph, startNode.Id))
      throw new ArgumentException($"The graph has no node with identifier {startNode.Id}.", nameof(startNode));

    return Flood(graph, startNode.Id, heldPois);
  }

  /// <summary>Floods from a node of the graph with fixed held POIs and reports the result in graph order.</summary>
  /// <param name="graph">Level graph; not modified.</param>
  /// <param name="startNodeId">Identifier of a node of <paramref name="graph"/>.</param>
  /// <param name="heldPois">Held POIs; nothing else is picked up.</param>
  /// <returns>Identifiers of the reachable nodes in <see cref="LilGraph.Nodes"/> order, each once.</returns>
  private static IReadOnlyList<Guid> Flood(LilGraph graph, Guid startNodeId, IReadOnlyCollection<Guid> heldPois)
  {
    // Held POIs are fixed: nothing is collectible along the way.
    var flood = new GraphFlood(graph);
    var (reached, _) = flood.Run(startNodeId, heldPois, new HashSet<Guid>(), blockedPoi: null);

    // Report in graph order; a duplicated node identifier is reported once.
    var result = new List<Guid>(reached.Count);
    var reported = new HashSet<Guid>();
    foreach (var node in graph.Nodes)
    {
      if (reached.Contains(node.Id) && reported.Add(node.Id)) result.Add(node.Id);
    }

    return result;
  }

  /// <summary>Checks whether the graph has a node with the given identifier.</summary>
  /// <param name="graph">Graph to search.</param>
  /// <param name="nodeId">Node identifier.</param>
  /// <returns>True when a node with that identifier exists.</returns>
  private static bool ContainsNode(LilGraph graph, Guid nodeId)
  {
    foreach (var node in graph.Nodes)
    {
      if (node.Id == nodeId) return true;
    }

    return false;
  }

  /// <summary>Finds the node holding the start POI with the given identifier.</summary>
  /// <param name="graph">Graph to search.</param>
  /// <param name="startPoiId">Identifier of the start POI.</param>
  /// <returns>The first node holding it, or <see langword="null"/> when there is none.</returns>
  private static LilNode? FindStartNode(LilGraph graph, Guid startPoiId)
  {
    foreach (var node in graph.Nodes)
    {
      foreach (var poi in node.Pois)
      {
        if (poi.Id == startPoiId && poi.Type == EnPoiType.Start) return node;
      }
    }

    return null;
  }
}
