using LostInLimbo.LevelGen.Data.Config.Graph;
using LostInLimbo.LevelGen.Data.Result.Graph;
using LostInLimbo.LevelGen.Internal.Generator.Graph.Processors;
using static LostInLimbo.LevelGen.Tests.Generator.Graph.GraphTestSupport;

namespace LostInLimbo.LevelGen.Tests.Generator.Graph.Styler;

/// <summary>
/// Tests <see cref="NodeStyler"/>: option validation, group naming and distribution,
/// contiguous growth with the restricted-relationship fallback and determinism of the
/// assignment.
/// </summary>
/// <remarks>
/// The styler works on a finished <see cref="LilGraph"/>, so most tests construct graphs
/// directly instead of going through a builder - that keeps topology exactly controlled
/// per test. Growth and fallback expectations are statistical: they average contiguity
/// over many seeds, because the exact outcome depends on the random seed split.
/// </remarks>
public sealed class NodeStylerTests
{
  /// <summary>Graph sub-seed used by all tests unless a test varies the seed itself.</summary>
  private const long Seed = GraphTestSupport.DefaultSeedGraph;

  /// <summary>Amount of seeds the statistical tests average over.</summary>
  private const int StatisticalSeedCount = 50;

  /// <summary>Tests for the hard constraints: nulls, out-of-range values and broken graphs.</summary>
  public sealed class ValidationTests
  {
    [Fact]
    public void Execute_WithNullGraph_ThrowsArgumentNullException()
    {
      // Act & Assert: A missing graph is rejected.
      Assert.Throws<ArgumentNullException>(() => NodeStyler.Execute(null!, NodeStylerOpt.Default, Seed));
    }

    [Fact]
    public void Execute_WithNullOptions_ThrowsArgumentNullException()
    {
      // Act & Assert: Missing options are rejected.
      Assert.Throws<ArgumentNullException>(() => NodeStyler.Execute(CreateChainGraph(4), null!, Seed));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Execute_WithGroupCountBelowOne_ThrowsArgumentOutOfRangeException(int groupCount)
    {
      // Arrange: Options demanding a forbidden group count.
      var options = NodeStylerOpt.Default with { GroupCount = groupCount };

      // Act & Assert: The styler rejects the options before touching the graph.
      var exception = Assert.Throws<ArgumentOutOfRangeException>(
        () => NodeStyler.Execute(CreateChainGraph(4), options, Seed));
      Assert.Equal("GroupCount", exception.ParamName);
    }

    [Fact]
    public void Execute_WithInvalidGroupCountOnEmptyGraph_ThrowsArgumentOutOfRangeException()
    {
      // Arrange: Empty graph; validation must still run.
      var options = NodeStylerOpt.Default with { GroupCount = 0 };

      // Act & Assert: Bad options never pass silently, even without nodes.
      var exception = Assert.Throws<ArgumentOutOfRangeException>(
        () => NodeStyler.Execute(new LilGraph(), options, Seed));
      Assert.Equal("GroupCount", exception.ParamName);
    }

    [Fact]
    public void Execute_WithEdgeToUnknownNode_ThrowsInvalidOperationException()
    {
      // Arrange: Chain whose extra edge points at a node that does not exist.
      var graph = CreateChainGraph(4);
      graph.Edges.Add(CreateEdge(3, 77));

      // Act & Assert: The dangling reference is reported.
      var exception = Assert.Throws<InvalidOperationException>(
        () => NodeStyler.Execute(graph, NodeStylerOpt.Default, Seed));
      Assert.Contains("unknown node", exception.Message);
    }

    [Fact]
    public void Execute_WithDuplicateNodeIds_ThrowsArgumentException()
    {
      // Arrange: Two nodes sharing one identifier.
      var graph = CreateChainGraph(4);
      graph.Nodes.Add(graph.Nodes[0] with { });

      // Act & Assert: The duplicate identifier is rejected.
      Assert.Throws<ArgumentException>(() => NodeStyler.Execute(graph, NodeStylerOpt.Default, Seed));
    }

    [Fact]
    public void Execute_WithEmptyGraph_LeavesGraphEmpty()
    {
      // Arrange: Graph without nodes.
      var graph = new LilGraph();

      // Act & Assert: Styling an empty graph is a no-op, not an error.
      NodeStyler.Execute(graph, NodeStylerOpt.Default, Seed);
      Assert.Empty(graph.Nodes);
    }
  }

  /// <summary>Tests for the group count, naming scheme and size distribution.</summary>
  public sealed class DistributionTests
  {
    [Fact]
    public void Execute_AssignsGroupToEveryNode()
    {
      // Arrange: Connected chain and default options.
      var graph = CreateChainGraph(10);

      // Act: Style the graph.
      NodeStyler.Execute(graph, NodeStylerOpt.Default, Seed);

      // Assert: No node is left without a group.
      Assert.All(graph.Nodes, node => Assert.NotNull(node.Style.Group));
    }

    [Fact]
    public void Execute_WithMoreGroupsThanNodes_DropsExcessGroups()
    {
      // Arrange: Two nodes, three requested groups - the soft target must drop one.
      var graph = CreateChainGraph(2);
      var options = NodeStylerOpt.Default with { GroupCount = 3 };

      // Act: Style the graph.
      NodeStyler.Execute(graph, options, Seed);

      // Assert: One group per node, named with the two-group width.
      var groups = graph.Nodes.Select(node => node.Style.Group).OrderBy(name => name).ToList();
      Assert.Equal(new[] { "g1", "g2" }, groups);
    }

    [Fact]
    public void Execute_WithSingleGroup_GroupsAllNodesTogether()
    {
      // Arrange: Chain with a single allowed group.
      var graph = CreateChainGraph(8);
      var options = NodeStylerOpt.Default with { GroupCount = 1 };

      // Act: Style the graph.
      NodeStyler.Execute(graph, options, Seed);

      // Assert: Every node carries the only group name.
      Assert.All(graph.Nodes, node => Assert.Equal("g1", node.Style.Group));
    }

    [Theory]
    [InlineData(10, 3, 3)]
    [InlineData(4, 3, 3)]
    [InlineData(3, 3, 3)]
    [InlineData(5, 10, 5)]
    [InlineData(12, 12, 12)]
    [InlineData(7, 1, 1)]
    public void Execute_UsesExactlyMinGroupCountDistinctGroups(int nodeCount, int groupCount, int expectedGroups)
    {
      // Arrange: Chain smaller or larger than the request.
      var graph = CreateChainGraph(nodeCount);
      var options = NodeStylerOpt.Default with { GroupCount = groupCount };

      // Act: Style the graph.
      NodeStyler.Execute(graph, options, Seed);

      // Assert: The group count is min(GroupCount, nodeCount); every group has a member.
      var counts = graph.Nodes
        .GroupBy(node => node.Style.Group)
        .ToDictionary(group => group.Key!, group => group.Count());
      Assert.Equal(expectedGroups, counts.Count);
      Assert.All(counts.Values, count => Assert.True(count >= 1));
    }

    [Fact]
    public void Execute_WithTwelveGroups_UsesZeroPaddedNames()
    {
      // Arrange: Exactly twelve nodes and twelve groups.
      var graph = CreateChainGraph(12);
      var options = NodeStylerOpt.Default with { GroupCount = 12 };

      // Act: Style the graph.
      NodeStyler.Execute(graph, options, Seed);

      // Assert: Names are padded to the width of the group count.
      var expected = Enumerable.Range(1, 12).Select(index => $"g{index:D2}").ToHashSet();
      var actual = graph.Nodes.Select(node => node.Style.Group!).ToHashSet();
      Assert.Equal(expected, actual);
    }

    [Fact]
    public void Execute_ProducesVariedGroupCountsAcrossSeeds()
    {
      // Arrange: Same structure, many seeds; the counts are a random split of the run.
      var distributions = new HashSet<string>();
      for (var seed = 0; seed < 40; seed++)
      {
        var graph = CreateChainGraph(12);

        // Act: Style with this seed.
        NodeStyler.Execute(graph, NodeStylerOpt.Default, seed);

        // Record the sorted group sizes for comparison.
        var sizes = graph.Nodes
          .GroupBy(node => node.Style.Group)
          .Select(group => group.Count())
          .OrderBy(size => size);
        distributions.Add(string.Join(",", sizes));
      }

      // Assert: The split is seed-driven rather than always an even three-way split.
      Assert.True(distributions.Count > 1, $"Expected varied splits, got: {string.Join(" | ", distributions)}");
    }

    [Fact]
    public void Build_WithSimpleBuilder_AssignsGroupsToEveryNode()
    {
      // Arrange & Act: Build a graph exactly like production dispatch does.
      var (success, graph) = GraphTestSupport.Build(EnGraphBuilder.Simple);

      // Assert: The builder's styling pass left no node ungrouped.
      Assert.True(success);
      Assert.All(graph.Nodes, node => Assert.NotNull(node.Style.Group));
    }
  }

  /// <summary>
  /// Tests that ordinary relationships grow groups contiguously before any node is
  /// dumped into a group randomly.
  /// </summary>
  public sealed class GrowthTests
  {
    [Fact]
    public void Execute_OnOpenChain_ProducesContiguousGroups()
    {
      // Arrange: Chain where ordinary growth can reach every node.
      var total = 0.0;
      for (var seed = 0; seed < StatisticalSeedCount; seed++)
      {
        var graph = CreateChainGraph(15);

        // Act: Style the graph with this seed.
        NodeStyler.Execute(graph, NodeStylerOpt.Default, seed);

        // Measure how much of each group forms its largest connected block.
        total += MeasureContiguity(graph);
      }

      // Assert: Groups stay overwhelmingly contiguous; random dumps only happen when
      // growth stalls, so the average must sit far above a random assignment.
      var average = total / StatisticalSeedCount;
      Assert.True(average >= 0.75, $"Average contiguity {average:F3} is below the expected 0.75.");
    }
  }

  /// <summary>
  /// Tests the fallback that unlocks secret, one-way and conditional relationships once
  /// every group ran out of ordinary ones.
  /// </summary>
  public sealed class FallbackTests
  {
    [Fact]
    public void Execute_OnRestrictedEdgeChain_AssignsGroupsToEveryNode()
    {
      // Arrange: Chain whose relationships cycle through secret, one-way and conditional.
      for (var seed = 0; seed < 20; seed++)
      {
        var graph = CreateRestrictedChainGraph(15);

        // Act: Style the graph; strict growth has nothing to work with.
        NodeStyler.Execute(graph, NodeStylerOpt.Default, seed);

        // Assert: Every node is grouped and the requested group count is reached.
        Assert.All(graph.Nodes, node => Assert.NotNull(node.Style.Group));
        Assert.Equal(3, graph.Nodes.Select(node => node.Style.Group).Distinct().Count());
      }
    }

    [Fact]
    public void Execute_OnSecretChain_ProducesContiguousGroups()
    {
      // Arrange: Chain whose every relationship is secret; only fallback growth can
      // continue a group along it, while a broken fallback would leave pure random dumps.
      var total = 0.0;
      for (var seed = 0; seed < StatisticalSeedCount; seed++)
      {
        var graph = CreateChainGraph(15, secretEdges: true);

        // Act: Style the graph with this seed.
        NodeStyler.Execute(graph, NodeStylerOpt.Default, seed);

        total += MeasureContiguity(graph);
      }

      // Assert: Contiguity far above a random dump proves the fallback growth ran.
      var average = total / StatisticalSeedCount;
      Assert.True(average >= 0.6, $"Average contiguity {average:F3} is below the expected 0.6.");
    }

    [Fact]
    public void Execute_OnOneWayChain_AssignsGroupsToEveryNode()
    {
      // Arrange: Chain of one-way relationships from lower to higher index; growth can
      // only ever move forward, so stalls and leftovers are expected.
      var graph = CreateChainGraph(15, oneWayEdges: true);

      // Act: Style the graph.
      NodeStyler.Execute(graph, NodeStylerOpt.Default, Seed);

      // Assert: Backwards growth being impossible does not leave nodes ungrouped.
      Assert.All(graph.Nodes, node => Assert.NotNull(node.Style.Group));
      Assert.Equal(3, graph.Nodes.Select(node => node.Style.Group).Distinct().Count());
    }
  }

  /// <summary>Tests for reproducibility and isolation of the assignment.</summary>
  public sealed class DeterminismTests
  {
    [Fact]
    public void Execute_WithSameSeed_ProducesIdenticalAssignments()
    {
      // Arrange: Two structurally identical graphs and the same options.
      var first = CreateRestrictedChainGraph(15);
      var second = CreateRestrictedChainGraph(15);

      // Act: Style both with the same seed.
      NodeStyler.Execute(first, NodeStylerOpt.Default, Seed);
      NodeStyler.Execute(second, NodeStylerOpt.Default, Seed);

      // Assert: Same identifiers with the same groups.
      GraphTestSupport.AssertEqualGraphs(first, second);
      Assert.Equal(GetGroupAssignments(first), GetGroupAssignments(second));
    }

    [Fact]
    public void Execute_WithDifferentSeed_ProducesDifferentAssignments()
    {
      // Arrange: Same structure, different seeds.
      var first = CreateChainGraph(30);
      var second = CreateChainGraph(30);

      // Act: Style both graphs.
      NodeStyler.Execute(first, NodeStylerOpt.Default, seedGraph: 1);
      NodeStyler.Execute(second, NodeStylerOpt.Default, seedGraph: 2);

      // Assert: The group sequences differ - counts, seeds and growth picks are seed-driven.
      Assert.NotEqual(GetGroupAssignments(first), GetGroupAssignments(second));
    }

    [Fact]
    public void Execute_IsIndependentOfInsertionOrder()
    {
      // Arrange: Same graph twice, but the node and edge lists of the second one reversed.
      var original = CreateRestrictedChainGraph(15);
      var reversed = CreateRestrictedChainGraph(15);
      reversed.Nodes.Reverse();
      reversed.Edges.Reverse();

      // Act: Style both with the same seed.
      NodeStyler.Execute(original, NodeStylerOpt.Default, Seed);
      NodeStyler.Execute(reversed, NodeStylerOpt.Default, Seed);

      // Assert: Every identifier keeps the group it got regardless of list order.
      Assert.Equal(GetGroupAssignments(original), GetGroupAssignments(reversed));
    }

    [Fact]
    public void Execute_ModifiesOnlyStyle()
    {
      // Arrange: Graph with known identifiers, sizes, edges and POIs, plus snapshots.
      var graph = CreateChainGraph(8, poiNodeIndex: 2, poiCount: 2);
      var nodeIdsBefore = graph.Nodes.Select(node => node.Id).ToList();
      var relSizesBefore = graph.Nodes.Select(node => node.RelSize).ToList();
      var edgesBefore = graph.Edges.Select(edge => (edge.Id, edge.From, edge.To)).ToList();
      var poisBefore = graph.Nodes
        .SelectMany(node => node.Pois)
        .Select(poi => (poi.Id, poi.Type))
        .ToList();

      // Act: Style the graph.
      NodeStyler.Execute(graph, NodeStylerOpt.Default, Seed);

      // Assert: Node order, sizes, edges and POIs are untouched; only groups were rewritten.
      Assert.Equal(nodeIdsBefore, graph.Nodes.Select(node => node.Id).ToList());
      Assert.Equal(relSizesBefore, graph.Nodes.Select(node => node.RelSize).ToList());
      Assert.Equal(edgesBefore, graph.Edges.Select(edge => (edge.Id, edge.From, edge.To)).ToList());
      Assert.Equal(poisBefore, graph.Nodes.SelectMany(node => node.Pois).Select(poi => (poi.Id, poi.Type)).ToList());
      Assert.All(graph.Nodes, node => Assert.NotNull(node.Style.Group));
    }

    [Fact]
    public void Execute_Twice_ProducesTheSameAssignment()
    {
      // Arrange: One graph styled once.
      var graph = CreateChainGraph(12);
      NodeStyler.Execute(graph, NodeStylerOpt.Default, Seed);
      var firstRun = GetGroupAssignments(graph);

      // Act: Style the very same graph again with the same inputs.
      NodeStyler.Execute(graph, NodeStylerOpt.Default, Seed);

      // Assert: Existing groups do not feed back into the draws, so the second run is a no-op.
      Assert.Equal(firstRun, GetGroupAssignments(graph));
    }

    [Fact]
    public void Execute_OverwritesExistingGroups()
    {
      // Arrange: Every node pre-styled with a foreign group.
      var graph = CreateChainGraph(6);
      for (var i = 0; i < graph.Nodes.Count; i++)
      {
        graph.Nodes[i] = graph.Nodes[i] with { Style = new LilStyle { Group = "old" } };
      }

      // Act: Style the graph.
      NodeStyler.Execute(graph, NodeStylerOpt.Default, Seed);

      // Assert: Stale groups are gone and the naming scheme took over.
      Assert.All(graph.Nodes, node => Assert.Matches("^g[0-9]+$", node.Style.Group));
      Assert.DoesNotContain(graph.Nodes, node => node.Style.Group == "old");
    }

    [Fact]
    public void Execute_OverManySeeds_IsReproducible()
    {
      for (var seed = 0; seed < 25; seed++)
      {
        // Arrange: Two structurally identical restricted chains.
        var first = CreateRestrictedChainGraph(12);
        var second = CreateRestrictedChainGraph(12);

        // Act: Style both with the same seed.
        NodeStyler.Execute(first, NodeStylerOpt.Default, seed);
        NodeStyler.Execute(second, NodeStylerOpt.Default, seed);

        // Assert: The restricted-relationship fallback stays deterministic too.
        GraphTestSupport.AssertEqualGraphs(first, second);
      }
    }
  }

  //
  // Graph fixtures
  //

  /// <summary>
  /// Creates a chain graph: node 0 - node 1 - ... with deterministic identifiers.
  /// </summary>
  /// <param name="nodeCount">Amount of nodes; at least 1.</param>
  /// <param name="secretEdges">When true, every relationship is a secret passage.</param>
  /// <param name="oneWayEdges">When true, every relationship only allows growth forward.</param>
  /// <param name="poiNodeIndex">Node that receives POIs, or -1 for none.</param>
  /// <param name="poiCount">Amount of POIs to place on <paramref name="poiNodeIndex"/>.</param>
  /// <returns>The constructed graph with <c>nodeCount - 1</c> edges.</returns>
  private static LilGraph CreateChainGraph(
    int nodeCount,
    bool secretEdges = false,
    bool oneWayEdges = false,
    int poiNodeIndex = -1,
    int poiCount = 0)
  {
    var graph = CreateNodes(poiNodeIndex, poiCount, nodeCount);
    for (var i = 0; i + 1 < nodeCount; i++)
    {
      graph.Edges.Add(CreateEdge(
        i,
        i + 1,
        direction: oneWayEdges ? EnPassageDirection.OneWay : EnPassageDirection.Bidirectional,
        secret: secretEdges));
    }

    return graph;
  }

  /// <summary>
  /// Creates a chain whose relationships cycle through the three restricted kinds:
  /// secret, one-way and conditional.
  /// </summary>
  /// <param name="nodeCount">Amount of nodes; at least 1.</param>
  /// <returns>The constructed graph with <c>nodeCount - 1</c> restricted edges.</returns>
  private static LilGraph CreateRestrictedChainGraph(int nodeCount)
  {
    var graph = CreateNodes(poiNodeIndex: -1, poiCount: 0, nodeCount: nodeCount);
    for (var i = 0; i + 1 < nodeCount; i++)
    {
      var kind = i % 3;
      graph.Edges.Add(kind switch
      {
        0 => CreateEdge(i, i + 1, secret: true),
        1 => CreateEdge(i, i + 1, direction: EnPassageDirection.OneWay),
        _ => CreateEdge(i, i + 1, conditions: [Condition(i)]),
      });
    }

    return graph;
  }

  /// <summary>
  /// Creates the node list with deterministic identifiers and initial sizes of 1.
  /// </summary>
  /// <param name="poiNodeIndex">Node that receives POIs, or -1 for none.</param>
  /// <param name="poiCount">Amount of POIs to place on that node.</param>
  /// <param name="nodeCount">Amount of nodes.</param>
  /// <returns>A graph holding the nodes; no edges.</returns>
  private static LilGraph CreateNodes(int poiNodeIndex, int poiCount, int nodeCount)
  {
    var graph = new LilGraph();
    for (var i = 0; i < nodeCount; i++)
    {
      var node = new LilNode { Id = Id(i), RelSize = 1 };
      if (i == poiNodeIndex)
      {
        for (var poi = 0; poi < poiCount; poi++)
        {
          node.Pois.Add(new LilPoi
          {
            Id = Guid.Parse($"00000000-0000-0000-0002-{i:D6}{poi:D6}"),
            Type = EnPoiType.Start,
          });
        }
      }

      graph.Nodes.Add(node);
    }

    return graph;
  }

  /// <summary>
  /// Creates an edge between two node indices with a deterministic identifier.
  /// </summary>
  /// <param name="fromIndex">Source node index.</param>
  /// <param name="toIndex">Target node index; must differ.</param>
  /// <param name="direction">Passage direction; open bidirectional unless set otherwise.</param>
  /// <param name="secret">Whether the passage is secret.</param>
  /// <param name="conditions">Passage requirements; open passage when null or empty.</param>
  /// <returns>The constructed edge.</returns>
  private static LilEdge CreateEdge(
    int fromIndex,
    int toIndex,
    EnPassageDirection direction = EnPassageDirection.Bidirectional,
    bool secret = false,
    LilCondition[]? conditions = null)
  {
    // Low/high ordering keeps identifiers unique per unordered pair.
    var low = Math.Min(fromIndex, toIndex);
    var high = Math.Max(fromIndex, toIndex);
    return new LilEdge
    {
      Id = Guid.Parse($"00000000-0000-0000-0001-{low:D6}{high:D6}"),
      From = Id(fromIndex),
      To = Id(toIndex),
      Direction = direction,
      Secret = secret,
      Conditions = conditions is null ? new List<LilCondition>() : [.. conditions],
    };
  }

  /// <summary>
  /// Measures how much of every group forms one connected block: the summed size of each
  /// group's largest component divided by the node count.
  /// </summary>
  /// <param name="graph">Styled graph to measure; every node must have a group.</param>
  /// <returns>Contiguity in <c>[0, 1]</c>; 1 means every group is fully connected.</returns>
  private static double MeasureContiguity(LilGraph graph)
  {
    // Build undirected neighbours; direction does not matter for structural contiguity.
    var neighbours = new List<int>[graph.Nodes.Count];
    var indexById = new Dictionary<Guid, int>();
    for (var i = 0; i < graph.Nodes.Count; i++)
    {
      neighbours[i] = new List<int>();
      indexById[graph.Nodes[i].Id] = i;
    }

    foreach (var edge in graph.Edges)
    {
      var from = indexById[edge.From];
      var to = indexById[edge.To];
      neighbours[from].Add(to);
      neighbours[to].Add(from);
    }

    // Find the largest same-group component of every group.
    var groups = graph.Nodes.Select(node => node.Style.Group!).ToArray();
    var visited = new bool[graph.Nodes.Count];
    var bestByGroup = new Dictionary<string, int>();
    for (var start = 0; start < graph.Nodes.Count; start++)
    {
      if (visited[start]) continue;

      // Walk the component reachable over same-group nodes only.
      var size = 0;
      var queue = new Queue<int>();
      visited[start] = true;
      queue.Enqueue(start);
      while (queue.Count > 0)
      {
        var current = queue.Dequeue();
        size++;
        foreach (var next in neighbours[current])
        {
          if (visited[next] || groups[next] != groups[start]) continue;
          visited[next] = true;
          queue.Enqueue(next);
        }
      }

      bestByGroup[groups[start]] = Math.Max(bestByGroup.GetValueOrDefault(groups[start]), size);
    }

    return bestByGroup.Values.Sum() / (double)graph.Nodes.Count;
  }
}
