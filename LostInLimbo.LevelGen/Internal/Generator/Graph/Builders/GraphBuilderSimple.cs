using System;
using System.Collections.Generic;
using LostInLimbo.LevelGen.Data.Result.Graph;
using LostInLimbo.LevelGen.Internal.Generator.Graph.Processors;
using LostInLimbo.LevelGen.Internal.Utils;

namespace LostInLimbo.LevelGen.Internal.Generator.Graph.Builders;

/// <summary>
/// <para>
/// Builds the simple level graph: one or more nodes without obstacles. Start and exit may or
/// may not be in the same node. Skeleton relationships may not exceed
/// <see cref="MaxDegreeStar"/> per node; connectivity-driven relationships on top of the
/// skeleton may not exceed <see cref="MaxDegree"/> per node.
/// </para>
/// </summary>
/// <remarks>
/// <para>Rules:</para>
/// <list type="bullet">
/// <item>Node count follows <c>CnfGraph.Size</c>.</item>
/// <item><c>CnfGraph.Connectivity</c> is the amount of relationships above the minimum
/// (the spanning structure). Builder can ignore excess Connectivity if it cannot make more
/// relationships.</item>
/// <item><c>CnfGraph.Complexity</c> is ignored (no obstacles, conditions always empty).</item>
/// </list>
/// <para>The graph is built in four passes:</para>
/// <list type="bullet">
/// <item>Create minimal connected skeleton - a randomly chosen scenario between linear, star and mix.</item>
/// <item>Add PoIs. Start and exit are random and same node is allowed.</item>
/// <item>Extra relationships between random nodes up to connectivity and <see cref="MaxDegree"/>.</item>
/// <item>Finalisation: secret nodes, node relative size and grouping.</item>
/// </list>
/// </remarks>
internal sealed class GraphBuilderSimple : AGraphBuilder
{
  /// <summary>Maximum amount of relationships a node may gain in the extra-relationships phase (inclusive).</summary>
  internal const int MaxDegree = 4;
  /// <summary>
  /// Maximum amount of relationships a node may gain in the skeleton phase (inclusive). A
  /// central node that reaches this cap hands its remaining nodes over to a new central.
  /// </summary>
  internal const int MaxDegreeStar = 7;
  /// <summary>Minimum size where scenarios differ.</summary>
  private const int ScenarioSize = 3;

  /// <summary>
  /// Initial relative size of every node; replaced by the <see cref="ProcNodeRelSize"/>
  /// pass once the whole graph (relationships and POIs included) is known.
  /// </summary>
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
  /// algorithm: create a linear prefix of nodes, then attach the rest to a central node,
  /// handing over to a new central when the current one reaches <see cref="MaxDegreeStar"/>.
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
  protected override bool Build()
  {
    var size = Config.Graph.Size;
    var connectivity = Config.Graph.Connectivity;
    var degrees = new int[size];
    var adjacency = new bool[size, size];

    var nodeIds = CreateNodes(size);
    CreateSkeleton(size, nodeIds, degrees, adjacency);
    PlacePois();
    AddExtraRelationships(connectivity, degrees, adjacency, nodeIds);

    // Finalisation.
    new ProcRelSecrets(Graph, ProcRelSecretsOpt.Default, PrepData.SeedGraph).Execute();
    new ProcNodeRelSize(Graph, ProcNodeRelSizeOpt.Default, PrepData.SeedGraph).Execute();
    new ProcNodeStyle(Graph, ProcNodeStyleOpt.Default, PrepData.SeedGraph).Execute();

    // Soft constraints only; there is no configuration this builder cannot satisfy.
    return true;
  }

  //
  // Skeleton pass.
  //

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
  /// every remaining node attached to the current central node. When a central node reaches
  /// <see cref="MaxDegreeStar"/>, a new central takes over so no node exceeds the skeleton
  /// cap. Uses exactly <c>size - 1</c> relationships - the minimum for a connected graph.
  /// </summary>
  /// <param name="size">Total amount of nodes, from <c>CnfGraph.Size</c>.</param>
  /// <param name="nodeIds">Node identifiers in index order.</param>
  /// <param name="degrees">Per-node relationship count, updated in place.</param>
  /// <param name="adjacency">Per-node-pair adjacency flags, updated in place.</param>
  private void CreateSkeleton(
    int size,
    Guid[] nodeIds,
    int[] degrees,
    bool[,] adjacency)
  {
    var scenario = PickScenario(size);
    var linearCount = scenario switch
    {
      EnSkeletonScenario.Star => 1,
      EnSkeletonScenario.Mixed => Random.NextInt(2, size), // [2, size - 1]; size >= 3 checked in PickScenario
      _ => size,
    };

    // Chain over the linear prefix.
    for (var i = 0; i + 1 < linearCount; i++)
      AddEdge(i, i + 1, nodeIds, degrees, adjacency);

    // Attach every remaining node to the central node; hand over to a new central as soon
    // as the current one is full, so the skeleton never exceeds its degree cap.
    if (linearCount < size)
    {
      var central = PickCentral(linearCount);
      for (var i = linearCount; i < size; i++)
      {
        if (degrees[central] >= MaxDegreeStar) central = PickNextCentral(degrees);
        AddEdge(central, i, nodeIds, degrees, adjacency);
      }
    }
  }

  /// <summary>
  /// Rolls the skeleton scenario with equal chance each. Mixed degenerates to a chain when
  /// there are too few nodes to tell the scenarios apart.
  /// </summary>
  /// <param name="size">Total amount of nodes, from <c>CnfGraph.Size</c>.</param>
  /// <returns>The scenario to build the skeleton from. Deterministic for the same generator state.</returns>
  private EnSkeletonScenario PickScenario(int size)
  {
    if (size < ScenarioSize) return EnSkeletonScenario.Linear;
    var roll = Random.NextInt(3);
    return (EnSkeletonScenario)roll;
  }

  /// <summary>
  /// Picks the initial node of the linear prefix that the remaining nodes attach to.
  /// </summary>
  /// <param name="linearCount">Length of the linear prefix; at least 1.</param>
  /// <returns>
  /// Any node of the prefix, chosen uniformly - including the first and the last node;
  /// the only node when the prefix is a single node.
  /// </returns>
  private int PickCentral(int linearCount)
  {
    return Random.NextInt(linearCount);
  }

  /// <summary>
  /// Picks a replacement central node once the previous one reached
  /// <see cref="MaxDegreeStar"/>. Only already attached nodes (degree at least one) below
  /// the cap are eligible - an unattached node would split the skeleton into components.
  /// </summary>
  /// <remarks>
  /// Candidates are enumerated in index order, so the choice is deterministic for the same
  /// generator state. The eligible set is never empty while unattached nodes remain: the
  /// attached part is a tree with n nodes, its degree sum is 2(n - 1), and that sum can
  /// never cover all n nodes at a cap of at least two.
  /// </remarks>
  /// <param name="degrees">Per-node relationship count.</param>
  /// <returns>Index of the chosen central node.</returns>
  /// <exception cref="InvalidOperationException">
  /// No eligible node remains; unreachable as long as <see cref="MaxDegreeStar"/> is at least two.
  /// </exception>
  private int PickNextCentral(int[] degrees)
  {
    // Find all eligible nodes.
    var eligible = new List<int>();
    for (var i = 0; i < degrees.Length; i++)
    {
      if (degrees[i] >= 1 && degrees[i] < MaxDegreeStar) eligible.Add(i);
    }

    if (eligible.Count == 0)
      throw new InvalidOperationException("Skeleton central reached its cap but no eligible node remains.");

    // Pick new central node from eligible nodes.
    return eligible[Random.NextInt(eligible.Count)];
  }

  //
  // PoI pass.
  //

  /// <summary>
  /// Places the start and exit POIs into two random nodes; both may be the same node.
  /// </summary>
  private void PlacePois()
  {
    var startIndex = Random.NextInt(Graph.Nodes.Count);
    var exitIndex = Random.NextInt(Graph.Nodes.Count);

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

  //
  // Extra relationship pass.
  //

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
  /// <param name="connectivity">Requested amount of extra relationships, from <c>CnfGraph.Connectivity</c>.</param>
  /// <param name="degrees">Per-node relationship count, updated in place.</param>
  /// <param name="adjacency">Per-node-pair adjacency flags, updated in place.</param>
  /// <param name="nodeIds">Node identifiers in index order.</param>
  private void AddExtraRelationships(
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

    Random.Shuffle(candidates);

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
      Chokepoint = Random.NextBool(0.5f),
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
    // Pack pair into one sub-channel.
    var subSeed = SeedDerivation.Derive(PrepData.SeedGraph, SaltEdge, ((long)low << 32) | (uint)high);
    return IdDerivation.Derive(subSeed, SaltEdge);
  }
}
