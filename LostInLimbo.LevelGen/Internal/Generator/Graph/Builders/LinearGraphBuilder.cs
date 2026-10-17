using System;
using System.Collections.Generic;
using LostInLimbo.LevelGen.Data.Result.Graph;
using LostInLimbo.LevelGen.Internal.Generator.Graph.Distributor;
using LostInLimbo.LevelGen.Internal.Utils;

namespace LostInLimbo.LevelGen.Internal.Generator.Graph.Builders;

/// <summary>
/// <para>Builds a linear graph: a chain of nodes where the first node holds the start POI, the
/// last node holds the exit POI, and every node between links to both its previous and next
/// node.</para>
/// </summary>
/// <remarks>
/// <para>Rules:</para>
/// <list type="bullet">
/// <item>Node count follows <c>CnfGraph.Size</c>.</item>
/// <item><c>CnfGraph.Connectivity</c> is the amount of relationships above the minimum
/// (the spanning structure). That allows multiple passages from previous node to next node
/// and/or shortcuts (passage that skips one or more nodes in chain - must respect required
/// obstacles).
/// Builder can ignore excess Connectivity if it cannot make more relationships.</item>
/// <item><c>CnfGraph.Complexity</c> slightly indirectly determines amount of obstacles.
/// Like Connectivity, it is soft constraint.</item>
/// </list>
/// <para>Even this simple shape allows different scenarios:</para>
/// <list type="bullet">
/// <item>Every node holding a solution for the conditional passage to the next node.</item>
/// <item>Some passages are open, other require solution that may be found earlier.</item>
/// <item>Every passage but the last one is unconditional with the last one requiring all solutions.</item>
/// </list>
/// <para>The graph is built in five passes:</para>
/// <list type="bullet">
/// <item>A linearly connected skeleton.</item>
/// <item>Start and exit POIs always at beginning and end node respectively.</item>
/// <item>Extra relationships between nodes up to connectivity and <see cref="MaxExtraDegree"/>.</item>
/// <item>Create obstacles for relationships up to complexity and <see cref="MaxConditionPerRel"/>.</item>
/// <item>Relative sizes distributed over the finished nodes via <see cref="RelSizeDistributor"/>.</item>
/// </list>
/// </remarks>
internal sealed class LinearGraphBuilder : AGraphBuilder
{
  /// <summary>
  /// Maximum amount of extra relationships a node may gain in the extra-relationships phase (inclusive).
  /// Important: we count in and out relationships separately.
  /// </summary>
  internal const int MaxExtraDegree = 2;
  /// <summary>Maximum amount of conditions that single relationship can have.</summary>
  internal const int MaxConditionPerRel = 3;
  /// <summary>Maximum distance between solution and node having relationship with relevant condition.</summary>
  internal const int MaxConditionDistance = 3;
  /// <summary>Maximum amount of nodes that can be skipped by extra relationship.</summary>
  internal const int MaxSkipDistance = 2;

  /// <summary>
  /// Initial relative size of every node; replaced by the <see cref="RelSizeDistributor"/>
  /// pass once the whole graph (relationships and POIs included) is known.
  /// </summary>
  private const int NodeRelSize = 1;

  /// <summary>Passage ordinal of the skeleton edge between a node pair.</summary>
  private const int SkeletonPassage = 0;
  /// <summary>Passage ordinal of the extra edge between a node pair; one per pair at most.</summary>
  private const int ExtraPassage = 1;

  /// <summary>Salt for node identifiers.</summary>
  private const long SaltNode = 0x6E6F6465; // "node"
  /// <summary>Salt for edge identifiers.</summary>
  private const long SaltEdge = 0x65646765; // "edge"
  /// <summary>Salt for the start POI identifier.</summary>
  private const long SaltStartPoi = 0x7374617274; // "start"
  /// <summary>Salt for the exit POI identifier.</summary>
  private const long SaltExitPoi = 0x65786974; // "exit"

  /// <inheritdoc/>
  protected override bool Build()
  {
    var size = Config.Graph.Size;
    var connectivity = Config.Graph.Connectivity;
    var complexity = Config.Graph.Complexity;

    var nodeIds = CreateNodes(size);
    var extraOutDegrees = new int[size];
    var extraInDegrees = new int[size];
    var extraPairs = new bool[size, size];

    CreateSkeleton(size, nodeIds);
    PlacePois();
    AddExtraRelationships(connectivity, nodeIds, extraOutDegrees, extraInDegrees, extraPairs);
    AddObstacles(complexity);
    RelSizeDistributor.Distribute(Graph, RelSizeDistributionOptions.Default, PrepData.SeedGraph);

    // Soft constraints only; there is no configuration this builder cannot satisfy.
    return true;
  }

  /// <summary>
  /// Creates all nodes in chain order with deterministic identifiers.
  /// </summary>
  /// <param name="size">Amount of nodes to create.</param>
  /// <returns>Node identifiers in chain order.</returns>
  private Guid[] CreateNodes(int size)
  {
    var nodeIds = new Guid[size];
    for (var index = 0; index < size; index++)
    {
      // Nested derivation: index is the sub-channel, so ids are distinct per index
      // and never collide with role salts of other identifiers.
      var subSeed = SeedDerivation.Derive(PrepData.SeedGraph, SaltNode, index);
      var id = IdDerivation.Derive(subSeed, SaltNode);
      nodeIds[index] = id;
      Graph.Nodes.Add(new LilNode { Id = id, RelSize = NodeRelSize });
    }

    return nodeIds;
  }

  /// <summary>
  /// Creates the linear skeleton: every node links to its next node, from the first up to the
  /// last one. Uses exactly <c>size - 1</c> relationships - the minimum for a connected graph.
  /// </summary>
  /// <param name="size">Total amount of nodes, from <c>CnfGraph.Size</c>.</param>
  /// <param name="nodeIds">Node identifiers in chain order.</param>
  private void CreateSkeleton(int size, Guid[] nodeIds)
  {
    for (var i = 0; i + 1 < size; i++)
      AddEdge(i, i + 1, nodeIds, SkeletonPassage);
  }

  /// <summary>
  /// Places the start POI into the first node and the exit POI into the last node; both land
  /// on the single node when the graph has size one.
  /// </summary>
  private void PlacePois()
  {
    // Nodes were added in chain order by CreateNodes, so index lookup is exact.
    Graph.Nodes[0].Pois.Add(new LilPoi
    {
      Id = IdDerivation.Derive(PrepData.SeedGraph, SaltStartPoi),
      Type = EnPoiType.Start,
    });
    Graph.Nodes[^1].Pois.Add(new LilPoi
    {
      Id = IdDerivation.Derive(PrepData.SeedGraph, SaltExitPoi),
      Type = EnPoiType.Exit,
    });
  }

  /// <summary>
  /// Adds extra relationships on top of the skeleton until connectivity is spent or no
  /// eligible node pair remains under the constraints of <see cref="MaxExtraDegree"/> and
  /// <see cref="MaxSkipDistance"/>.
  /// </summary>
  /// <remarks>
  /// <para>One relationship is added per iteration:</para>
  /// <list type="number">
  /// <item>The source (node A) is picked uniformly among all nodes that are not the last node,
  /// still have out-capacity under <see cref="MaxExtraDegree"/> and can actually place a
  /// relationship forward - so a source without any eligible target never blocks the pass.</item>
  /// <item>The target (node B) is picked among nodes after A within <see cref="MaxSkipDistance"/>
  /// skipped nodes, with in-capacity under <see cref="MaxExtraDegree"/> and no extra
  /// relationship yet on the (A, B) pair. Targets are weighted linearly by remaining skips
  /// (adjacent most likely, see <see cref="PickTarget"/>).</item>
  /// </list>
  /// <para>When no eligible source (and therefore no eligible pair) remains, the pass ends
  /// silently - connectivity is a soft constraint. At most one extra relationship per node
  /// pair, so an adjacent pair holds at most two passages (skeleton plus extra) and a
  /// skipping pair at most one. No self-loops by construction.</para>
  /// </remarks>
  /// <param name="connectivity">Requested amount of extra relationships, from <c>CnfGraph.Connectivity</c>.</param>
  /// <param name="nodeIds">Node identifiers in chain order.</param>
  /// <param name="extraOutDegrees">Extra relationships per source node, updated in place.</param>
  /// <param name="extraInDegrees">Extra relationships per target node, updated in place.</param>
  /// <param name="extraPairs">Flags pairs that already carry an extra relationship, updated in place (both directions).</param>
  private void AddExtraRelationships(
    int connectivity,
    Guid[] nodeIds,
    int[] extraOutDegrees,
    int[] extraInDegrees,
    bool[,] extraPairs)
  {
    if (connectivity <= 0) return;

    var size = extraOutDegrees.Length;
    var added = 0;
    while (added < connectivity)
    {
      // Find all sources that are not full and have somewhere to link to.
      var sources = new List<int>();
      for (var from = 0; from < size - 1; from++)
      {
        if (extraOutDegrees[from] >= MaxExtraDegree) continue;
        if (HasEligibleTarget(from, extraInDegrees, extraPairs, size)) sources.Add(from);
      }

      // No source with an eligible target remains; excess connectivity is ignored.
      if (sources.Count == 0) break;

      var source = sources[Random.NextInt(sources.Count)];
      var target = PickTarget(source, extraInDegrees, extraPairs, size);

      AddEdge(source, target, nodeIds, ExtraPassage);
      extraOutDegrees[source]++;
      extraInDegrees[target]++;
      extraPairs[source, target] = true;
      extraPairs[target, source] = true;
      added++;
    }
  }

  /// <summary>
  /// Checks whether a source can place at least one extra relationship forward.
  /// </summary>
  /// <param name="from">Index of the source node; never the last node.</param>
  /// <param name="extraInDegrees">Extra relationships per target node.</param>
  /// <param name="extraPairs">Pairs that already carry an extra relationship.</param>
  /// <param name="size">Total amount of nodes.</param>
  /// <returns>True when at least one target within the skip window is eligible.</returns>
  private bool HasEligibleTarget(int from, int[] extraInDegrees, bool[,] extraPairs, int size)
  {
    var lastTarget = Math.Min(from + MaxSkipDistance + 1, size - 1);
    for (var to = from + 1; to <= lastTarget; to++)
    {
      if (extraPairs[from, to]) continue;
      if (extraInDegrees[to] < MaxExtraDegree) return true;
    }

    return false;
  }

  /// <summary>
  /// Picks a target for the given source among its eligible targets, weighted linearly by
  /// remaining skips: the adjacent node counts <see cref="MaxSkipDistance"/> + 1, one node
  /// skipped counts <see cref="MaxSkipDistance"/>, and so on down to 1 for the farthest skip.
  /// So the adjacent node is the most likely pick and likelihood falls with distance.
  /// </summary>
  /// <remarks>Deterministic for the same generator state; candidates are enumerated in
  /// chain order.</remarks>
  /// <param name="from">Index of the source node; must have at least one eligible target.</param>
  /// <param name="extraInDegrees">Extra relationships per target node.</param>
  /// <param name="extraPairs">Pairs that already carry an extra relationship.</param>
  /// <param name="size">Total amount of nodes.</param>
  /// <returns>Index of the chosen target node.</returns>
  /// <exception cref="InvalidOperationException">No eligible target remains; unreachable while
  /// <see cref="HasEligibleTarget"/> agreed that one exists.</exception>
  private int PickTarget(int from, int[] extraInDegrees, bool[,] extraPairs, int size)
  {
    var lastTarget = Math.Min(from + MaxSkipDistance + 1, size - 1);
    var totalWeight = 0;
    for (var to = from + 1; to <= lastTarget; to++)
    {
      if (extraPairs[from, to]) continue;
      if (extraInDegrees[to] >= MaxExtraDegree) continue;
      totalWeight += MaxSkipDistance + 1 - (to - from - 1);
    }

    if (totalWeight == 0)
      throw new InvalidOperationException("Extra relationship source has no eligible target.");

    // Roll once on the cumulative weights and walk the candidates to the picked one.
    var roll = Random.NextInt(totalWeight);
    for (var to = from + 1; to <= lastTarget; to++)
    {
      if (extraPairs[from, to]) continue;
      if (extraInDegrees[to] >= MaxExtraDegree) continue;
      roll -= MaxSkipDistance + 1 - (to - from - 1);
      if (roll < 0) return to;
    }

    throw new InvalidOperationException("Extra relationship weight roll landed outside the candidate list.");
  }

  /// <summary>
  /// Placeholder for the obstacle pass: creates no obstacles and no conditions yet, so every
  /// passage stays open and <c>CnfGraph.Complexity</c> is ignored.
  /// </summary>
  /// <remarks>Obstacles and conditions land together with the scenario selection: up to
  /// <see cref="MaxConditionPerRel"/> conditions per relationship, keeping every solution
  /// within <see cref="MaxConditionDistance"/> of the node that needs it.</remarks>
  /// <param name="complexity">Requested obstacle amount, from <c>CnfGraph.Complexity</c>; ignored for now.</param>
  private void AddObstacles(int complexity)
  {
    // Placeholder: the obstacle and condition pass lands later.
  }

  /// <summary>
  /// Adds one bidirectional edge between the two nodes in chain order.
  /// </summary>
  /// <param name="from">Index of the source node; must be earlier in the chain than <paramref name="to"/>.</param>
  /// <param name="to">Index of the target node; must differ from <paramref name="from"/>.</param>
  /// <param name="nodeIds">Node identifiers in chain order.</param>
  /// <param name="passage">Passage ordinal for the node pair; keeps parallel passages unique.</param>
  private void AddEdge(int from, int to, Guid[] nodeIds, int passage)
  {
    Graph.Edges.Add(new LilEdge
    {
      Id = DeriveEdgeId(from, to, passage),
      From = nodeIds[from],
      To = nodeIds[to],
    });
  }

  /// <summary>
  /// Derives an edge identifier from the ordered endpoint indices and the passage ordinal,
  /// so it is unique per node pair and passage - including the parallel passage of an
  /// adjacent pair that already carries a skeleton edge.
  /// </summary>
  /// <param name="fromIndex">Index of one endpoint node.</param>
  /// <param name="toIndex">Index of the other endpoint node.</param>
  /// <param name="passage">Passage ordinal for the node pair.</param>
  /// <returns>Identifier unique per node pair and passage. Not <see cref="Guid.Empty"/>.</returns>
  private Guid DeriveEdgeId(int fromIndex, int toIndex, int passage)
  {
    var low = Math.Min(fromIndex, toIndex);
    var high = Math.Max(fromIndex, toIndex);

    // Pack pair and passage into one sub-channel; 8 bits each leave room for up to
    // 255 nodes (CnfGraph.MaxSize is 100) and 255 passages per pair.
    var pairAndPassage = (long)(uint)low | ((long)(uint)high << 8) | ((long)(uint)passage << 16);
    var subSeed = SeedDerivation.Derive(PrepData.SeedGraph, SaltEdge, pairAndPassage);
    return IdDerivation.Derive(subSeed, SaltEdge);
  }
}
