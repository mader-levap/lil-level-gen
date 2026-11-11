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

      Assert.Equal(expected.Nodes[i].OpenReach, actual.Nodes[i].OpenReach);
    }
  }

  /// <summary>
  /// Asserts that a node's open reach lists exactly the expected start POIs, in ascending order.
  /// </summary>
  /// <param name="node">Node whose <see cref="LilNode.OpenReach"/> is checked.</param>
  /// <param name="startPois">Expected start POI identifiers, ascending.</param>
  private static void AssertOpenReach(LilNode node, params int[] startPois)
  {
    Assert.Equal(startPois.Select(Id).ToList(), node.OpenReach);
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
    public void Generate_WithTwoDisjointSingleKeyRoutes_ListsNeitherSolution()
    {
      // Arrange: Two disjoint routes from the start to the exit, each behind its own single
      // key (13 and 14), so neither key is unavoidable.
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

      // Assert: Each key can be avoided by taking the other route, so none is required.
      AssertReach(graph.Nodes[3], (11, []));
      // Assert: The rooms holding the keys are open from the start.
      AssertReach(graph.Nodes[1], (11, []));
      AssertReach(graph.Nodes[2], (11, []));
    }

    [Fact]
    public void Generate_WithKeyBehindKey_ListsBothTransitively()
    {
      // Arrange: Door A (needs 13) leads to the room with solution 14; door B (needs 14)
      // leads to the exit. Solution 13 lies in the start room.
      var graph = new LilGraph
      {
        Nodes =
        [
          Node(1, Poi(11, EnPoiType.Start), Poi(13, EnPoiType.Solution)),
          Node(2, Poi(14, EnPoiType.Solution)),
          Node(3, Poi(15, EnPoiType.Exit)),
        ],
        Edges =
        [
          Edge(1, 1, 2, conditions: [Condition(13)]),
          Edge(2, 2, 3, conditions: [Condition(14)]),
        ],
      };

      // Act: Generate the metadata.
      new GraphMetadata(graph).Generate();

      // Assert: The middle room needs door A; the exit needs door B and, transitively, door A.
      AssertReach(graph.Nodes[1], (11, [13]));
      AssertReach(graph.Nodes[2], (11, [13, 14]));
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

  /// <summary>Tests <see cref="LilNode.OpenReach"/>: reachability without any solution.</summary>
  public sealed class OpenReachTests
  {
    [Fact]
    public void Generate_WithSpecExample_ListsStartsReachingOpenly()
    {
      // Arrange: The specification example graph.
      var graph = CreateSpecGraph();

      // Act: Generate the metadata.
      new GraphMetadata(graph).Generate();

      // Assert: A is behind a one-way passage from start1 only; B and C are open from both
      // starts; D lies behind the solution door, so no start reaches it openly.
      AssertOpenReach(graph.Nodes[0], 11);
      AssertOpenReach(graph.Nodes[1], 11, 12);
      AssertOpenReach(graph.Nodes[2], 11, 12);
      AssertOpenReach(graph.Nodes[3]);
    }

    [Fact]
    public void Generate_WithTwoDisjointSingleKeyRoutes_ExitIsNotOpenlyReachable()
    {
      // Arrange: Two disjoint routes to the exit, behind key 13 or key 14.
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

      // Assert: No single key is unavoidable, yet some key is always needed for the exit.
      AssertReach(graph.Nodes[3], (11, []));
      AssertOpenReach(graph.Nodes[3]);
      // Assert: The start and the key rooms are open.
      AssertOpenReach(graph.Nodes[0], 11);
      AssertOpenReach(graph.Nodes[1], 11);
      AssertOpenReach(graph.Nodes[2], 11);
    }

    [Fact]
    public void Generate_WithParallelOpenAndConditionalPassages_ExitIsOpenlyReachable()
    {
      // Arrange: The exit is reachable through an open passage and a parallel locked one.
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

      // Assert: The open passage makes the exit openly reachable.
      AssertOpenReach(graph.Nodes[1], 11);
    }

    [Fact]
    public void Generate_WithKeyBehindOpenRoom_LockedNodeIsNotOpenlyReachable()
    {
      // Arrange: Open room with key 13, then a locked passage requiring it.
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
          Edge(2, 2, 3, conditions: [Condition(13)]),
        ],
      };

      // Act: Generate the metadata.
      new GraphMetadata(graph).Generate();

      // Assert: The exit is reachable, but only with the key, so it is not openly reachable.
      AssertReach(graph.Nodes[2], (11, [13]));
      AssertOpenReach(graph.Nodes[2]);
      AssertOpenReach(graph.Nodes[1], 11);
    }

    [Fact]
    public void Generate_WithUnreachableNode_HasEmptyOpenReachAndNoReach()
    {
      // Arrange: A start room and a disconnected room.
      var graph = new LilGraph
      {
        Nodes = [Node(1, Poi(11, EnPoiType.Start)), Node(2)],
        Edges = [],
      };

      // Act: Generate the metadata.
      new GraphMetadata(graph).Generate();

      // Assert: The disconnected room appears in neither output.
      Assert.Empty(graph.Nodes[1].Reach);
      AssertOpenReach(graph.Nodes[1]);
      AssertOpenReach(graph.Nodes[0], 11);
    }

    [Fact]
    public void Generate_WithStartsInReverseIdOrder_SortsOpenReachAscending()
    {
      // Arrange: Two starts whose node order is the reverse of their POI id order, joined openly.
      var graph = new LilGraph
      {
        Nodes = [Node(1, Poi(22, EnPoiType.Start)), Node(2, Poi(21, EnPoiType.Start))],
        Edges = [Edge(1, 1, 2)],
      };

      // Act: Generate the metadata.
      new GraphMetadata(graph).Generate();

      // Assert: Both nodes list both starts in ascending id order.
      AssertOpenReach(graph.Nodes[0], 21, 22);
      AssertOpenReach(graph.Nodes[1], 21, 22);
    }

    [Fact]
    public void Generate_Twice_DoesNotDuplicateOpenReach()
    {
      // Arrange: The specification example graph, processed once.
      var graph = CreateSpecGraph();
      new GraphMetadata(graph).Generate();

      // Act: Process it again.
      new GraphMetadata(graph).Generate();

      // Assert: Entries are replaced, not accumulated.
      AssertOpenReach(graph.Nodes[1], 11, 12);
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

  /// <summary>
  /// Tests that the search scales with POIs that conditions reference, not with all POIs:
  /// goal-like POIs that no passage requires must neither slow the pass down nor change its output.
  /// </summary>
  public sealed class ScalingTests
  {
    /// <summary>Time budget for one metadata pass on the ladder graphs below.</summary>
    private static readonly TimeSpan PassTimeout = TimeSpan.FromSeconds(2);

    /// <summary>Number of rungs of the ladder graph; two nodes per rung.</summary>
    private const int LadderRungs = 16;

    /// <summary>Node id offset for the second rail of the ladder.</summary>
    private const int SecondRailOffset = 100;

    /// <summary>POI id offset; POI ids are node id plus this offset.</summary>
    private const int PoiOffset = 1000;

    /// <summary>
    /// Builds a 2 x <see cref="LadderRungs"/> ladder: two rails of open passages joined by a
    /// rung at every step, so most nodes are reachable along many different paths. Node 1
    /// holds the start POI; with <paramref name="withGoals"/> every other node holds an
    /// optional goal POI that no passage requires. With <paramref name="withConditions"/>
    /// node 2 holds a solution and the last rung requires it.
    /// </summary>
    /// <param name="withGoals">Whether every non-start node gets an unrequired optional goal POI.</param>
    /// <param name="withConditions">Whether one solution POI and one conditional passage are added.</param>
    /// <returns>The ladder graph with deterministic identifiers.</returns>
    private static LilGraph CreateLadderGraph(bool withGoals, bool withConditions)
    {
      var nodes = new List<LilNode>();
      var edges = new List<LilEdge>();
      int edgeId = 1;

      for (int rung = 1; rung <= LadderRungs; rung++)
      {
        foreach (int nodeId in new[] { rung, rung + SecondRailOffset })
        {
          var pois = new List<LilPoi>();
          if (nodeId == 1) pois.Add(Poi(nodeId + PoiOffset, EnPoiType.Start));
          else if (withConditions && nodeId == 2) pois.Add(Poi(nodeId + PoiOffset, EnPoiType.Solution));
          else if (withGoals) pois.Add(Poi(nodeId + PoiOffset, EnPoiType.OptionalGoal));
          nodes.Add(Node(nodeId, pois.ToArray()));
        }

        // Rung between the rails; the last one is conditional when requested.
        bool lastRung = rung == LadderRungs;
        edges.Add(withConditions && lastRung
          ? Edge(edgeId++, rung, rung + SecondRailOffset, conditions: [Condition(2 + PoiOffset)])
          : Edge(edgeId++, rung, rung + SecondRailOffset));

        // Rails to the next rung.
        if (!lastRung)
        {
          edges.Add(Edge(edgeId++, rung, rung + 1));
          edges.Add(Edge(edgeId++, rung + SecondRailOffset, rung + 1 + SecondRailOffset));
        }
      }

      return new LilGraph { Nodes = nodes, Edges = edges };
    }

    /// <summary>Runs the metadata pass on a worker task and reports whether it finished in time.</summary>
    /// <param name="graph">Graph to process in place.</param>
    /// <returns>True when the pass finished within <see cref="PassTimeout"/>.</returns>
    private static bool GenerateWithinBudget(LilGraph graph)
    {
      return Task.Run(() => new GraphMetadata(graph).Generate()).Wait(PassTimeout);
    }

    [Fact]
    public void Generate_WithManyUnrequiredPoisOnMultiPathGraph_FinishesInTime()
    {
      // Arrange: Ladder with an unrequired optional goal on nearly every node and no conditions.
      var graph = CreateLadderGraph(withGoals: true, withConditions: false);

      // Act: Run the pass under the time budget.
      var finished = GenerateWithinBudget(graph);

      // Assert: Unrequired POIs do not blow up the search.
      Assert.True(finished, $"Metadata pass did not finish within {PassTimeout}.");
    }

    [Fact]
    public void Generate_WithManyUnrequiredPoisAndConditions_FinishesInTime()
    {
      // Arrange: Same ladder plus one solution and one conditional passage.
      var graph = CreateLadderGraph(withGoals: true, withConditions: true);

      // Act: Run the pass under the time budget.
      var finished = GenerateWithinBudget(graph);

      // Assert: The search stays fast when some POIs do matter.
      Assert.True(finished, $"Metadata pass did not finish within {PassTimeout}.");
    }

    [Fact]
    public void Generate_WithUnrequiredPois_ProducesSameReachAsWithout()
    {
      // Arrange: The conditional ladder with and without unrequired goal POIs.
      var withGoals = CreateLadderGraph(withGoals: true, withConditions: true);
      var withoutGoals = CreateLadderGraph(withGoals: false, withConditions: true);

      // Act: Run the pass on both.
      Assert.True(GenerateWithinBudget(withGoals), $"Metadata pass did not finish within {PassTimeout}.");
      Assert.True(GenerateWithinBudget(withoutGoals), $"Metadata pass did not finish within {PassTimeout}.");

      // Assert: POIs no passage requires cannot change what is required to reach a node.
      AssertReachSame(withoutGoals, withGoals);

      // Assert: The far end of the conditional last rung is also reachable along the
      // second rail, so its minimal requirement stays empty.
      var lastFar = withGoals.Nodes.Single(node => node.Id == Id(LadderRungs + SecondRailOffset));
      AssertReach(lastFar, (1 + PoiOffset, []));
    }

    /// <summary>Number of chain nodes in the skippable-guard chain.</summary>
    private const int GuardedChainLength = 40;

    /// <summary>POI id of the start in the skippable-guard chain.</summary>
    private const int ChainStartPoi = 900;

    /// <summary>Node id of the vault hanging off the last chain node.</summary>
    private const int VaultNode = 500;

    /// <summary>POI id of the vault key, placed in chain node 2.</summary>
    private const int VaultKeyPoi = 1500;

    /// <summary>
    /// Builds the shape that blew up the exact search at difficulty 100: a chain 1..N where
    /// passage (i, i+1) requires solution <c>PoiOffset + i</c> lying in node i, plus open
    /// one-way shortcuts (i -> i+2) that skip guarded nodes. A vault behind the last node requires a key
    /// lying in node 2, which itself is only reachable through the first guarded passage.
    /// </summary>
    /// <returns>The chain graph with deterministic identifiers.</returns>
    private static LilGraph CreateSkippableGuardChain()
    {
      var nodes = new List<LilNode>();
      var edges = new List<LilEdge>();
      int edgeId = 1;

      for (int i = 1; i <= GuardedChainLength; i++)
      {
        var pois = new List<LilPoi>();
        if (i == 1) pois.Add(Poi(ChainStartPoi, EnPoiType.Start));
        if (i < GuardedChainLength) pois.Add(Poi(PoiOffset + i, EnPoiType.Solution));
        if (i == 2) pois.Add(Poi(VaultKeyPoi, EnPoiType.Solution));
        nodes.Add(Node(i, pois.ToArray()));

        if (i < GuardedChainLength)
          edges.Add(Edge(edgeId++, i, i + 1, conditions: [Condition(PoiOffset + i)]));
        if (i + 2 <= GuardedChainLength)
          edges.Add(Edge(edgeId++, i, i + 2, EnPassageDirection.OneWay));
      }

      nodes.Add(Node(VaultNode));
      edges.Add(Edge(edgeId, GuardedChainLength, VaultNode, conditions: [Condition(VaultKeyPoi)]));
      return new LilGraph { Nodes = nodes, Edges = edges };
    }

    [Fact]
    public void Generate_WithManySkippableGuardedNodes_FinishesInTime()
    {
      // Arrange: Chain with about forty guarded passages that shortcuts can bypass.
      var graph = CreateSkippableGuardChain();

      // Act: Run the pass under the time budget.
      var finished = GenerateWithinBudget(graph);

      // Assert: Many conditions do not blow up the search.
      Assert.True(finished, $"Metadata pass did not finish within {PassTimeout}.");
    }

    [Fact]
    public void Generate_WithManySkippableGuardedNodes_ListsOnlyUnavoidableKeys()
    {
      // Arrange: Same chain.
      var graph = CreateSkippableGuardChain();

      // Act: Run the pass under the time budget.
      Assert.True(GenerateWithinBudget(graph), $"Metadata pass did not finish within {PassTimeout}.");

      // Assert: Node 2 has no shortcut into it, so its guard is unavoidable. Every later node
      // can be entered either through its guard or over a shortcut, so it needs nothing.
      Node2Reach(graph, [PoiOffset + 1]);
      for (int i = 3; i <= GuardedChainLength; i++)
      {
        var node = graph.Nodes.Single(n => n.Id == Id(i));
        AssertReach(node, (ChainStartPoi, []));
      }

      // Assert: The vault needs its own key and, transitively, node 2's guard.
      var vault = graph.Nodes.Single(n => n.Id == Id(VaultNode));
      AssertReach(vault, (ChainStartPoi, [PoiOffset + 1, VaultKeyPoi]));
    }

    /// <summary>Asserts the reach entry of chain node 2.</summary>
    /// <param name="graph">Chain graph.</param>
    /// <param name="required">Expected required POI ids.</param>
    private static void Node2Reach(LilGraph graph, int[] required)
    {
      AssertReach(graph.Nodes.Single(n => n.Id == Id(2)), (ChainStartPoi, required));
    }
  }
}
