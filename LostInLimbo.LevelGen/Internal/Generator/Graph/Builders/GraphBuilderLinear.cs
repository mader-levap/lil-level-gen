using System;
using System.Collections.Generic;
using LostInLimbo.LevelGen.Api;
using LostInLimbo.LevelGen.Data.Result.Graph;
using LostInLimbo.LevelGen.Internal.Generator.Graph.Processors;
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
/// <item><c>CnfGraph.Connectivity</c> is the number of relationships above the minimum
/// (the spanning structure). That allows multiple passages from the previous node to the next node
/// and/or shortcuts (a passage that skips one or more nodes in the chain - must respect required
/// obstacles).
/// The builder can ignore excess Connectivity if it cannot make more relationships.</item>
/// <item><c>CnfGraph.Complexity</c> slightly indirectly determines the number of obstacles.
/// Like Connectivity, it is a soft constraint.</item>
/// </list>
/// <para>Even this simple shape allows different scenarios:</para>
/// <list type="bullet">
/// <item>Every node holding a solution for the conditional passage to the next node.</item>
/// <item>Some passages are open, others require a solution that may be found earlier.</item>
/// <item>Every passage but the last one is unconditional with the last one requiring all solutions.</item>
/// </list>
/// <para>The graph is built in five passes:</para>
/// <list type="bullet">
/// <item>Create a linearly connected skeleton.</item>
/// <item>Add POIs. Start and exit are at the ends of the linear graph.</item>
/// <item>Extra relationships between nodes up to connectivity, <see cref="MaxExtraDegree"/>
/// and the node <see cref="LilNode.MaxEdges"/> hard cap.</item>
/// <item>Create obstacles for relationships up to complexity and
/// <see cref="MaxConditionPerRel"/>: one shared solution condition per chosen node's
/// incoming relationships, solution most likely near its guarded passage within
/// <see cref="MaxConditionDistance"/>.</item>
/// <item>Finalization: secret nodes, objective POIs, node relative size and grouping.</item>
/// </list>
/// </remarks>
internal sealed class GraphBuilderLinear : AGraphBuilder
{
  /// <summary>
  /// Maximum number of relationships a node may have, counting skeleton and extra
  /// relationships together. Assigned to every node as its <see cref="LilNode.MaxEdges"/>
  /// hard cap and enforced by the extra-relationships pass.
  /// </summary>
  internal const int MaxEdges = 5;
  /// <summary>
  /// Maximum number of extra relationships a node may gain in the extra-relationships phase (inclusive).
  /// Important: we count in and out relationships separately.
  /// </summary>
  internal const int MaxExtraDegree = 2;
  /// <summary>Maximum number of conditions that a single relationship can have.</summary>
  internal const int MaxConditionPerRel = 3;
  /// <summary>Maximum distance between the solution and the guarded node's chain predecessor (so at most MaxConditionDistance + 1 from the guarded node).</summary>
  internal const int MaxConditionDistance = 3;
  /// <summary>Maximum number of nodes that can be skipped by an extra relationship.</summary>
  internal const int MaxSkipDistance = 2;

  /// <summary>
  /// Initial relative size of every node; replaced by the <see cref="ProcNodeRelSize"/>
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
  /// <summary>Salt for solution POI identifiers; one sub-channel per obstacle.</summary>
  private const long SaltSolutionPoi = 0x736F6C7574696F6E; // "solution"

  /// <inheritdoc/>
  protected override LilResult Build()
  {
    var size = Config.Graph.Size;
    var connectivity = Config.Graph.Connectivity;
    var complexity = Config.Graph.Complexity;
    var degrees = new int[size];
    var extraOutDegrees = new int[size];
    var extraInDegrees = new int[size];
    var extraPairs = new bool[size, size];

    var nodeIds = CreateNodes(size);
    CreateSkeleton(size, nodeIds, degrees);
    PlacePois();
    AddExtraRelationships(connectivity, nodeIds, degrees, extraOutDegrees, extraInDegrees, extraPairs);
    AddObstacles(complexity, nodeIds);

    // Finalization.
    var procOpts = Config.Graph.ProcOpts;
    new ProcRelSecrets(Graph, procOpts.RelSecrets, PrepData.SeedGraph).Execute();
    new ProcNodeObjectives(Graph, procOpts.NodeObjectives, PrepData.SeedGraph).Execute();
    new ProcNodeRelSize(Graph, procOpts.NodeRelSize, PrepData.SeedGraph).Execute();
    new ProcNodeStyle(Graph, procOpts.NodeStyle, PrepData.SeedGraph).Execute();

    // Soft constraints only; there is no configuration this builder cannot satisfy.
    return LilResult.Succeed();
  }

  //
  // Skeleton pass.
  //

  /// <summary>
  /// Creates all nodes in chain order with deterministic identifiers.
  /// </summary>
  /// <param name="size">Number of nodes to create.</param>
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
      Graph.Nodes.Add(new LilNode { 
        Id = id,
        RelSize = NodeRelSize,
        MaxEdges = MaxEdges,
      });
    }

    return nodeIds;
  }

  /// <summary>
  /// Creates the linear skeleton: every node links to its next node, from the first up to the
  /// last one. Uses exactly <c>size - 1</c> relationships - the minimum for a connected graph.
  /// </summary>
  /// <param name="size">Total number of nodes, from <c>CnfGraph.Size</c>.</param>
  /// <param name="nodeIds">Node identifiers in chain order.</param>
  /// <param name="degrees">Per-node relationship count, updated in place.</param>
  private void CreateSkeleton(int size, Guid[] nodeIds, int[] degrees)
  {
    for (var i = 0; i + 1 < size; i++)
      AddEdge(i, i + 1, nodeIds, degrees, SkeletonPassage);
  }

  //
  // POI pass.
  //

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

  //
  // Extra relationship pass.
  //

  /// <summary>
  /// Adds extra relationships on top of the skeleton until connectivity is spent or no
  /// eligible node pair remains under the constraints of <see cref="MaxExtraDegree"/>,
  /// <see cref="MaxSkipDistance"/> and the <see cref="MaxEdges"/> hard cap.
  /// </summary>
  /// <remarks>
  /// <para>One relationship is added per iteration:</para>
  /// <list type="number">
  /// <item>The source (node A) is picked uniformly among all nodes that are not the last node,
  /// still have out-capacity under <see cref="MaxExtraDegree"/>, still have total capacity
  /// under <see cref="MaxEdges"/> and can actually place a relationship forward - so a source
  /// without any eligible target never blocks the pass.</item>
  /// <item>The target (node B) is picked among nodes after A within <see cref="MaxSkipDistance"/>
  /// skipped nodes, with in-capacity under <see cref="MaxExtraDegree"/>, total capacity under
  /// <see cref="MaxEdges"/> and no extra relationship yet on the (A, B) pair. Targets are
  /// weighted linearly by remaining skips (adjacent most likely, see <see cref="PickTarget"/>).</item>
  /// </list>
  /// <para>When no eligible source (and therefore no eligible pair) remains, the pass ends
  /// silently - connectivity is a soft constraint. At most one extra relationship per node
  /// pair, so an adjacent pair holds at most two passages (skeleton plus extra) and a
  /// skipping pair at most one. No self-loops by construction.</para>
  /// </remarks>
  /// <param name="connectivity">Requested number of extra relationships, from <c>CnfGraph.Connectivity</c>.</param>
  /// <param name="nodeIds">Node identifiers in chain order.</param>
  /// <param name="degrees">Total relationships per node (skeleton plus extra), updated in place.</param>
  /// <param name="extraOutDegrees">Extra relationships per source node, updated in place.</param>
  /// <param name="extraInDegrees">Extra relationships per target node, updated in place.</param>
  /// <param name="extraPairs">Flags pairs that already carry an extra relationship, updated in place (both directions).</param>
  private void AddExtraRelationships(
    int connectivity,
    Guid[] nodeIds,
    int[] degrees,
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
        if (degrees[from] >= MaxEdges) continue;
        if (HasEligibleTarget(from, degrees, extraInDegrees, extraPairs, size)) sources.Add(from);
      }

      // No source with an eligible target remains; excess connectivity is ignored.
      if (sources.Count == 0) break;

      var source = sources[Random.NextInt(sources.Count)];
      var target = PickTarget(source, degrees, extraInDegrees, extraPairs, size);

      AddEdge(source, target, nodeIds, degrees, ExtraPassage);
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
  /// <param name="degrees">Total relationships per node.</param>
  /// <param name="extraInDegrees">Extra relationships per target node.</param>
  /// <param name="extraPairs">Pairs that already carry an extra relationship.</param>
  /// <param name="size">Total number of nodes.</param>
  /// <returns>True when at least one target within the skip window is eligible.</returns>
  private bool HasEligibleTarget(int from, int[] degrees, int[] extraInDegrees, bool[,] extraPairs, int size)
  {
    var lastTarget = Math.Min(from + MaxSkipDistance + 1, size - 1);
    for (var to = from + 1; to <= lastTarget; to++)
    {
      if (extraPairs[from, to]) continue;
      if (extraInDegrees[to] >= MaxExtraDegree) continue;
      if (degrees[to] >= MaxEdges) continue;
      return true;
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
  /// <param name="degrees">Total relationships per node.</param>
  /// <param name="extraInDegrees">Extra relationships per target node.</param>
  /// <param name="extraPairs">Pairs that already carry an extra relationship.</param>
  /// <param name="size">Total number of nodes.</param>
  /// <returns>Index of the chosen target node.</returns>
  /// <exception cref="InvalidOperationException">No eligible target remains; unreachable while
  /// <see cref="HasEligibleTarget"/> agreed that one exists.</exception>
  private int PickTarget(int from, int[] degrees, int[] extraInDegrees, bool[,] extraPairs, int size)
  {
    var lastTarget = Math.Min(from + MaxSkipDistance + 1, size - 1);
    var totalWeight = 0;
    for (var to = from + 1; to <= lastTarget; to++)
    {
      if (extraPairs[from, to]) continue;
      if (extraInDegrees[to] >= MaxExtraDegree) continue;
      if (degrees[to] >= MaxEdges) continue;
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
      if (degrees[to] >= MaxEdges) continue;
      roll -= MaxSkipDistance + 1 - (to - from - 1);
      if (roll < 0) return to;
    }

    throw new InvalidOperationException("Extra relationship weight roll landed outside the candidate list.");
  }

  /// <summary>
  /// Adds one bidirectional edge between the two nodes in chain order and updates the
  /// degree bookkeeping. It is always a chokepoint.
  /// </summary>
  /// <param name="from">Index of the source node; must be earlier in the chain than <paramref name="to"/>.</param>
  /// <param name="to">Index of the target node; must differ from <paramref name="from"/>.</param>
  /// <param name="nodeIds">Node identifiers in chain order.</param>
  /// <param name="degrees">Per-node relationship count, updated in place.</param>
  /// <param name="passage">Passage ordinal for the node pair; keeps parallel passages unique.</param>
  private void AddEdge(int from, int to, Guid[] nodeIds, int[] degrees, int passage)
  {
    degrees[from]++;
    degrees[to]++;

    Graph.Edges.Add(new LilEdge
    {
      Id = DeriveEdgeId(from, to, passage),
      From = nodeIds[from],
      To = nodeIds[to],
      Chokepoint = true,
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
    var pairAndPassage = (uint)low | ((long)(uint)high << 8) | ((long)(uint)passage << 16);
    var subSeed = SeedDerivation.Derive(PrepData.SeedGraph, SaltEdge, pairAndPassage);
    return IdDerivation.Derive(subSeed, SaltEdge);
  }

  //
  // Obstacle pass.
  //

  /// <summary>
  /// Obstacle pass: guards node entries with solution-locked passages until complexity is
  /// spent or every relationship is saturated.
  /// </summary>
  /// <remarks>
  /// <para>One obstacle per iteration, up to <paramref name="complexity"/> iterations:</para>
  /// <list type="number">
  /// <item>Among all nodes but the first that still have capacity (see
  /// <see cref="FindObstacleTargets"/>), one target is picked uniformly.</item>
  /// <item>A new solution POI is created and placed by <see cref="PickSolutionNode"/>, so it
  /// is most likely right before the guarded passage and falls off with distance up to
  /// <see cref="MaxConditionDistance"/>.</item>
  /// <item>Every relationship going into the target node gains the same new condition
  /// (<see cref="EnConditionType.RequiresSolution"/> pointing at that POI).</item>
  /// </list>
  /// <para>Conditions only ever enter a node's relationships all at once, so all of them
  /// stay identical and saturate together at <see cref="MaxConditionPerRel"/>. Once no
  /// target with capacity remains, excess complexity is discarded silently - complexity is a
  /// soft constraint, just like connectivity. Guaranteed solvable: a solution always lands
  /// at or before the chain predecessor of its guarded node, so the chain reaches it before
  /// any guarded passage is needed.</para>
  /// </remarks>
  /// <param name="complexity">Requested number of obstacles, from <c>CnfGraph.Complexity</c>.</param>
  /// <param name="nodeIds">Node identifiers in chain order.</param>
  private void AddObstacles(int complexity, Guid[] nodeIds)
  {
    var size = nodeIds.Length;
    var added = 0;
    while (added < complexity)
    {
      var targets = FindObstacleTargets(size, nodeIds);
      if (targets.Count == 0) break;

      var target = targets[Random.NextInt(targets.Count)];

      // One new solution per obstacle, placed at or before the guarded node's chain predecessor.
      var solutionPoi = new LilPoi
      {
        Id = DeriveSolutionId(added),
        Type = EnPoiType.Solution,
      };
      Graph.Nodes[PickSolutionNode(target - 1)].Pois.Add(solutionPoi);

      // Every relationship into the target gets the same condition.
      var condition = new LilCondition
      {
        Type = EnConditionType.RequiresSolution,
        RequiresPoi = solutionPoi.Id,
      };
      foreach (var edge in Graph.Edges)
      {
        if (edge.To == nodeIds[target]) edge.Conditions.Add(condition);
      }

      added++;
    }
  }

  /// <summary>
  /// Collects all nodes but the first whose relationships all still have room for one more
  /// condition under <see cref="MaxConditionPerRel"/>.
  /// </summary>
  /// <remarks>Because <see cref="AddObstacles"/> always conditions a node's relationships
  /// together, they saturate together - a node either has full capacity on all of them or on
  /// none, and the check never splits a set of parallel passages.</remarks>
  /// <param name="size">Total number of nodes.</param>
  /// <param name="nodeIds">Node identifiers in chain order.</param>
  /// <returns>Target node indices in ascending chain order; empty when everything is saturated.</returns>
  private List<int> FindObstacleTargets(int size, Guid[] nodeIds)
  {
    var targets = new List<int>(size - 1);
    for (var target = 1; target < size; target++)
    {
      var eligible = true;
      foreach (var edge in Graph.Edges)
      {
        if (edge.To != nodeIds[target]) continue;
        if (edge.Conditions.Count >= MaxConditionPerRel)
        {
          eligible = false;
          break;
        }
      }

      if (eligible) targets.Add(target);
    }

    return targets;
  }

  /// <summary>
  /// Picks the node holding the solution for a passage guarded at the given source node,
  /// weighted linearly by distance: the source node itself counts
  /// <see cref="MaxConditionDistance"/> + 1, each node further back one less, so the node
  /// facing the guarded passage is the most likely and likelihood falls with distance.
  /// </summary>
  /// <remarks>Deterministic for the same generator state; candidates are enumerated backwards
  /// in chain order. The walk stops at the first node, so small chains simply lose the far
  /// candidates.</remarks>
  /// <param name="nearestSource">Index of the guarded passage's chain predecessor.</param>
  /// <returns>Index of the chosen solution node.</returns>
  private int PickSolutionNode(int nearestSource)
  {
    var totalWeight = 0;
    for (var distance = 0; nearestSource - distance >= 0 && distance <= MaxConditionDistance; distance++)
      totalWeight += MaxConditionDistance + 1 - distance;

    // Roll once on the cumulative weights and walk the candidates to the picked one.
    var roll = Random.NextInt(totalWeight);
    for (var distance = 0; nearestSource - distance >= 0 && distance <= MaxConditionDistance; distance++)
    {
      roll -= MaxConditionDistance + 1 - distance;
      if (roll < 0) return nearestSource - distance;
    }

    throw new InvalidOperationException("Solution weight roll landed outside the candidate list.");
  }

  /// <summary>
  /// Derives a solution POI identifier unique per obstacle, from the graph sub-seed and the
  /// obstacle index as sub-channel.
  /// </summary>
  /// <param name="obstacleIndex">Zero-based obstacle ordinal within the pass.</param>
  /// <returns>Identifier unique for the run. Not <see cref="Guid.Empty"/>.</returns>
  private Guid DeriveSolutionId(int obstacleIndex)
  {
    var subSeed = SeedDerivation.Derive(PrepData.SeedGraph, SaltSolutionPoi, obstacleIndex);
    return IdDerivation.Derive(subSeed, SaltSolutionPoi);
  }
}
