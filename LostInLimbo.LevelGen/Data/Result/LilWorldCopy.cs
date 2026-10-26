using System;
using System.Collections.Generic;
using LostInLimbo.LevelGen.Data.Result.Graph;
using LostInLimbo.LevelGen.Data.Result.Stats;

namespace LostInLimbo.LevelGen.Data.Result;

/// <summary>
/// Creates deep copies of <see cref="LilWorld"/> data.
/// </summary>
/// <remarks>
/// <see cref="LilWorld"/> is a mutable record: a plain <c>with</c> expression would only copy
/// the top-level references and leave the copy sharing the source's graph containers, stats
/// and nested lists with the source. This helper detaches every mutable part instead, so the
/// copy can be handed to consumers (e.g. <see cref="Api.LevelGenSession.World"/>) or
/// stored without either side being able to affect the other through shared state.
/// <para>
  /// Maintenance rule: when mutable state is added to <see cref="LilWorld"/>, <see cref="LilGraph"/>,
  /// <see cref="LilNode"/>, <see cref="LilEdge"/> or <see cref="LilStats"/>, extend
  /// <see cref="Copy"/> accordingly so the detachment guarantee holds. Leaf records
  /// (<see cref="LilPoi"/>, <see cref="LilCondition"/>, <see cref="LilStyle"/>) are immutable
  /// (init-only) and are shared as list elements; they need no copying.
  /// Note that <see cref="LilNode.Reach"/> counts as mutable state even though its leaves are
  /// <see cref="Guid"/> values: the dictionary and its lists must be detached too.
/// </para>
/// Deterministic: the copy preserves all values, identifiers and iteration order of the source.
/// </remarks>
public static class LilWorldCopy
{
  /// <summary>
  /// Creates a deep copy of the given world: the copy shares no mutable state with the source,
  /// so mutating either world cannot affect the other. Identifiers (node, edge and POI
  /// <see cref="Guid"/>s) and list contents are preserved, so the copy carries the same data
  /// and still satisfies the graph validation invariants.
  /// </summary>
  /// <param name="source">World to copy, or <see langword="null"/>.</param>
  /// <returns>
  /// A new <see cref="LilWorld"/> instance detached from <paramref name="source"/>, or
  /// <see langword="null"/> when <paramref name="source"/> is <see langword="null"/>.
  /// </returns>
  public static LilWorld? Copy(LilWorld? source)
  {
    if (source is null) return null;

    return new LilWorld
    {
      Phase = source.Phase,
      Graph = CopyGraph(source.Graph),
      Map = source.Map with { }, // TODO: need to be updated when we actually implement map generation
      Stats = new LilStats { Steps = source.Stats.Steps },
    };
  }

  /// <summary>
  /// Copies a graph with fresh node and edge lists. Nodes and edges are recreated via
  /// <c>with</c> so their mutable nested lists (<see cref="LilNode.Pois"/>,
  /// <see cref="LilEdge.Conditions"/>) and dictionaries (<see cref="LilNode.Reach"/>) are
  /// detached as well; immutable leaf records inside them stay shared.
  /// </summary>
  /// <param name="source">Graph to copy. Must not be <see langword="null"/>.</param>
  /// <returns>A new <see cref="LilGraph"/> instance with fresh containers.</returns>
  private static LilGraph CopyGraph(LilGraph source)
  {
    var nodes = new List<LilNode>(source.Nodes.Count);
    foreach (var node in source.Nodes)
      nodes.Add(node with { Pois = [.. node.Pois], Reach = CopyReach(node.Reach) });

    var edges = new List<LilEdge>(source.Edges.Count);
    foreach (var edge in source.Edges)
      edges.Add(edge with { Conditions = [.. edge.Conditions] });

    return new LilGraph { Nodes = nodes, Edges = edges };
  }

  /// <summary>
  /// Copies a reach dictionary with fresh inner lists, so neither the dictionary nor any
  /// required list is shared between source and copy.
  /// </summary>
  /// <param name="source">Reach dictionary to copy. Must not be <see langword="null"/>.</param>
  /// <returns>A new dictionary containing new lists with the same values.</returns>
  private static Dictionary<Guid, List<Guid>> CopyReach(Dictionary<Guid, List<Guid>> source)
  {
    var copy = new Dictionary<Guid, List<Guid>>(source.Count);
    foreach (var (startPoiId, required) in source)
      copy[startPoiId] = [.. required];

    return copy;
  }
}
