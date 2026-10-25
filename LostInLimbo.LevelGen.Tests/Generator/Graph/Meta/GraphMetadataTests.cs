using LostInLimbo.LevelGen.Data.Result.Graph;
using LostInLimbo.LevelGen.Internal.Generator.Graph.Meta;
using static LostInLimbo.LevelGen.Tests.Generator.Graph.GraphTestCreate;

namespace LostInLimbo.LevelGen.Tests.Generator.Graph.Meta;

/// <summary>
/// Tests <see cref="GraphMetadata"/>: per-start reach metadata stored on every node
/// (<see cref="LilNode.Reach"/>), its minimality rule, its tolerance of broken graphs and
/// its determinism.
/// </summary>
/// <remarks>
/// Grouped into suites: specification semantics (the motivating example, minimality,
/// secret passages), boundary and robustness cases (null/empty graphs, unreachable
/// content, dangling references) and determinism (repeat runs, edge order independence).
/// The pass never verifies the graph - broken references must be skipped, never thrown.
/// </remarks>
public sealed class GraphMetadataTests
{
  /// <summary>
  /// Builds the specification example: node A (start1) -one way-> node B (start2) &lt;-&gt;
  /// node C (solution1) &lt;-&gt; node D (exit), where the passage out of C requires solution1.
  /// </summary>
  /// <returns>The example graph with deterministic identifiers.</returns>
  private static LilGraph CreateSpecGraph()
  {
    return new LilGraph
    {
      Nodes =
      [
        Node(1, Poi(11, EnPoiType.Start)),
        Node(2, Poi(12, EnPoiType.Start)),
        Node(3, Poi(13, EnPoiType.Solution)),
        Node(4, Poi(14, EnPoiType.Exit)),
      ],
      Edges =
      [
        Edge(1, 1, 2, EnPassageDirection.OneWay),
        Edge(2, 2, 3),
        Edge(3, 3, 4, conditions: [Condition(13)]),
      ],
    };
  }

  /// <summary>
  /// Asserts that a node's reach dictionary holds exactly the expected entries: one key per
  /// expected start POI, no extras, and each required list matching element-wise - which
  /// also pins the ascending identifier order of the lists.
  /// </summary>
  /// <param name="node">Node whose <see cref="LilNode.Reach"/> is checked.</param>
  /// <param name="expected">Pairs of start POI identifier and required POI identifiers.</param>
  private static void AssertReach(LilNode node, params (int StartPoi, int[] Required)[] expected)
  {
    Assert.Equal(expected.Length, node.Reach.Count);
    foreach (var (startPoi, required) in expected)
    {
      var key = Id(startPoi);
      Assert.True(node.Reach.ContainsKey(key), $"Missing Reach entry for start POI {startPoi}.");
      var actual = node.Reach[key];
      Assert.Equal(required.Length, actual.Count);
      for (var i = 0; i < required.Length; i++)
      {
        Assert.Equal(Id(required[i]), actual[i]);
      }
    }
  }

  /// <summary>
  /// Asserts that two graphs carry the same reach metadata, node by node and entry by entry.
  /// </summary>
  /// <param name="expected">Graph whose reach data is the reference.</param>
  /// <param name="actual">Graph that must carry identical reach data.</param>
  private static void AssertReachSame(LilGraph expected, LilGraph actual)
  {
    Assert.Equal(expected.Nodes.Count, actual.Nodes.Count);
    for (var i = 0; i < expected.Nodes.Count; i++)
    {
      var expectedReach = expected.Nodes[i].Reach;
      var actualReach = actual.Nodes[i].Reach;
      Assert.Equal(expectedReach.Count, actualReach.Count);
      foreach (var (startId, required) in expectedReach)
      {
        Assert.True(actualReach.ContainsKey(startId), $"Node {i} misses entry for start {startId}.");
        Assert.Equal(required, actualReach[startId]);
      }
    }
  }

  /// <summary>Tests the specification semantics: per-start entries, minimality and secrets.</summary>
  public sealed class SpecSemanticsTests
  {
    [Fact]
    public void Generate_WithSpecExample_ProducesReachPerNode()
    {
      // Arrange: The specification example graph.
      var graph = CreateSpecGraph();

      // Act: Generate the metadata.
      new GraphMetadata(graph).Generate();

      // Assert: A sits behind a one-way passage, so only start1 reaches it, with no conditions.
      AssertReach(graph.Nodes[0], (11, []));
      // Assert: Both starts reach B through open passages (start1 via A, start2 in place).
      AssertReach(graph.Nodes[1], (11, []), (12, []));
      // Assert: C is open from both sides, so no solution is required to stand there.
      AssertReach(graph.Nodes[2], (11, []), (12, []));
      // Assert: D lies behind the solution1 door, so both starts must have collected it.
      AssertReach(graph.Nodes[3], (11, [13]), (12, [13]));
    }

    [Fact]
    public void Generate_WithParallelOpenAndConditionalPassages_PrefersOpenPassage()
    {
      // Arrange: The exit is reachable through an open passage and, in parallel, through a
      // door requiring solution 13, which sits in a side room off the start.
      var graph = new LilGraph
      {
        Nodes =
        [
          Node(1, Poi(11, EnPoiType.Start)),
          Node(2, Poi(14, EnPoiType.Exit)),
          Node(3, Poi(13, EnPoiType.Solution)),
        ],
        Edges =
        [
          Edge(1, 1, 2),
          Edge(2, 1, 2, conditions: [Condition(13)]),
          Edge(3, 1, 3),
        ],
      };

      // Act: Generate the metadata.
      new GraphMetadata(graph).Generate();

      // Assert: The open passage wins - the exit needs no solutions at all.
      AssertReach(graph.Nodes[1], (11, []));
      // Assert: The side room is reachable open, so its own solution is not required either.
      AssertReach(graph.Nodes[2], (11, []));
    }

    [Fact]
    public void Generate_WithTwoDisjointSingleKeyRoutes_PicksSmallestSolution()
    {
      // Arrange: Two disjoint routes from the start to the exit, each behind its own single
      // key (13 and 14); both minimal sets have size one, so the tie must break stably.
      var graph = new LilGraph
      {
        Nodes =
        [
          Node(1, Poi(11, EnPoiType.Start)),
          Node(2, Poi(13, EnPoiType.Solution)),
          Node(3, Poi(14, EnPoiType.Solution)),
          Node(4, Poi(15, EnPoiType.Exit)),
        ],
        Edges =
        [
          Edge(1, 1, 2),
          Edge(2, 2, 4, conditions: [Condition(13)]),
          Edge(3, 1, 3),
          Edge(4, 3, 4, conditions: [Condition(14)]),
        ],
      };

      // Act: Generate the metadata.
      new GraphMetadata(graph).Generate();

      // Assert: Both routes cost exactly one solution; the smaller identifier wins the tie.
      AssertReach(graph.Nodes[3], (11, [13]));
    }

    [Fact]
    public void Generate_WithUnusedSolutionOnPath_DoesNotListIt()
    {
      // Arrange: The only route to the exit passes through a room holding a solution, but no
      // passage on the way requires it - picking it up en route does not make it required.
      var graph = new LilGraph
      {
        Nodes =
        [
          Node(1, Poi(11, EnPoiType.Start)),
          Node(2, Poi(13, EnPoiType.Solution)),
          Node(3, Poi(14, EnPoiType.Exit)),
        ],
        Edges =
        [
          Edge(1, 1, 2),
          Edge(2, 2, 3),
        ],
      };

      // Act: Generate the metadata.
      new GraphMetadata(graph).Generate();

      // Assert: The exit is reachable without ever needing the solution lying on the path.
      AssertReach(graph.Nodes[2], (11, []));
    }

    [Fact]
    public void Generate_WithMultipleConditionsOnPassage_ListsBothSolutions()
    {
      // Arrange: A door on the only route requires two solutions, both inside the room before it.
      var graph = new LilGraph
      {
        Nodes =
        [
          Node(1, Poi(11, EnPoiType.Start)),
          Node(2, Poi(13, EnPoiType.Solution), Poi(14, EnPoiType.Solution)),
          Node(3, Poi(15, EnPoiType.Exit)),
        ],
        Edges =
        [
          Edge(1, 1, 2),
          Edge(2, 2, 3, conditions: [Condition(13), Condition(14)]),
        ],
      };

      // Act: Generate the metadata.
      new GraphMetadata(graph).Generate();

      // Assert: The room before the door needs nothing; the exit needs both solutions.
      AssertReach(graph.Nodes[1], (11, []));
      AssertReach(graph.Nodes[2], (11, [13, 14]));
    }

    [Fact]
    public void Generate_WithSecretPassages_TreatsThemLikeOrdinaryPassages()
    {
      // Arrange: A secret open passage and a secret conditional passage leaving the start room.
      var graph = new LilGraph
      {
        Nodes =
        [
          Node(1, Poi(11, EnPoiType.Start)),
          Node(2, Poi(13, EnPoiType.Solution)),
          Node(3, Poi(14, EnPoiType.Exit)),
        ],
        Edges =
        [
          Edge(1, 1, 2, secret: true),
          Edge(2, 2, 3, secret: true, conditions: [Condition(13)]),
        ],
      };

      // Act: Generate the metadata.
      new GraphMetadata(graph).Generate();

      // Assert: The secret open passage needs nothing; the secret door still needs its key -
      // the secret flag changes nothing about reach metadata.
      AssertReach(graph.Nodes[1], (11, []));
      AssertReach(graph.Nodes[2], (11, [13]));
    }
  }

  /// <summary>Tests boundary graphs and robustness against broken references.</summary>
  public sealed class BoundaryTests
  {
    [Fact]
    public void Generate_WithNullGraph_DoesNotThrow()
    {
      // Arrange: No graph at all.

      // Act: Generate over the missing graph.
      new GraphMetadata(null).Generate();

      // Assert: The pass returns without throwing; metadata generation never fails a run.
    }

    [Fact]
    public void Generate_WithEmptyGraph_DoesNotThrow()
    {
      // Arrange: A graph with no nodes and no edges.
      var graph = new LilGraph();

      // Act: Generate the metadata.
      new GraphMetadata(graph).Generate();

      // Assert: Nothing to fill, nothing to break.
      Assert.Empty(graph.Nodes);
      Assert.Empty(graph.Edges);
    }

    [Fact]
    public void Generate_WithSingleStartNode_EntryIsSelfWithEmptyList()
    {
      // Arrange: One node holding both start and exit, no passages at all.
      var graph = new LilGraph
      {
        Nodes = [Node(1, Poi(11, EnPoiType.Start), Poi(12, EnPoiType.Exit))],
      };

      // Act: Generate the metadata.
      new GraphMetadata(graph).Generate();

      // Assert: The start reaches itself without meeting any condition.
      AssertReach(graph.Nodes[0], (11, []));
    }

    [Fact]
    public void Generate_WithNoStartPois_LeavesReachEmpty()
    {
      // Arrange: A connected graph in which nobody starts.
      var graph = new LilGraph
      {
        Nodes = [Node(1, Poi(11, EnPoiType.Exit)), Node(2)],
        Edges = [Edge(1, 1, 2)],
      };

      // Act: Generate the metadata.
      new GraphMetadata(graph).Generate();

      // Assert: Without start POIs there are no keys to report, on any node.
      Assert.Empty(graph.Nodes[0].Reach);
      Assert.Empty(graph.Nodes[1].Reach);
    }

    [Fact]
    public void Generate_WithTwoStartPoisInOneNode_CreatesBothEntries()
    {
      // Arrange: One node hosting two start POIs, connected to an exit room.
      var graph = new LilGraph
      {
        Nodes = [Node(1, Poi(11, EnPoiType.Start), Poi(12, EnPoiType.Start)), Node(2, Poi(13, EnPoiType.Exit))],
        Edges = [Edge(1, 1, 2)],
      };

      // Act: Generate the metadata.
      new GraphMetadata(graph).Generate();

      // Assert: Each start POI is its own key, even when both sit in the same node.
      AssertReach(graph.Nodes[0], (11, []), (12, []));
      AssertReach(graph.Nodes[1], (11, []), (12, []));
    }

    [Fact]
    public void Generate_WithNodeUnreachableFromAllStarts_HasEmptyReach()
    {
      // Arrange: A start component of two nodes plus an isolated third node.
      var graph = new LilGraph
      {
        Nodes = [Node(1, Poi(11, EnPoiType.Start)), Node(2, Poi(12, EnPoiType.Exit)), Node(3)],
        Edges = [Edge(1, 1, 2)],
      };

      // Act: Generate the metadata.
      new GraphMetadata(graph).Generate();

      // Assert: Reachable nodes report the start; the isolated node has no entries at all.
      AssertReach(graph.Nodes[0], (11, []));
      AssertReach(graph.Nodes[1], (11, []));
      Assert.Empty(graph.Nodes[2].Reach);
    }

    [Fact]
    public void Generate_WithUnreachableKey_TreatsDoorNodeAsUnreachable()
    {
      // Arrange: The exit door requires solution 13, but the key room is disconnected from
      // the start, so the door can never be opened from there.
      var graph = new LilGraph
      {
        Nodes =
        [
          Node(1, Poi(11, EnPoiType.Start)),
          Node(2, Poi(12, EnPoiType.Exit)),
          Node(3, Poi(13, EnPoiType.Solution)),
        ],
        Edges = [Edge(1, 1, 2, conditions: [Condition(13)])],
      };

      // Act: Generate the metadata.
      new GraphMetadata(graph).Generate();

      // Assert: The start reports itself; the door node gets no entry instead of a required
      // list whose key could never be collected.
      AssertReach(graph.Nodes[0], (11, []));
      Assert.Empty(graph.Nodes[1].Reach);
      Assert.Empty(graph.Nodes[2].Reach);
    }

    [Fact]
    public void Generate_WithDanglingReferences_SkipsInsteadOfThrowing()
    {
      // Arrange: One edge points at a node that does not exist, another carries a condition
      // referencing a POI that does not exist - the pass skips, it never verifies.
      var graph = new LilGraph
      {
        Nodes = [Node(1, Poi(11, EnPoiType.Start)), Node(2, Poi(12, EnPoiType.Exit))],
        Edges =
        [
          Edge(1, 1, 99),
          Edge(2, 1, 2, conditions: [Condition(77)]),
        ],
      };

      // Act: Generate the metadata.
      new GraphMetadata(graph).Generate();

      // Assert: The dangling passage is ignored, the unmeetable door stays closed, and no
      // exception escapes to fail the run.
      AssertReach(graph.Nodes[0], (11, []));
      Assert.Empty(graph.Nodes[1].Reach);
    }

    [Fact]
    public void Generate_WithCycleContainingCondition_Terminates()
    {
      // Arrange: A three-node cycle where the way back to the start requires a key the start
      // itself holds; the search must reach a fixpoint instead of looping forever.
      var graph = new LilGraph
      {
        Nodes =
        [
          Node(1, Poi(11, EnPoiType.Start), Poi(13, EnPoiType.Solution)),
          Node(2),
          Node(3, Poi(14, EnPoiType.Exit)),
        ],
        Edges =
        [
          Edge(1, 1, 2),
          Edge(2, 2, 3),
          Edge(3, 3, 1, conditions: [Condition(13)]),
        ],
      };

      // Act: Generate the metadata.
      new GraphMetadata(graph).Generate();

      // Assert: Every node is reachable through the open way round the cycle, so no solution
      // is ever required - including by the conditional way back into the start.
      AssertReach(graph.Nodes[0], (11, []));
      AssertReach(graph.Nodes[1], (11, []));
      AssertReach(graph.Nodes[2], (11, []));
    }
  }

  /// <summary>Tests that the pass is repeatable and independent of container order.</summary>
  public sealed class DeterminismTests
  {
    [Fact]
    public void Generate_TwiceOnSameGraph_ProducesSameReachAsOnce()
    {
      // Arrange: Two identically built spec graphs; one is generated once, the other twice.
      var once = CreateSpecGraph();
      var twice = CreateSpecGraph();
      new GraphMetadata(once).Generate();

      // Act: Run the pass a second time over the already-filled graph, as a repeated run would.
      new GraphMetadata(twice).Generate();
      new GraphMetadata(twice).Generate();

      // Assert: Re-running overwrites the same values instead of accumulating or changing them.
      AssertReachSame(once, twice);
    }

    [Fact]
    public void Generate_WithReversedEdgeOrder_ProducesIdenticalReach()
    {
      // Arrange: The same spec graph with its edge list in reverse order.
      var normal = CreateSpecGraph();
      var reversed = CreateSpecGraph();
      reversed.Edges.Reverse();

      // Act: Generate metadata for both orderings.
      new GraphMetadata(normal).Generate();
      new GraphMetadata(reversed).Generate();

      // Assert: Container order cannot influence the produced metadata.
      AssertReachSame(normal, reversed);
    }
  }
}
