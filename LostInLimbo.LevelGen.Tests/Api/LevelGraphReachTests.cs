using LostInLimbo.LevelGen.Api;
using LostInLimbo.LevelGen.Data.Prop;
using LostInLimbo.LevelGen.Data.Result;
using LostInLimbo.LevelGen.Data.Result.Graph;
using LostInLimbo.LevelGen.Internal.Generator.Graph;
using LostInLimbo.LevelGen.Internal.Generator.Prepass;
using LostInLimbo.LevelGen.Internal.Prop;
using static LostInLimbo.LevelGen.Tests.Generator.Graph.GraphTestCreate;

namespace LostInLimbo.LevelGen.Tests.Api;

/// <summary>
/// Tests <see cref="LevelGraphReach.GetReachableNodes"/>: which nodes the player can walk to
/// from a start with exactly the solutions they hold (used to decide which corpses are possessable).
/// </summary>
/// <remarks>
/// Grouped into suites: semantics on hand-built graphs, consistency with the metadata pass on
/// generated graphs, determinism and boundary cases, and argument validation.
/// </remarks>
public sealed class LevelGraphReachTests
{
  /// <summary>POI id of the start used in hand-built graphs.</summary>
  private const int StartPoi = 11;

  /// <summary>Runs the query and returns the reached node ids as small test integers would map to.</summary>
  /// <param name="graph">Graph to query.</param>
  /// <param name="held">Held POI ids as small test integers.</param>
  /// <returns>Reached node identifiers in result order.</returns>
  private static IReadOnlyList<Guid> Reach(LilGraph graph, params int[] held)
  {
    return LevelGraphReach.GetReachableNodes(graph, Id(StartPoi), held.Select(Id).ToList());
  }

  /// <summary>Checks whether the result of a query contains the given node.</summary>
  /// <param name="reached">Query result.</param>
  /// <param name="nodeId">Node id as a small test integer.</param>
  /// <returns>True when the node was reached.</returns>
  private static bool Has(IReadOnlyList<Guid> reached, int nodeId) => reached.Contains(Id(nodeId));

  /// <summary>
  /// Two routes from the start (node 1) to the exit (node 4): via node 2 behind key 13, or via
  /// node 3 behind key 14. The keys are not placed in the graph: for this query only held keys
  /// matter.
  /// </summary>
  /// <returns>The graph.</returns>
  private static LilGraph CreateEitherKeyGraph()
  {
    return new LilGraph
    {
      Nodes =
      [
        Node(1, Poi(StartPoi, EnPoiType.Start)),
        Node(2),
        Node(3),
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
  }

  /// <summary>Tests the reachability rules on hand-built graphs.</summary>
  public sealed class SemanticsTests
  {
    [Fact]
    public void GetReachableNodes_WithEitherOfTwoKeys_ReachesExitWithAnyOfThem()
    {
      // Arrange: Exit behind key 13 or key 14.
      var graph = CreateEitherKeyGraph();

      // Act/Assert: Without keys the exit stays closed; either key opens it.
      Assert.False(Has(Reach(graph), 4));
      Assert.True(Has(Reach(graph, 13), 4));
      Assert.True(Has(Reach(graph, 14), 4));
      // Assert: The open side rooms are always reachable.
      Assert.True(Has(Reach(graph), 2));
      Assert.True(Has(Reach(graph), 3));
    }

    [Fact]
    public void GetReachableNodes_WithOneKeyOrTwoKeysRoutes_NeedsAWholeRoute()
    {
      // Arrange: Exit (node 4) behind key 13 directly, or through node 3 behind keys 14 then 15.
      var graph = new LilGraph
      {
        Nodes =
        [
          Node(1, Poi(StartPoi, EnPoiType.Start)),
          Node(3),
          Node(4, Poi(16, EnPoiType.Exit)),
        ],
        Edges =
        [
          Edge(1, 1, 4, conditions: [Condition(13)]),
          Edge(2, 1, 3, conditions: [Condition(14)]),
          Edge(3, 3, 4, conditions: [Condition(15)]),
        ],
      };

      // Act/Assert: Half a route is not enough; a complete route is.
      Assert.False(Has(Reach(graph, 14), 4));
      Assert.False(Has(Reach(graph, 15), 4));
      Assert.True(Has(Reach(graph, 14, 15), 4));
      Assert.True(Has(Reach(graph, 13), 4));
    }

    [Fact]
    public void GetReachableNodes_WithKeyLyingInReachableRoomButNotHeld_KeepsDoorClosed()
    {
      // Arrange: Key 13 lies in the open room 2; the exit behind it requires it.
      var graph = new LilGraph
      {
        Nodes =
        [
          Node(1, Poi(StartPoi, EnPoiType.Start)),
          Node(2, Poi(13, EnPoiType.Solution)),
          Node(3, Poi(14, EnPoiType.Exit)),
        ],
        Edges =
        [
          Edge(1, 1, 2),
          Edge(2, 2, 3, conditions: [Condition(13)]),
        ],
      };

      // Act/Assert: Only held keys count; a key merely lying around is not picked up.
      Assert.False(Has(Reach(graph), 3));
      Assert.True(Has(Reach(graph, 13), 3));
    }

    [Fact]
    public void GetReachableNodes_WithKeyBehindKey_NeedsBothDoorsOpen()
    {
      // Arrange: Door A (needs 13) then door B (needs 14).
      var graph = new LilGraph
      {
        Nodes =
        [
          Node(1, Poi(StartPoi, EnPoiType.Start)),
          Node(2),
          Node(3, Poi(15, EnPoiType.Exit)),
        ],
        Edges =
        [
          Edge(1, 1, 2, conditions: [Condition(13)]),
          Edge(2, 2, 3, conditions: [Condition(14)]),
        ],
      };

      // Act/Assert: The second key alone does not get past the first door.
      Assert.False(Has(Reach(graph, 14), 3));
      Assert.True(Has(Reach(graph, 13), 2));
      Assert.False(Has(Reach(graph, 13), 3));
      Assert.True(Has(Reach(graph, 13, 14), 3));
    }

    [Fact]
    public void GetReachableNodes_WithTwoConditionsOnPassage_NeedsBothKeys()
    {
      // Arrange: One door needing keys 13 and 14.
      var graph = new LilGraph
      {
        Nodes = [Node(1, Poi(StartPoi, EnPoiType.Start)), Node(2)],
        Edges = [Edge(1, 1, 2, conditions: [Condition(13), Condition(14)])],
      };

      // Act/Assert: Either key alone is not enough.
      Assert.False(Has(Reach(graph, 13), 2));
      Assert.False(Has(Reach(graph, 14), 2));
      Assert.True(Has(Reach(graph, 13, 14), 2));
    }

    [Fact]
    public void GetReachableNodes_WithOneWayPassage_OnlyReachesForward()
    {
      // Arrange: Start in node 2; node 1 -> node 2 is one-way, node 2 -> node 3 is one-way.
      var graph = new LilGraph
      {
        Nodes = [Node(1), Node(2, Poi(StartPoi, EnPoiType.Start)), Node(3)],
        Edges =
        [
          Edge(1, 1, 2, EnPassageDirection.OneWay),
          Edge(2, 2, 3, EnPassageDirection.OneWay),
        ],
      };

      // Act: Query without keys.
      var reached = Reach(graph);

      // Assert: Forward through the one-way passage works; backward does not.
      Assert.True(Has(reached, 3));
      Assert.False(Has(reached, 1));
    }

    [Fact]
    public void GetReachableNodes_WithOnlyRouteThroughSecretNode_ReachesFarNode()
    {
      // Arrange: The normal node 3 is reachable only through the secret node 2.
      var graph = new LilGraph
      {
        Nodes =
        [
          Node(1, Poi(StartPoi, EnPoiType.Start)),
          new LilNode { Id = Id(2), RelSize = 1, MaxEdges = -1, Secret = true },
          Node(3),
        ],
        Edges =
        [
          Edge(1, 1, 2, secret: true),
          Edge(2, 2, 3),
        ],
      };

      // Act: Query without keys.
      var reached = Reach(graph);

      // Assert: Undiscovered secrets still count for walking.
      Assert.True(Has(reached, 2));
      Assert.True(Has(reached, 3));
    }

    [Fact]
    public void GetReachableNodes_WithUnrelatedHeldPois_ChangesNothing()
    {
      // Arrange: Exit behind key 13 or key 14.
      var graph = CreateEitherKeyGraph();

      // Act: Hold the exit POI and an id that exists nowhere, but no key.
      var reached = LevelGraphReach.GetReachableNodes(graph, Id(StartPoi), [Id(15), Guid.NewGuid()]);

      // Assert: Same as holding nothing.
      Assert.Equal(Reach(graph), reached);
    }
  }

  /// <summary>Tests agreement with the metadata pass on real generated graphs.</summary>
  public sealed class ConsistencyTests
  {
    /// <summary>Generates a graph through the real pipeline (metadata included).</summary>
    /// <param name="difficulty">Difficulty to generate for.</param>
    /// <param name="seed">Supporting config seed.</param>
    /// <returns>The generated graph.</returns>
    private static LilGraph Generate(int difficulty, long seed)
    {
      var propMain = new PropMain { TextSeed = "mordor", Difficulty = difficulty, ManyThemes = true };
      var config = new ConfigGenerator(propMain, seed).Execute();
      var prepData = new PrepassGenerator(config).Create();
      var world = new LilWorld();
      new GraphGenerator(config, prepData, world).Execute();
      return world.Graph;
    }

    /// <summary>Finds the identifier of the single start POI of a generated graph.</summary>
    /// <param name="graph">Generated graph.</param>
    /// <returns>Start POI identifier.</returns>
    private static Guid StartPoiId(LilGraph graph)
    {
      return graph.Nodes.SelectMany(node => node.Pois).Single(poi => poi.Type == EnPoiType.Start).Id;
    }

    [Theory]
    [InlineData(3, 1)]
    [InlineData(3, 2)]
    [InlineData(25, 3)]
    [InlineData(25, 4)]
    [InlineData(100, 5)]
    [InlineData(100, 6)]
    public void GetReachableNodes_WithNoKeys_MatchesOpenReach(int difficulty, long seed)
    {
      // Arrange: A generated graph and its start.
      var graph = Generate(difficulty, seed);
      var start = StartPoiId(graph);

      // Act: Query without keys.
      var reached = LevelGraphReach.GetReachableNodes(graph, start, []);

      // Assert: Exactly the nodes the metadata marks as openly reachable from this start.
      var expected = graph.Nodes.Where(node => node.OpenReach.Contains(start)).Select(node => node.Id);
      Assert.Equal(expected, reached);
    }

    [Theory]
    [InlineData(3, 1)]
    [InlineData(3, 2)]
    [InlineData(25, 3)]
    [InlineData(25, 4)]
    [InlineData(100, 5)]
    [InlineData(100, 6)]
    public void GetReachableNodes_WithAllSolutions_MatchesReachEntries(int difficulty, long seed)
    {
      // Arrange: A generated graph, its start and every solution in it.
      var graph = Generate(difficulty, seed);
      var start = StartPoiId(graph);
      var allSolutions = graph.Nodes.SelectMany(node => node.Pois)
        .Where(poi => poi.Type == EnPoiType.Solution).Select(poi => poi.Id).ToList();

      // Act: Query holding every solution.
      var reached = LevelGraphReach.GetReachableNodes(graph, start, allSolutions);

      // Assert: Exactly the nodes that have a reach entry for this start, i.e. all reachable ones.
      var expected = graph.Nodes.Where(node => node.Reach.ContainsKey(start)).Select(node => node.Id);
      Assert.Equal(expected, reached);
    }
  }

  /// <summary>Tests result order, purity and boundary graphs.</summary>
  public sealed class DeterminismAndBoundaryTests
  {
    [Fact]
    public void GetReachableNodes_ReturnsNodesInGraphOrder()
    {
      // Arrange: Exit behind key 13 or key 14, holding 13.
      var graph = CreateEitherKeyGraph();

      // Act: Query twice.
      var first = Reach(graph, 13);
      var second = Reach(graph, 13);

      // Assert: Graph node order, identical across calls.
      Assert.Equal([Id(1), Id(2), Id(3), Id(4)], first);
      Assert.Equal(first, second);
    }

    [Fact]
    public void GetReachableNodes_WithReversedEdgeOrder_ProducesSameResult()
    {
      // Arrange: The same graph with its edges reversed.
      var normal = CreateEitherKeyGraph();
      var reversed = CreateEitherKeyGraph();
      reversed.Edges.Reverse();

      // Act/Assert: Edge order does not influence the result.
      Assert.Equal(Reach(normal, 14), Reach(reversed, 14));
    }

    [Fact]
    public void GetReachableNodes_DoesNotModifyGraph()
    {
      // Arrange: A graph and a snapshot of its shape.
      var graph = CreateEitherKeyGraph();
      var nodeIds = graph.Nodes.Select(node => node.Id).ToList();
      var edgeIds = graph.Edges.Select(edge => edge.Id).ToList();

      // Act: Query.
      _ = Reach(graph, 13);

      // Assert: Nodes, edges and metadata are untouched.
      Assert.Equal(nodeIds, graph.Nodes.Select(node => node.Id));
      Assert.Equal(edgeIds, graph.Edges.Select(edge => edge.Id));
      Assert.All(graph.Nodes, node => Assert.Empty(node.Reach));
      Assert.All(graph.Nodes, node => Assert.Empty(node.OpenReach));
    }

    [Fact]
    public void GetReachableNodes_WithStartOnlyGraph_ReturnsStartNode()
    {
      // Arrange: A single start node.
      var graph = new LilGraph { Nodes = [Node(1, Poi(StartPoi, EnPoiType.Start))] };

      // Act/Assert: The start node is always reachable.
      Assert.Equal([Id(1)], Reach(graph));
    }
  }

  /// <summary>Tests the overload that starts from a node instead of a start POI.</summary>
  public sealed class NodeOverloadTests
  {
    /// <summary>Runs the node overload from the node with the given small test id.</summary>
    /// <param name="graph">Graph to query.</param>
    /// <param name="nodeId">Start node id as a small test integer.</param>
    /// <param name="held">Held POI ids as small test integers.</param>
    /// <returns>Reached node identifiers in result order.</returns>
    private static IReadOnlyList<Guid> ReachFromNode(LilGraph graph, int nodeId, params int[] held)
    {
      var startNode = graph.Nodes.Single(node => node.Id == Id(nodeId));
      return LevelGraphReach.GetReachableNodes(graph, startNode, held.Select(Id).ToList());
    }

    [Fact]
    public void GetReachableNodes_FromStartPoiNode_MatchesStartPoiOverload()
    {
      // Arrange: Exit behind key 13 or key 14; node 1 holds the start POI.
      var graph = CreateEitherKeyGraph();

      // Act/Assert: Starting from the start POI's node equals starting from the start POI.
      Assert.Equal(Reach(graph), ReachFromNode(graph, 1));
      Assert.Equal(Reach(graph, 13), ReachFromNode(graph, 1, 13));
      Assert.Equal(Reach(graph, 14), ReachFromNode(graph, 1, 14));
    }

    [Fact]
    public void GetReachableNodes_FromNodeWithoutStartPoi_WalksFromThatNode()
    {
      // Arrange: Exit behind key 13 (via node 2) or key 14 (via node 3); start from node 3.
      var graph = CreateEitherKeyGraph();

      // Act/Assert: Without keys the open part around node 3 is reachable, the exit is not.
      Assert.Equal([Id(1), Id(2), Id(3)], ReachFromNode(graph, 3));
      // Assert: Either key still opens the exit, now walking from node 3.
      Assert.True(Has(ReachFromNode(graph, 3, 14), 4));
      Assert.True(Has(ReachFromNode(graph, 3, 13), 4));
    }

    [Fact]
    public void GetReachableNodes_FromNodeBehindOneWayPassage_CannotGoBack()
    {
      // Arrange: Node 1 -> node 2 is one-way; start from node 2.
      var graph = new LilGraph
      {
        Nodes = [Node(1, Poi(StartPoi, EnPoiType.Start)), Node(2), Node(3)],
        Edges =
        [
          Edge(1, 1, 2, EnPassageDirection.OneWay),
          Edge(2, 2, 3),
        ],
      };

      // Act/Assert: The one-way passage cannot be walked backward.
      Assert.Equal([Id(2), Id(3)], ReachFromNode(graph, 2));
    }

    [Fact]
    public void GetReachableNodes_WithNodeFromCopiedGraph_MatchesById()
    {
      // Arrange: A node instance taken from a deep copy of the graph.
      var graph = CreateEitherKeyGraph();
      var copy = LilWorldCopy.Copy(new LilWorld { Graph = graph })!;
      var copiedNode = copy.Graph.Nodes.Single(node => node.Id == Id(3));

      // Act: Query the original graph with the copied node.
      var reached = LevelGraphReach.GetReachableNodes(graph, copiedNode, []);

      // Assert: The node is matched by identifier, not by reference.
      Assert.Equal(ReachFromNode(graph, 3), reached);
    }

    [Fact]
    public void GetReachableNodes_WithNullStartNode_ThrowsArgumentNullException()
    {
      // Act/Assert: The start node is required.
      var exception = Assert.Throws<ArgumentNullException>(
        () => LevelGraphReach.GetReachableNodes(CreateEitherKeyGraph(), (LilNode)null!, []));
      Assert.Equal("startNode", exception.ParamName);
    }

    [Fact]
    public void GetReachableNodes_WithNodeOverloadAndNullGraph_ThrowsArgumentNullException()
    {
      // Act/Assert: The graph is required.
      var exception = Assert.Throws<ArgumentNullException>(
        () => LevelGraphReach.GetReachableNodes(null!, Node(1), []));
      Assert.Equal("graph", exception.ParamName);
    }

    [Fact]
    public void GetReachableNodes_WithNodeOverloadAndNullHeldPois_ThrowsArgumentNullException()
    {
      // Arrange: A valid graph and node.
      var graph = CreateEitherKeyGraph();

      // Act/Assert: The held set is required.
      var exception = Assert.Throws<ArgumentNullException>(
        () => LevelGraphReach.GetReachableNodes(graph, graph.Nodes[0], null!));
      Assert.Equal("heldPois", exception.ParamName);
    }

    [Fact]
    public void GetReachableNodes_WithNodeNotInGraph_ThrowsArgumentException()
    {
      // Act/Assert: The start node must belong to the graph.
      var exception = Assert.Throws<ArgumentException>(
        () => LevelGraphReach.GetReachableNodes(CreateEitherKeyGraph(), Node(999), []));
      Assert.Equal("startNode", exception.ParamName);
    }
  }

  /// <summary>Tests argument validation.</summary>
  public sealed class ArgumentTests
  {
    [Fact]
    public void GetReachableNodes_WithNullGraph_ThrowsArgumentNullException()
    {
      // Act/Assert: The graph is required.
      var exception = Assert.Throws<ArgumentNullException>(
        () => LevelGraphReach.GetReachableNodes(null!, Id(StartPoi), []));
      Assert.Equal("graph", exception.ParamName);
    }

    [Fact]
    public void GetReachableNodes_WithNullHeldPois_ThrowsArgumentNullException()
    {
      // Act/Assert: The held set is required (pass an empty one for "no keys").
      var exception = Assert.Throws<ArgumentNullException>(
        () => LevelGraphReach.GetReachableNodes(CreateEitherKeyGraph(), Id(StartPoi), null!));
      Assert.Equal("heldPois", exception.ParamName);
    }

    [Theory]
    [InlineData(999)] // Exists nowhere.
    [InlineData(15)]  // Exists, but is the exit, not a start.
    public void GetReachableNodes_WithUnknownOrNonStartPoi_ThrowsArgumentException(int poiId)
    {
      // Act/Assert: The start POI must exist and be a start.
      var exception = Assert.Throws<ArgumentException>(
        () => LevelGraphReach.GetReachableNodes(CreateEitherKeyGraph(), Id(poiId), []));
      Assert.Equal("startPoiId", exception.ParamName);
    }
  }
}
