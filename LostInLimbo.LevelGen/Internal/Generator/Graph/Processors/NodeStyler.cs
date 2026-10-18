using System;
using System.Collections.Generic;
using System.Globalization;
using LostInLimbo.LevelGen.Data.Result.Graph;
using LostInLimbo.LevelGen.Internal.Utils;

namespace LostInLimbo.LevelGen.Internal.Generator.Graph.Processors;

/// <summary>
/// <para>
/// Graph pass that assigns <see cref="LilStyle.Group"/> in <see cref="LilNode.Style"/> to
/// every node. It runs after nodes, relationships and POIs are known.
/// </para>
/// </summary>
/// <remarks>
/// <para>
/// Every node ends up in exactly one group. Groups are named <c>g1</c>, <c>g2</c>, ...
/// zero-padded to the width of the actual group count (e.g. <c>g01</c> for 12 groups).
/// <see cref="NodeStylerOpt.GroupCount"/> is a soft target: a graph with fewer nodes
/// than requested groups simply drops the excess and uses one group per node.
/// </para>
/// <para>
/// Algorithm: the node count is first split into random positive per-group targets
/// (at least one node per group, exact total). Every group then claims a random
/// ungrouped seed node, after which groups grow in cycles: a group picks one of its
/// members that still reaches an ungrouped node over a traversal-eligible relationship
/// and absorbs that neighbour. Growth stops for a group once its target is reached -
/// the targets are hard capacities, so the final group sizes always match the split.
/// Secret, one-way and conditional relationships are skipped while any group can still
/// grow through an ordinary relationship; only when every group has run out does the
/// pass fall back to them (one-way still only from source to destination). When growth
/// stalls completely, a single leftover node is dumped into a random group that still
/// has spare capacity - this breaks contiguity, but only ever as a last resort - and
/// the pass starts growing again, because the dumped node may open new frontiers.
/// </para>
/// <para>
/// Deterministic for the same graph contents, options and seed: the generator derives
/// its own sub-seed from a dedicated salt, nodes are visited in Id order, adjacency
/// lists are built in relationship-Id order and the initial seed shuffle works on the
/// Id-ordered node list, so list insertion order cannot influence the result. Only
/// <see cref="LilStyle.Group"/> is modified; identifiers, sizes, POIs and edges stay
/// untouched.
/// </para>
/// </remarks>
internal static class NodeStyler
{
  /// <summary>Salt for the distribution sub-seed; keeps this pass off other passes' streams.</summary>
  private const long SaltStyle = 0x32658C73697A65; // "style"

  /// <summary>
  /// Assigns <see cref="LilStyle.Group"/> in <see cref="LilNode.Style"/> to every node
  /// of <paramref name="graph"/>.
  /// </summary>
  /// <param name="graph">Graph whose nodes are rewritten in place; may be empty.</param>
  /// <param name="options">Node styling parameters; validated before use.</param>
  /// <param name="seedGraph">Graph sub-seed the distribution stream is derived from.</param>
  /// <exception cref="ArgumentNullException"><paramref name="graph"/> or <paramref name="options"/> is null.</exception>
  /// <exception cref="ArgumentOutOfRangeException">Options are out of range; see <see cref="NodeStylerOpt.Validate"/>.</exception>
  /// <exception cref="InvalidOperationException">An edge references a node missing from the graph, or two nodes share an identifier.</exception>
  public static void Execute(LilGraph graph, NodeStylerOpt options, long seedGraph)
  {
    if (graph is null) throw new ArgumentNullException(nameof(graph));
    if (options is null) throw new ArgumentNullException(nameof(options));

    // Validation runs even for empty graphs, so bad options never pass silently.
    options.Validate();
    if (graph.Nodes.Count == 0) return;

    var random = new SeededRandom(SeedDerivation.Derive(seedGraph, SaltStyle));
    var nodeCount = graph.Nodes.Count;
    var groupCount = Math.Min(options.GroupCount, nodeCount);

    var adjacency = BuildAdjacency(graph);
    var idOrder = BuildVisitOrder(graph);
    var names = BuildGroupNames(groupCount);
    var capacity = SampleGroupCounts(nodeCount, groupCount, random);
    var groupOf = new string?[nodeCount];

    // Scratch buffers shared by all growth attempts, like the sizer's weight buffer.
    var sources = new List<int>();
    var targets = new List<int>();

    // Every group claims one random ungrouped node as its seed.
    var seedOrder = new List<int>(idOrder);
    random.Shuffle(seedOrder);
    var ungrouped = nodeCount;
    for (var groupIndex = 0; groupIndex < groupCount; groupIndex++)
    {
      groupOf[seedOrder[groupIndex]] = names[groupIndex];
      capacity[groupIndex]--;
      ungrouped--;
    }

    // Grow, fall back to restricted relationships, dump one leftover, repeat.
    // Invariant: the spare capacity always equals the ungrouped node count, so every
    // iteration makes progress and the loop terminates after at most nodeCount rounds.
    while (ungrouped > 0)
    {
      var grown = GrowCycle(graph, adjacency, idOrder, groupOf, capacity, names, sources, targets, random, strict: true);
      if (grown == 0)
        grown = GrowCycle(graph, adjacency, idOrder, groupOf, capacity, names, sources, targets, random, strict: false);
      if (grown == 0)
        grown = AssignOneLeftover(idOrder, groupOf, capacity, names, random);

      ungrouped -= grown;
    }

    // Verify that all nodes have assigned group.
    for (var i = 0; i < nodeCount; i++)
    {
      var name = groupOf[i];
      if (name is null)
        throw new InvalidOperationException($"Node {graph.Nodes[i].Id} was left without a group.");

      graph.Nodes[i] = graph.Nodes[i] with { Style = graph.Nodes[i].Style with { Group = name } };
    }
  }

  //
  // Internals
  //

  /// <summary>
  /// Runs growth cycles until a full cycle over all groups produces no assignment.
  /// </summary>
  /// <param name="graph">Graph whose edges are consulted for eligibility.</param>
  /// <param name="adjacency">Per-node traversal neighbours built by <see cref="BuildAdjacency"/>.</param>
  /// <param name="idOrder">Node indices in ascending Id order.</param>
  /// <param name="groupOf">Group name per node index; rewritten in place.</param>
  /// <param name="capacity">Remaining nodes per group; decremented in place.</param>
  /// <param name="names">Group names indexed by group index.</param>
  /// <param name="sources">Scratch buffer for candidate source nodes.</param>
  /// <param name="targets">Scratch buffer for eligible growth targets.</param>
  /// <param name="random">Seeded generator providing the draws.</param>
  /// <param name="strict">When true, secret, one-way and conditional relationships are skipped.</param>
  /// <returns>Amount of nodes grouped by this call.</returns>
  private static int GrowCycle(
    LilGraph graph,
    List<(int Target, int EdgeIndex)>[] adjacency,
    List<int> idOrder,
    string?[] groupOf,
    int[] capacity,
    string[] names,
    List<int> sources,
    List<int> targets,
    SeededRandom random,
    bool strict)
  {
    var grown = 0;
    while (true)
    {
      // One growth step per group per pass, in group-name order, so every group gets
      // an equal chance to expand before the cycle restarts.
      var grewThisPass = false;
      for (var groupIndex = 0; groupIndex < names.Length; groupIndex++)
      {
        if (capacity[groupIndex] <= 0) continue;
        if (!TryGrowOne(graph, adjacency, idOrder, groupOf, capacity, names[groupIndex], groupIndex, sources, targets, random, strict))
          continue;

        grewThisPass = true;
        grown++;
      }

      if (!grewThisPass) break;
    }

    return grown;
  }

  /// <summary>
  /// Performs a single growth step for one group: picks a random member that still
  /// reaches an ungrouped node, then a random eligible relationship from it.
  /// </summary>
  /// <param name="graph">Graph whose edges are consulted for eligibility.</param>
  /// <param name="adjacency">Per-node traversal neighbours.</param>
  /// <param name="idOrder">Node indices in ascending Id order.</param>
  /// <param name="groupOf">Group name per node index; rewritten in place.</param>
  /// <param name="capacity">Remaining nodes per group; decremented in place.</param>
  /// <param name="name">Group name of the growing group.</param>
  /// <param name="groupIndex">Index of the growing group.</param>
  /// <param name="sources">Scratch buffer for candidate source nodes.</param>
  /// <param name="targets">Scratch buffer for eligible growth targets.</param>
  /// <param name="random">Seeded generator providing the draws.</param>
  /// <param name="strict">When true, secret, one-way and conditional relationships are skipped.</param>
  /// <returns>True when a node was added to the group; false when no growth is possible.</returns>
  private static bool TryGrowOne(
    LilGraph graph,
    List<(int Target, int EdgeIndex)>[] adjacency,
    List<int> idOrder,
    string?[] groupOf,
    int[] capacity,
    string name,
    int groupIndex,
    List<int> sources,
    List<int> targets,
    SeededRandom random,
    bool strict)
  {
    // Members in Id order that still reach an ungrouped node.
    sources.Clear();
    foreach (var nodeIndex in idOrder)
    {
      if (groupOf[nodeIndex] != name) continue;
      CollectTargets(graph, adjacency, nodeIndex, groupOf, strict, targets);
      if (targets.Count > 0) sources.Add(nodeIndex);
    }

    if (sources.Count == 0) return false;

    // Pick one eligible member, then one of its eligible relationships.
    var source = sources[random.NextInt(sources.Count)];
    CollectTargets(graph, adjacency, source, groupOf, strict, targets);
    var absorbed = targets[random.NextInt(targets.Count)];

    groupOf[absorbed] = name;
    capacity[groupIndex]--;
    return true;
  }

  /// <summary>
  /// Collects the ungrouped nodes reachable from one node over eligible relationships.
  /// </summary>
  /// <param name="graph">Graph whose edges are consulted for eligibility.</param>
  /// <param name="adjacency">Per-node traversal neighbours.</param>
  /// <param name="nodeIndex">Index of the node to expand from.</param>
  /// <param name="groupOf">Group name per node index.</param>
  /// <param name="strict">When true, secret, one-way and conditional relationships are skipped.</param>
  /// <param name="targets">Scratch buffer; cleared and refilled with target node indices.</param>
  private static void CollectTargets(
    LilGraph graph,
    List<(int Target, int EdgeIndex)>[] adjacency,
    int nodeIndex,
    string?[] groupOf,
    bool strict,
    List<int> targets)
  {
    targets.Clear();
    foreach (var (target, edgeIndex) in adjacency[nodeIndex])
    {
      if (groupOf[target] is not null) continue;
      if (strict && IsRestricted(graph.Edges[edgeIndex])) continue;
      targets.Add(target);
    }
  }

  /// <summary>
  /// Checks whether a relationship is held back until all groups ran out of ordinary ones.
  /// </summary>
  /// <param name="edge">Relationship to check.</param>
  /// <returns>True for secret, one-way or conditional relationships.</returns>
  private static bool IsRestricted(LilEdge edge)
  {
    return edge.Secret
        || edge.Direction == EnPassageDirection.OneWay
        || edge.Conditions.Count > 0;
  }

  /// <summary>
  /// Dumps one random ungrouped node into a random group that still has spare capacity.
  /// </summary>
  /// <param name="idOrder">Node indices in ascending Id order.</param>
  /// <param name="groupOf">Group name per node index; rewritten in place.</param>
  /// <param name="capacity">Remaining nodes per group; decremented in place.</param>
  /// <param name="names">Group names indexed by group index.</param>
  /// <param name="random">Seeded generator providing the draws.</param>
  /// <returns>Always 1 - one node was grouped.</returns>
  /// <exception cref="InvalidOperationException">An invariant of the capacity split was violated.</exception>
  private static int AssignOneLeftover(
    List<int> idOrder,
    string?[] groupOf,
    int[] capacity,
    string[] names,
    SeededRandom random)
  {
    var ungrouped = new List<int>();
    foreach (var nodeIndex in idOrder)
    {
      if (groupOf[nodeIndex] is null) ungrouped.Add(nodeIndex);
    }

    var underCapacity = new List<int>();
    for (var groupIndex = 0; groupIndex < names.Length; groupIndex++)
    {
      if (capacity[groupIndex] > 0) underCapacity.Add(groupIndex);
    }

    // The split guarantees spare capacity equals ungrouped count; anything else is a bug.
    if (ungrouped.Count == 0 || underCapacity.Count == 0)
      throw new InvalidOperationException("No group has spare capacity while nodes remain ungrouped.");

    var node = ungrouped[random.NextInt(ungrouped.Count)];
    var target = underCapacity[random.NextInt(underCapacity.Count)];
    groupOf[node] = names[target];
    capacity[target]--;
    return 1;
  }

  /// <summary>
  /// Builds the per-node traversal neighbours and validates that every edge endpoint exists.
  /// </summary>
  /// <param name="graph">Graph to inspect.</param>
  /// <returns>
  /// One list per node index holding <c>(target node index, edge index)</c> pairs in
  /// ascending edge-Id order. One-way relationships only appear at their source end, so
  /// direction is enforced by the structure itself; self-loops are dropped because they
  /// cannot reach another node.
  /// </returns>
  /// <exception cref="InvalidOperationException">An edge references a missing node.</exception>
  /// <exception cref="ArgumentException">Two nodes share an identifier.</exception>
  private static List<(int Target, int EdgeIndex)>[] BuildAdjacency(LilGraph graph)
  {
    var indexById = new Dictionary<Guid, int>(graph.Nodes.Count);
    for (var i = 0; i < graph.Nodes.Count; i++)
    {
      indexById.Add(graph.Nodes[i].Id, i);
    }

    var adjacency = new List<(int, int)>[graph.Nodes.Count];
    for (var i = 0; i < adjacency.Length; i++)
    {
      adjacency[i] = new List<(int, int)>();
    }

    // Visit edges in Id order so every adjacency list has a stable order.
    var edgeOrder = new List<int>(graph.Edges.Count);
    for (var i = 0; i < graph.Edges.Count; i++)
    {
      edgeOrder.Add(i);
    }

    edgeOrder.Sort((left, right) => graph.Edges[left].Id.CompareTo(graph.Edges[right].Id));

    foreach (var edgeIndex in edgeOrder)
    {
      var edge = graph.Edges[edgeIndex];
      if (!indexById.TryGetValue(edge.From, out var from))
        throw new InvalidOperationException($"Edge {edge.Id} references unknown node {edge.From}.");
      if (!indexById.TryGetValue(edge.To, out var to))
        throw new InvalidOperationException($"Edge {edge.Id} references unknown node {edge.To}.");

      if (from == to) continue;

      adjacency[from].Add((to, edgeIndex));
      if (edge.Direction == EnPassageDirection.Bidirectional)
      {
        adjacency[to].Add((from, edgeIndex));
      }
    }

    return adjacency;
  }

  /// <summary>
  /// Produces the node indices in ascending Id order.
  /// </summary>
  /// <param name="graph">Graph whose nodes are visited.</param>
  /// <returns>Node indices sorted by <see cref="LilNode.Id"/>.</returns>
  private static List<int> BuildVisitOrder(LilGraph graph)
  {
    var order = new List<int>(graph.Nodes.Count);
    for (var i = 0; i < graph.Nodes.Count; i++)
    {
      order.Add(i);
    }

    order.Sort((left, right) => graph.Nodes[left].Id.CompareTo(graph.Nodes[right].Id));
    return order;
  }

  /// <summary>
  /// Builds the group names <c>g1..gK</c>, zero-padded to the width of <paramref name="groupCount"/>.
  /// </summary>
  /// <param name="groupCount">Actual amount of groups; at least 1.</param>
  /// <returns>One name per group index.</returns>
  private static string[] BuildGroupNames(int groupCount)
  {
    var width = groupCount.ToString(CultureInfo.InvariantCulture).Length;
    var names = new string[groupCount];
    for (var groupIndex = 0; groupIndex < groupCount; groupIndex++)
    {
      names[groupIndex] = "g" + (groupIndex + 1).ToString("D" + width, CultureInfo.InvariantCulture);
    }

    return names;
  }

  /// <summary>
  /// Splits <paramref name="nodeCount"/> nodes into random positive per-group targets
  /// that sum up to the node count exactly.
  /// </summary>
  /// <param name="nodeCount">Amount of nodes to distribute; at least 1.</param>
  /// <param name="groupCount">Amount of groups; between 1 and <paramref name="nodeCount"/>.</param>
  /// <param name="random">Seeded generator providing the draws.</param>
  /// <returns>One positive target per group, summing to <paramref name="nodeCount"/>.</returns>
  private static int[] SampleGroupCounts(int nodeCount, int groupCount, SeededRandom random)
  {
    var counts = new int[groupCount];
    if (groupCount == 1)
    {
      counts[0] = nodeCount;
      return counts;
    }

    // Cut points between consecutive positions; sorted cuts split the run of nodes into
    // groupCount positive parts. Shuffling first and keeping groupCount - 1 of them makes
    // the split random while the sorted application keeps it deterministic.
    var cuts = new List<int>(nodeCount - 1);
    for (var position = 1; position < nodeCount; position++)
    {
      cuts.Add(position);
    }

    random.Shuffle(cuts);
    cuts.RemoveRange(groupCount - 1, cuts.Count - (groupCount - 1));
    cuts.Sort();

    var previous = 0;
    for (var groupIndex = 0; groupIndex < groupCount - 1; groupIndex++)
    {
      counts[groupIndex] = cuts[groupIndex] - previous;
      previous = cuts[groupIndex];
    }

    counts[groupCount - 1] = nodeCount - previous;
    return counts;
  }
}
