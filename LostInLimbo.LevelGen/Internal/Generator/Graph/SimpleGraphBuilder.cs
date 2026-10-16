using System;
using System.Collections.Generic;
using LostInLimbo.LevelGen.Data.Result.Graph;
using LostInLimbo.LevelGen.Internal.Utils;

namespace LostInLimbo.LevelGen.Internal.Generator.Graph;

/// <summary>
/// <para>
/// Builds the simple level graph: one or more nodes without obstacles. Start and exit may or
/// may not be in the same node. Connectivity-driven relationships may not exceed
/// <see cref="MaxDegree"/> per node; the minimal skeleton itself is exempt (a large star has
/// one central node with many relationships).
/// </para>
/// </summary>
/// <remarks>
/// <para>Rules:</para>
/// <list type="bullet">
/// <item>Node count follows <c>CnfGraph.Size</c>.</item>
/// <item><c>CnfGraph.Connectivity</c> is the amount of relationships above the minimum
/// (the spanning structure); it is stretched down silently if the degree cap makes the full
/// amount unreachable.</item>
/// <item>Complexity is ignored (no obstacles, conditions always empty).</item>
/// </list>
/// <para>
/// All configuration values are soft constraints: the builder never fails, it only shrinks
/// what it cannot satisfy (see <see cref="AGraphBuilder"/>).
/// </para>
/// <para>
/// The graph is built in three passes: (1) a minimal connected skeleton - a randomly chosen
/// scenario that in fact comes from one unified algorithm parameterized by the length of a
/// linear prefix; (2) extra relationships between random nodes up to connectivity and the
/// degree cap; (3) start and exit POIs in two random nodes (the same node is allowed).
/// Output is fully deterministic for the same <c>PrepData.SeedGraph</c> and config.
/// </para>
/// </remarks>
internal sealed class SimpleGraphBuilder : AGraphBuilder
{
  /// <summary>Maximum amount of relationships connectivity may add to a node (inclusive).</summary>
  private const int MaxDegree = 4;
  /// <summary>Minimum size where scenarios differ.</summary>
  private const int ScenarioSize = 3;

  /// <summary>Relative size of every node; baseline set by <c>OneNodeGraphBuilder</c>.</summary>
  private const int NodeRelSize = 1;

  /// <summary>Salt for node identifiers.</summary>
  private const long SaltNode = 0x6E6F6465; // "node"

  /// <summary>Salt for edge identifiers.</summary>
  private const long SaltEdge = 0x65646765; // "edge"

  /// <summary>Salt for the start POI identifier.</summary>
  private const long SaltStartPoi = 0x7374617274; // "start"

  /// <summary>Salt for the exit POI identifier.</summary>
  private const long SaltExitPoi = 0x65786974; // "exit"

  /// <summary>
  /// Skeleton scenario for the minimal spanning structure. All three come from the same
  /// algorithm: create a linear prefix of nodes, then attach the rest to a central node.
  /// </summary>
  private enum EnSkeletonScenario
  {
    /// <summary>Linear prefix covers every node - a plain chain.</summary>
    Linear,

    /// <summary>Linear prefix is a single node - a star around that node.</summary>
    Star,

    /// <summary>Linear prefix is a part of the graph - a chain with a star hanging off it.</summary>
    Mixed,
  }

  /// <inheritdoc/>
  public override bool Build()
  {
    var size = Config.Graph.Size;
    var connectivity = Config.Graph.Connectivity;
    var random = new SeededRandom(PrepData.SeedGraph);

    var nodeIds = CreateNodes(size);
    var degrees = new int[size];
    var adjacency = new bool[size, size];

    CreateSkeleton(random, size, nodeIds, degrees, adjacency);
    AddExtraRelationships(random, connectivity, degrees, adjacency, nodeIds);
    PlacePois(random);

    // Soft constraints only; there is no configuration this builder cannot satisfy.
    return true;
  }

  /// <summary>
  /// Creates all nodes in index order with deterministic identifiers.
  /// </summary>
  /// <param name="size">Amount of nodes to create.</param>
  /// <returns>Node identifiers in index order.</returns>
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
  /// Creates the minimal connected skeleton: a chain over a linear prefix of nodes, with
  /// every remaining node attached to one central node of the prefix. Uses exactly
  /// <c>size - 1</c> relationships - the minimum for a connected graph.
  /// </summary>
  /// <param name="random">Seeded generator driving scenario selection.</param>
  /// <param name="size">Total amount of nodes, from <c>CnfGraph.Size</c>.</param>
  /// <param name="nodeIds">Node identifiers in index order.</param>
  /// <param name="degrees">Per-node relationship count, updated in place.</param>
  /// <param name="adjacency">Per-node-pair adjacency flags, updated in place.</param>
  private void CreateSkeleton(
    SeededRandom random,
    int size,
    Guid[] nodeIds,
    int[] degrees,
    bool[,] adjacency)
  {
    var scenario = PickScenario(random, size);
    var linearCount = scenario switch
    {
      EnSkeletonScenario.Star => 1,
      EnSkeletonScenario.Mixed => random.NextInt(2, size), // [2, size - 1]; size >= 3 checked in PickScenario
      _ => size,
    };

    // Chain over the linear prefix.
    for (var i = 0; i + 1 < linearCount; i++)
      AddEdge(i, i + 1, nodeIds, degrees, adjacency);

    // Attach every remaining node to the central node of the prefix.
    if (linearCount < size)
    {
      var central = PickCentral(random, linearCount);
      for (var i = linearCount; i < size; i++)
        AddEdge(central, i, nodeIds, degrees, adjacency);
    }
  }

  /// <summary>
  /// Rolls the skeleton scenario with equal chance each. Mixed degenerates to a chain when
  /// there are too few nodes to tell the scenarios apart.
  /// </summary>
  /// <param name="random">Seeded generator providing the roll.</param>
  /// <param name="size">Total amount of nodes, from <c>CnfGraph.Size</c>.</param>
  /// <returns>The scenario to build the skeleton from. Deterministic for the same generator state.</returns>
  private static EnSkeletonScenario PickScenario(SeededRandom random, int size)
  {
    if (size < ScenarioSize) return EnSkeletonScenario.Linear;
    var roll = random.NextInt(3);
    return (EnSkeletonScenario)roll;
  }

  /// <summary>
  /// Picks the node of the linear prefix that the remaining nodes attach to.
  /// </summary>
  /// <param name="random">Seeded generator.</param>
  /// <param name="linearCount">Length of the linear prefix; at least 1.</param>
  /// <returns>
  /// An interior node when the prefix has one, otherwise a random end of the prefix
  /// (the only node when the prefix is a single node).
  /// </returns>
  private static int PickCentral(SeededRandom random, int linearCount)
  {
    if (linearCount >= 3)
      return random.NextInt(1, linearCount - 1); // Never first or last.
    if (linearCount == 2)
      return random.NextInt(2); // No interior node; random end.
    return 0;
  }

  /// <summary>
  /// Adds random extra relationships on top of the skeleton until connectivity is spent or
  /// every node pair is exhausted under the degree cap.
  /// </summary>
  /// <remarks>
  /// Candidate pairs are enumerated in index order and shuffled once, then consumed in
  /// order; pairs whose endpoints filled up in the meantime are skipped. Stops early
  /// silently when no eligible pair remains - connectivity is a soft constraint.
  /// No self-loops or duplicate pairs can occur by construction.
  /// </remarks>
  /// <param name="random">Seeded generator driving the candidate shuffle.</param>
  /// <param name="connectivity">Requested amount of extra relationships, from <c>CnfGraph.Connectivity</c>.</param>
  /// <param name="degrees">Per-node relationship count, updated in place.</param>
  /// <param name="adjacency">Per-node-pair adjacency flags, updated in place.</param>
  /// <param name="nodeIds">Node identifiers in index order.</param>
  private void AddExtraRelationships(
    SeededRandom random,
    int connectivity,
    int[] degrees,
    bool[,] adjacency,
    Guid[] nodeIds)
  {
    if (connectivity == 0) return;

    var candidates = new List<(int From, int To)>();
    var size = degrees.Length;
    for (var i = 0; i < size; i++)
    {
      for (var j = i + 1; j < size; j++)
      {
        if (!adjacency[i, j]) candidates.Add((i, j));
      }
    }

    random.Shuffle(candidates);

    var added = 0;
    foreach (var (from, to) in candidates)
    {
      if (added >= connectivity) break;
      if (degrees[from] >= MaxDegree || degrees[to] >= MaxDegree) continue;

      AddEdge(from, to, nodeIds, degrees, adjacency);
      added++;
    }
  }

  /// <summary>
  /// Places the start and exit POIs into two random nodes; both may be the same node.
  /// </summary>
  /// <param name="random">Seeded generator picking the node indices.</param>
  private void PlacePois(SeededRandom random)
  {
    var startIndex = random.NextInt(Graph.Nodes.Count);
    var exitIndex = random.NextInt(Graph.Nodes.Count);

    // Nodes were added in index order by CreateNodes, so index lookup is exact.
    Graph.Nodes[startIndex].Pois.Add(new LilPoi
    {
      Id = IdDerivation.Derive(PrepData.SeedGraph, SaltStartPoi),
      Type = EnPoiType.Start,
    });
    Graph.Nodes[exitIndex].Pois.Add(new LilPoi
    {
      Id = IdDerivation.Derive(PrepData.SeedGraph, SaltExitPoi),
      Type = EnPoiType.Exit,
    });
  }

  /// <summary>
  /// Adds one bidirectional, unconditional edge between the two nodes and updates the
  /// degree bookkeeping.
  /// </summary>
  /// <param name="from">Index of the source node.</param>
  /// <param name="to">Index of the target node; must differ from <paramref name="from"/>.</param>
  /// <param name="nodeIds">Node identifiers in index order.</param>
  /// <param name="degrees">Per-node relationship count, updated in place.</param>
  /// <param name="adjacency">Per-node-pair adjacency flags, updated in place.</param>
  private void AddEdge(int from, int to, Guid[] nodeIds, int[] degrees, bool[,] adjacency)
  {
    adjacency[from, to] = true;
    adjacency[to, from] = true;
    degrees[from]++;
    degrees[to]++;

    Graph.Edges.Add(new LilEdge
    {
      Id = DeriveEdgeId(from, to),
      From = nodeIds[from],
      To = nodeIds[to],
    });
  }

  /// <summary>
  /// Derives an edge identifier from the ordered endpoint indices, so it is unique per
  /// node pair regardless of the order edges are created in.
  /// </summary>
  /// <param name="fromIndex">Index of one endpoint node.</param>
  /// <param name="toIndex">Index of the other endpoint node.</param>
  /// <returns>Identifier unique per unordered node pair. Not <see cref="Guid.Empty"/>.</returns>
  private Guid DeriveEdgeId(int fromIndex, int toIndex)
  {
    var low = Math.Min(fromIndex, toIndex);
    var high = Math.Max(fromIndex, toIndex);
    var subSeed = SeedDerivation.Derive(PrepData.SeedGraph, SaltEdge, ((long)low << 32) | (uint)high);
    return IdDerivation.Derive(subSeed, SaltEdge);
  }
}
