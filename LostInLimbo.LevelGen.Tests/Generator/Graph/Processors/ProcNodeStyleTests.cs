using LostInLimbo.LevelGen.Data.Config.Graph;
using LostInLimbo.LevelGen.Data.Result.Graph;
using LostInLimbo.LevelGen.Internal.Generator.Graph.Processors;
using static LostInLimbo.LevelGen.Tests.Generator.Graph.GraphTestCreate;
using static LostInLimbo.LevelGen.Tests.Generator.Graph.GraphTestSupport;

namespace LostInLimbo.LevelGen.Tests.Generator.Graph.Processors;

/// <summary>
/// Tests <see cref="ProcNodeStyle"/>: option validation, group naming and distribution,
/// contiguous growth with the restricted-relationship fallback and determinism of the
/// assignment.
/// </summary>
/// <remarks>
/// The styler works on a finished <see cref="LilGraph"/>, so most tests construct graphs
/// directly instead of going through a builder - that keeps topology exactly controlled
/// per test. Growth and fallback expectations are statistical: they average contiguity
/// over many seeds, because the exact outcome depends on the random seed split.
/// </remarks>
public sealed class ProcNodeStyleTests
{
  /// <summary>Graph sub-seed used by all tests unless a test varies the seed itself.</summary>
  private const long Seed = GraphTestSupport.DefaultSeedGraph;

  /// <summary>Amount of seeds the statistical tests average over.</summary>
  private const int StatisticalSeedCount = 50;

  /// <summary>Tests for the hard constraints: nulls, out-of-range values and broken graphs.</summary>
  public sealed class ValidationTests
  {
    [Fact]
    public void Constructor_WithNullGraph_ThrowsArgumentNullException()
    {
      // Act & Assert: A missing graph is rejected.
      Assert.Throws<ArgumentNullException>(() => new ProcNodeStyle(null!, CnfProcNodeStyleOpts.Default, Seed));
    }

    [Fact]
    public void Constructor_WithNullOptions_ThrowsArgumentNullException()
    {
      // Act & Assert: Missing options are rejected.
      Assert.Throws<ArgumentNullException>(() => new ProcNodeStyle(CreateChainGraph(4), null!, Seed));
    }

    [Fact]
    public void Constructor_WithInvalidOptions_ThrowsBeforeExecute()
    {
      // Arrange: Options rejected by validation; the rules themselves are pinned by
      // CnfProcOptsTests.
      var options = CnfProcNodeStyleOpts.Default with { GroupCount = 0 };

      // Act & Assert: Construction alone rejects the options, before Execute can run.
      Assert.Throws<ArgumentOutOfRangeException>(
        () => new ProcNodeStyle(CreateChainGraph(4), options, Seed));
    }

    [Fact]
    public void Execute_WithEdgeToUnknownNode_ThrowsInvalidOperationException()
    {
      // Arrange: Chain whose extra edge points at a node that does not exist.
      var graph = CreateChainGraph(4);
      graph.Edges.Add(CreateEdge(3, 77));

      // Act & Assert: The dangling reference is reported.
      var exception = Assert.Throws<InvalidOperationException>(
        () => new ProcNodeStyle(graph, CnfProcNodeStyleOpts.Default, Seed).Execute());
      Assert.Contains("unknown node", exception.Message);
    }

    [Fact]
    public void Execute_WithDuplicateNodeIds_ThrowsArgumentException()
    {
      // Arrange: Two nodes sharing one identifier.
      var graph = CreateChainGraph(4);
      graph.Nodes.Add(graph.Nodes[0] with { });

      // Act & Assert: The duplicate identifier is rejected.
      Assert.Throws<ArgumentException>(() => new ProcNodeStyle(graph, CnfProcNodeStyleOpts.Default, Seed).Execute());
    }

    [Fact]
    public void Execute_WithEmptyGraph_LeavesGraphEmpty()
    {
      // Arrange: Graph without nodes.
      var graph = new LilGraph();

      // Act & Assert: Styling an empty graph is a no-op, not an error.
      new ProcNodeStyle(graph, CnfProcNodeStyleOpts.Default, Seed).Execute();
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
      new ProcNodeStyle(graph, CnfProcNodeStyleOpts.Default, Seed).Execute();

      // Assert: No node is left without a group.
      Assert.All(graph.Nodes, node => Assert.NotNull(node.Style.Group));
    }

    [Fact]
    public void Execute_WithMoreGroupsThanNodes_DropsExcessGroups()
    {
      // Arrange: Two nodes, three requested groups - the soft target must drop one.
      var graph = CreateChainGraph(2);
      var options = CnfProcNodeStyleOpts.Default with { GroupCount = 3 };

      // Act: Style the graph.
      new ProcNodeStyle(graph, options, Seed).Execute();

      // Assert: One group per node, named with the two-group width.
      var groups = graph.Nodes.Select(node => node.Style.Group).OrderBy(name => name).ToList();
      Assert.Equal(new[] { "g1", "g2" }, groups);
    }

    [Fact]
    public void Execute_WithSingleGroup_GroupsAllNodesTogether()
    {
      // Arrange: Chain with a single allowed group.
      var graph = CreateChainGraph(8);
      var options = CnfProcNodeStyleOpts.Default with { GroupCount = 1 };

      // Act: Style the graph.
      new ProcNodeStyle(graph, options, Seed).Execute();

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
      var options = CnfProcNodeStyleOpts.Default with { GroupCount = groupCount };

      // Act: Style the graph.
      new ProcNodeStyle(graph, options, Seed).Execute();

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
      var options = CnfProcNodeStyleOpts.Default with { GroupCount = 12 };

      // Act: Style the graph.
      new ProcNodeStyle(graph, options, Seed).Execute();

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
        new ProcNodeStyle(graph, CnfProcNodeStyleOpts.Default, seed).Execute();

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
        new ProcNodeStyle(graph, CnfProcNodeStyleOpts.Default, seed).Execute();

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
        new ProcNodeStyle(graph, CnfProcNodeStyleOpts.Default, seed).Execute();

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
        new ProcNodeStyle(graph, CnfProcNodeStyleOpts.Default, seed).Execute();

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
      new ProcNodeStyle(graph, CnfProcNodeStyleOpts.Default, Seed).Execute();

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
      new ProcNodeStyle(first, CnfProcNodeStyleOpts.Default, Seed).Execute();
      new ProcNodeStyle(second, CnfProcNodeStyleOpts.Default, Seed).Execute();

      // Assert: Same identifiers with the same groups.
      GraphTestAssert.AssertEqualGraphs(first, second);
      Assert.Equal(GetGroupAssignments(first), GetGroupAssignments(second));
    }

    [Fact]
    public void Execute_WithDifferentSeed_ProducesDifferentAssignments()
    {
      // Arrange: Same structure, different seeds.
      var first = CreateChainGraph(30);
      var second = CreateChainGraph(30);

      // Act: Style both graphs.
      new ProcNodeStyle(first, CnfProcNodeStyleOpts.Default, seedGraph: 1).Execute();
      new ProcNodeStyle(second, CnfProcNodeStyleOpts.Default, seedGraph: 2).Execute();

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
      new ProcNodeStyle(original, CnfProcNodeStyleOpts.Default, Seed).Execute();
      new ProcNodeStyle(reversed, CnfProcNodeStyleOpts.Default, Seed).Execute();

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
      new ProcNodeStyle(graph, CnfProcNodeStyleOpts.Default, Seed).Execute();

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
      new ProcNodeStyle(graph, CnfProcNodeStyleOpts.Default, Seed).Execute();
      var firstRun = GetGroupAssignments(graph);

      // Act: Style the very same graph again with the same inputs.
      new ProcNodeStyle(graph, CnfProcNodeStyleOpts.Default, Seed).Execute();

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
      new ProcNodeStyle(graph, CnfProcNodeStyleOpts.Default, Seed).Execute();

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
        new ProcNodeStyle(first, CnfProcNodeStyleOpts.Default, seed).Execute();
        new ProcNodeStyle(second, CnfProcNodeStyleOpts.Default, seed).Execute();

        // Assert: The restricted-relationship fallback stays deterministic too.
        GraphTestAssert.AssertEqualGraphs(first, second);
      }
    }
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
