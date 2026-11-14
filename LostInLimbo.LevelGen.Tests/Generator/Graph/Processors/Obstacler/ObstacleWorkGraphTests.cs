using LostInLimbo.LevelGen.Data.Config.Graph.Proc;
using LostInLimbo.LevelGen.Data.Result.Graph;
using LostInLimbo.LevelGen.Internal.Generator.Graph.Processors.Obstacler;
using static LostInLimbo.LevelGen.Tests.Generator.Graph.Processors.Obstacler.ObstaclerTestSupport;

namespace LostInLimbo.LevelGen.Tests.Generator.Graph.Processors.Obstacler;

/// <summary>
/// Tests <see cref="ObstacleWorkGraph"/>: the index view, cut costs, hop balls and the side
/// assignment of a cut.
/// </summary>
public sealed class ObstacleWorkGraphTests
{
  /// <summary>Tests construction, work indices and start/exit lookup.</summary>
  public sealed class StructureTests
  {
    [Fact]
    public void Constructor_WithNullArguments_ThrowsArgumentNullException()
    {
      // Act/Assert: Each missing argument is rejected naming the parameter.
      Assert.Equal("graph", Assert.Throws<ArgumentNullException>(
        () => new ObstacleWorkGraph(null!, CnfProcObstaclePlacerOpt.Default)).ParamName);
      Assert.Equal("options", Assert.Throws<ArgumentNullException>(
        () => new ObstacleWorkGraph(new LilGraph(), null!)).ParamName);
    }

    [Fact]
    public void Constructor_WithEmptyGraph_HasNoNodesOrTerminals()
    {
      // Act: Index an empty graph.
      var work = new ObstacleWorkGraph(new LilGraph(), CnfProcObstaclePlacerOpt.Default);

      // Assert: Nothing to index, no start and no exit.
      Assert.Equal(0, work.NodeCount);
      Assert.Equal(0, work.EdgeCount);
      Assert.Equal(ObstacleWorkGraph.NoNode, work.StartNode);
      Assert.Equal(ObstacleWorkGraph.NoNode, work.ExitNode);
    }

    [Fact]
    public void Constructor_WithShuffledLists_IndexesByIdentifier()
    {
      // Arrange: Nodes and edges listed out of identifier order.
      var graph = new LilGraph();
      graph.Nodes.Add(GraphTestCreate.Node(3));
      graph.Nodes.Add(GraphTestCreate.Node(1));
      graph.Nodes.Add(GraphTestCreate.Node(2));
      graph.Edges.Add(GraphTestCreate.Edge(102, 1, 2));
      graph.Edges.Add(GraphTestCreate.Edge(101, 2, 3));

      // Act: Index it.
      var work = new ObstacleWorkGraph(graph, CnfProcObstaclePlacerOpt.Default);

      // Assert: Work indices follow identifiers, not list positions.
      Assert.Equal(GraphTestCreate.Id(1), work.GetNode(0).Id);
      Assert.Equal(GraphTestCreate.Id(3), work.GetNode(2).Id);
      Assert.Equal(GraphTestCreate.Id(101), work.GetEdge(0).Id);
      Assert.Equal(1, work.EdgeFrom(0));
      Assert.Equal(2, work.EdgeTo(0));
      Assert.Equal(0, work.EdgeFrom(1));
      Assert.Equal(1, work.EdgeTo(1));
    }

    [Fact]
    public void Constructor_WithSelfLoop_LeavesItOut()
    {
      // Arrange: One real passage and one self-loop.
      var work = CreateWorkGraph(2, new Passage(1, 1), new Passage(1, 2));

      // Assert: Only the real passage is part of the view.
      Assert.Equal(1, work.EdgeCount);
      Assert.Equal(GraphTestCreate.Id(FirstEdgeId + 1), work.GetEdge(0).Id);
      Assert.Equal([0], work.IncidentEdges(0));
    }

    [Fact]
    public void Constructor_WithDanglingEdge_ThrowsInvalidOperationException()
    {
      // Arrange: Passage to a node that does not exist.
      var graph = CreateGraph(2, new Passage(1, 7));

      // Act/Assert: The broken reference is reported.
      Assert.Throws<InvalidOperationException>(() => new ObstacleWorkGraph(graph, CnfProcObstaclePlacerOpt.Default));
    }

    [Fact]
    public void Constructor_WithDuplicateNodeId_ThrowsArgumentException()
    {
      // Arrange: Two nodes sharing an identifier.
      var graph = new LilGraph();
      graph.Nodes.Add(GraphTestCreate.Node(1));
      graph.Nodes.Add(GraphTestCreate.Node(1));

      // Act/Assert: The duplicate is rejected.
      Assert.Throws<ArgumentException>(() => new ObstacleWorkGraph(graph, CnfProcObstaclePlacerOpt.Default));
    }

    [Fact]
    public void IncidentEdges_WithParallelPassages_ListsEachSeparately()
    {
      // Arrange: Two parallel passages 1 - 2 and one passage 2 - 3.
      var work = CreateWorkGraph(3, new Passage(1, 2), new Passage(2, 1), new Passage(2, 3));

      // Assert: Parallels stay separate edges; the far end is found from either side.
      Assert.Equal([0, 1], work.IncidentEdges(0));
      Assert.Equal([0, 1, 2], work.IncidentEdges(1));
      Assert.Equal(1, work.OtherEnd(1, 0));
      Assert.Equal(0, work.OtherEnd(1, 1));
    }

    [Fact]
    public void IsDeadEnd_CountsDistinctOrdinaryNeighbours()
    {
      // Arrange: Node 1 has two parallel passages to node 2; node 2 also leads to node 3;
      // node 3 has a secret passage to node 4; node 5 has no passage at all.
      var work = CreateWorkGraph(
        5,
        new Passage(1, 2),
        new Passage(2, 1),
        new Passage(2, 3),
        new Passage(3, 4, Secret: true));

      // Assert: Parallel passages to one neighbour still make a dead end; two neighbours do
      // not; secret passages are not counted, so nodes 3 and 4 are dead ends as well.
      Assert.True(work.IsDeadEnd(0));
      Assert.False(work.IsDeadEnd(1));
      Assert.True(work.IsDeadEnd(2));
      Assert.True(work.IsDeadEnd(3));
      Assert.True(work.IsDeadEnd(4));
    }

    [Fact]
    public void StartAndExit_WithOneNodeEach_AreFound()
    {
      // Arrange: Start in node 2, exit in node 3.
      var graph = CreateGraph(3, Chain(3));
      graph.Nodes[1].Pois.Add(GraphTestCreate.Poi(500, EnPoiType.Start));
      graph.Nodes[2].Pois.Add(GraphTestCreate.Poi(501, EnPoiType.Exit));

      // Act: Index it.
      var work = new ObstacleWorkGraph(graph, CnfProcObstaclePlacerOpt.Default);

      // Assert: Both terminals resolve to work indices.
      Assert.Equal(1, work.StartNode);
      Assert.Equal(2, work.ExitNode);
    }

    [Fact]
    public void StartAndExit_InSameNode_ShareTheIndex()
    {
      // Arrange: Start and exit in node 1.
      var graph = CreateGraph(2, Chain(2));
      graph.Nodes[0].Pois.Add(GraphTestCreate.Poi(500, EnPoiType.Start));
      graph.Nodes[0].Pois.Add(GraphTestCreate.Poi(501, EnPoiType.Exit));

      // Act: Index it.
      var work = new ObstacleWorkGraph(graph, CnfProcObstaclePlacerOpt.Default);

      // Assert: Both terminals are the same node.
      Assert.Equal(0, work.StartNode);
      Assert.Equal(0, work.ExitNode);
    }

    [Fact]
    public void StartNode_WithStartsInTwoNodes_IsNoNode()
    {
      // Arrange: Start POIs in two different nodes, one exit.
      var graph = CreateGraph(3, Chain(3));
      graph.Nodes[0].Pois.Add(GraphTestCreate.Poi(500, EnPoiType.Start));
      graph.Nodes[1].Pois.Add(GraphTestCreate.Poi(501, EnPoiType.Start));
      graph.Nodes[2].Pois.Add(GraphTestCreate.Poi(502, EnPoiType.Exit));

      // Act: Index it.
      var work = new ObstacleWorkGraph(graph, CnfProcObstaclePlacerOpt.Default);

      // Assert: Several start nodes are unsupported; the exit is still found.
      Assert.Equal(ObstacleWorkGraph.NoNode, work.StartNode);
      Assert.Equal(2, work.ExitNode);
    }

    [Fact]
    public void StartNode_WithTwoStartPoisInOneNode_IsThatNode()
    {
      // Arrange: Two start POIs sharing a node.
      var graph = CreateGraph(2, Chain(2));
      graph.Nodes[1].Pois.Add(GraphTestCreate.Poi(500, EnPoiType.Start));
      graph.Nodes[1].Pois.Add(GraphTestCreate.Poi(501, EnPoiType.Start));

      // Act: Index it.
      var work = new ObstacleWorkGraph(graph, CnfProcObstaclePlacerOpt.Default);

      // Assert: What counts is the number of nodes, not of POIs.
      Assert.Equal(1, work.StartNode);
    }
  }

  /// <summary>Tests the hop ball around a node.</summary>
  public sealed class BallTests
  {
    [Theory]
    [InlineData(0, new[] { 2 })]
    [InlineData(1, new[] { 1, 2, 3 })]
    [InlineData(2, new[] { 0, 1, 2, 3, 4 })]
    [InlineData(9, new[] { 0, 1, 2, 3, 4 })]
    public void Ball_OnChain_HoldsNodesWithinRadius(int radius, int[] expected)
    {
      // Arrange: Chain of five nodes.
      var work = CreateWorkGraph(5, Chain(5));

      // Act: Ball around the middle node.
      var ball = work.Ball(2, radius);

      // Assert: Exactly the nodes within the radius are inside.
      var inside = new List<int>();
      for (var node = 0; node < ball.Length; node++)
      {
        if (ball[node]) inside.Add(node);
      }

      Assert.Equal(expected, inside);
    }

    [Fact]
    public void Ball_CrossesEveryKindOfPassage()
    {
      // Arrange: One-way, secret and conditioned passages around node 1; the one-way points at it.
      var work = CreateWorkGraph(
        4,
        new Passage(2, 1, OneWay: true),
        new Passage(1, 3, Secret: true),
        new Passage(1, 4, Conditions: 3));

      // Act: Ball of one hop around node 1.
      var ball = work.Ball(0, 1);

      // Assert: Hops are structural: direction, secrecy and conditions do not matter.
      Assert.Equal([true, true, true, true], ball);
    }

    [Fact]
    public void Ball_WithNegativeRadius_ThrowsArgumentOutOfRangeException()
    {
      // Arrange: Any graph.
      var work = CreateWorkGraph(2, Chain(2));

      // Act/Assert: A negative radius is rejected.
      var exception = Assert.Throws<ArgumentOutOfRangeException>(() => work.Ball(0, -1));
      Assert.Equal("radius", exception.ParamName);
    }
  }

  /// <summary>Tests the main-pass cut costs and the lockable check.</summary>
  public sealed class CutCostTests
  {
    [Fact]
    public void MainCutCost_ByPassageKind_FollowsTheCostTable()
    {
      // Arrange: One passage of every kind, all between nodes 1 and 2 (parallel).
      var work = CreateWorkGraph(
        2,
        new Passage(1, 2, Chokepoint: true),
        new Passage(1, 2),
        new Passage(1, 2, Conditions: 1),
        new Passage(1, 2, Chokepoint: true, Conditions: 2),
        new Passage(1, 2, Conditions: 3),
        new Passage(1, 2, OneWay: true),
        new Passage(1, 2, Secret: true),
        new Passage(1, 2, Secret: true, Conditions: 2),
        new Passage(1, 2, Secret: true, OneWay: true),
        new Passage(1, 2, Secret: true, Conditions: 3));

      // Act: Cost of every passage.
      var costs = work.BuildMainCutCosts();

      // Assert: Chokepoint cheaper than open, stacking adds up, uncuttable passages stand out,
      // secrets are free unless they cannot be locked at all.
      long[] expected =
      [
        ObstacleWorkGraph.CostChokepoint,
        ObstacleWorkGraph.CostOpen,
        ObstacleWorkGraph.CostOpen + ObstacleWorkGraph.CostStack,
        ObstacleWorkGraph.CostChokepoint + 2 * ObstacleWorkGraph.CostStack,
        ObstacleWorkGraph.CostUncuttable,
        ObstacleWorkGraph.CostUncuttable,
        ObstacleWorkGraph.CostSecret,
        ObstacleWorkGraph.CostSecret,
        ObstacleWorkGraph.CostUncuttable,
        ObstacleWorkGraph.CostUncuttable,
      ];
      Assert.Equal(expected, costs);
      for (var edge = 0; edge < expected.Length; edge++)
      {
        Assert.Equal(expected[edge], work.MainCutCost(edge));
        Assert.Equal(expected[edge] < ObstacleWorkGraph.CostUncuttable, work.IsLockable(edge));
      }
    }

    [Fact]
    public void CostConstants_KeepTheDocumentedOrder()
    {
      // Assert: A chokepoint beats an open passage, one open passage beats two chokepoints,
      // and no realistic sum of costs reaches the uncuttable mark.
      Assert.True(ObstacleWorkGraph.CostChokepoint < ObstacleWorkGraph.CostOpen);
      Assert.True(ObstacleWorkGraph.CostOpen < 2 * ObstacleWorkGraph.CostChokepoint);
      Assert.True(ObstacleWorkGraph.CostStack > 0);
      Assert.Equal(0, ObstacleWorkGraph.CostSecret);
      Assert.True(ObstacleWorkGraph.CostUncuttable > 1_000_000 * ObstacleWorkGraph.CostOpen);
    }

    [Fact]
    public void MainCutCost_WithLowerConditionCap_SaturatesEarlier()
    {
      // Arrange: Passage with one condition under a cap of one condition per passage.
      var graph = CreateGraph(2, new Passage(1, 2, Conditions: 1));
      var options = CnfProcObstaclePlacerOpt.Default with { MaxConditionsPerEdge = 1 };

      // Act: Index it.
      var work = new ObstacleWorkGraph(graph, options);

      // Assert: The passage is already full.
      Assert.False(work.IsLockable(0));
      Assert.Equal(ObstacleWorkGraph.CostUncuttable, work.MainCutCost(0));
    }

    [Fact]
    public void MainCutCost_AfterConditionIsAdded_ReadsItLive()
    {
      // Arrange: Open passage, indexed while it has no condition.
      var graph = CreateGraph(2, new Passage(1, 2));
      var work = new ObstacleWorkGraph(graph, CnfProcObstaclePlacerOpt.Default);
      var before = work.MainCutCost(0);

      // Act: Add a condition after indexing.
      graph.Edges[0].Conditions.Add(GraphTestCreate.Condition(900));

      // Assert: The cost follows without rebuilding the view.
      Assert.Equal(ObstacleWorkGraph.CostOpen, before);
      Assert.Equal(ObstacleWorkGraph.CostOpen + ObstacleWorkGraph.CostStack, work.MainCutCost(0));
    }
  }

  /// <summary>Tests the side assignment that turns cut passages into a candidate.</summary>
  public sealed class BuildCandidateTests
  {
    [Fact]
    public void BuildCandidate_OnChain_SplitsAtTheCut()
    {
      // Arrange: Chain 1 - 2 - 3 - 4, cut between 2 and 3.
      var work = CreateWorkGraph(4, Chain(4));
      var costs = work.BuildMainCutCosts();

      // Act: Keep node 4 away from node 1.
      var candidate = work.BuildCandidate(0, 3, [1], costs);

      // Assert: Everything behind the cut is far; one ordinary passage is locked.
      Assert.NotNull(candidate);
      Assert.Equal(3, candidate.Target);
      Assert.Equal([1], candidate.LockEdges);
      Assert.Equal([2, 3], candidate.FarNodes);
      Assert.Equal(1, candidate.OrdinaryWidth);
      Assert.Equal(ObstacleWorkGraph.CostOpen, candidate.Cost);
      Assert.Equal(0, candidate.MaxConditions);
    }

    [Fact]
    public void BuildCandidate_WhenEdgesDoNotSeparate_ReturnsNull()
    {
      // Arrange: Cycle 1 - 2 - 3 - 4 - 1 with only one of the two ways cut.
      var work = CreateWorkGraph(4, new Passage(1, 2), new Passage(2, 3), new Passage(3, 4), new Passage(4, 1));
      var costs = work.BuildMainCutCosts();

      // Act/Assert: The target is still connected the other way round.
      Assert.Null(work.BuildCandidate(0, 2, [1], costs));
    }

    [Fact]
    public void BuildCandidate_WithSameSourceAndTarget_ReturnsNull()
    {
      // Arrange: Any graph.
      var work = CreateWorkGraph(2, Chain(2));

      // Act/Assert: A node cannot be cut from itself.
      Assert.Null(work.BuildCandidate(0, 0, [0], work.BuildMainCutCosts()));
    }

    [Fact]
    public void BuildCandidate_WithUnconnectedTarget_ReturnsNull()
    {
      // Arrange: Node 3 has no passage at all.
      var work = CreateWorkGraph(3, new Passage(1, 2));

      // Act/Assert: Nothing to lock, so there is no candidate.
      Assert.Null(work.BuildCandidate(0, 2, [], work.BuildMainCutCosts()));
    }

    [Fact]
    public void BuildCandidate_WithSecretRouteAroundTheCut_LocksTheSecretPassageToo()
    {
      // Arrange: Chain 1 - 2 - 3 plus secret node 4 bridging 1 and 3.
      var work = CreateWorkGraph(
        4,
        new Passage(1, 2),
        new Passage(2, 3),
        new Passage(1, 4, Secret: true),
        new Passage(4, 3, Secret: true));
      var costs = work.BuildMainCutCosts();

      // Act: Cut between 2 and 3.
      var candidate = work.BuildCandidate(0, 2, [1], costs);

      // Assert: The secret node joins the start side; its passage into the far side is locked
      // but counts neither as width nor as cost.
      Assert.NotNull(candidate);
      Assert.Equal([1, 3], candidate.LockEdges);
      Assert.Equal([2], candidate.FarNodes);
      Assert.Equal(1, candidate.OrdinaryWidth);
      Assert.Equal(ObstacleWorkGraph.CostOpen, candidate.Cost);
    }

    [Fact]
    public void BuildCandidate_WithSecretLeafOnFarNode_KeepsItFarAndUnlocked()
    {
      // Arrange: Chain 1 - 2 - 3 plus secret leaf 4 hanging off node 3.
      var work = CreateWorkGraph(4, new Passage(1, 2), new Passage(2, 3), new Passage(3, 4, Secret: true));
      var costs = work.BuildMainCutCosts();

      // Act: Cut between 1 and 2.
      var candidate = work.BuildCandidate(0, 2, [0], costs);

      // Assert: The leaf stays with its host behind the cut; its own passage is not locked.
      Assert.NotNull(candidate);
      Assert.Equal([0], candidate.LockEdges);
      Assert.Equal([1, 2, 3], candidate.FarNodes);
    }

    [Fact]
    public void BuildCandidate_WithSecretLeafOnStartSide_LeavesItAlone()
    {
      // Arrange: Chain 1 - 2 - 3 plus secret leaf 4 hanging off node 1.
      var work = CreateWorkGraph(4, new Passage(1, 2), new Passage(2, 3), new Passage(1, 4, Secret: true));
      var costs = work.BuildMainCutCosts();

      // Act: Cut between 2 and 3.
      var candidate = work.BuildCandidate(0, 2, [1], costs);

      // Assert: The leaf is on the start side; only the cut passage is locked.
      Assert.NotNull(candidate);
      Assert.Equal([1], candidate.LockEdges);
      Assert.Equal([2], candidate.FarNodes);
    }

    [Fact]
    public void BuildCandidate_WithSuperfluousCutEdge_DropsIt()
    {
      // Arrange: Chain 1 - 2 - 3 plus leaf 4 off node 1; the cut list also names the leaf's passage.
      var work = CreateWorkGraph(4, new Passage(1, 2), new Passage(2, 3), new Passage(1, 4));
      var costs = work.BuildMainCutCosts();

      // Act: Keep node 3 away from node 1.
      var candidate = work.BuildCandidate(0, 2, [1, 2], costs);

      // Assert: Locking the leaf would not help, so it stays open and on the start side.
      Assert.NotNull(candidate);
      Assert.Equal([1], candidate.LockEdges);
      Assert.Equal([2], candidate.FarNodes);
    }

    [Fact]
    public void BuildCandidate_WithParallelPassagesInTheCut_LocksAllOfThem()
    {
      // Arrange: Node 1 and node 2 joined by two parallel passages, one a chokepoint.
      var work = CreateWorkGraph(2, new Passage(1, 2, Chokepoint: true), new Passage(1, 2));
      var costs = work.BuildMainCutCosts();

      // Act: Cut the two nodes apart.
      var candidate = work.BuildCandidate(0, 1, [0, 1], costs);

      // Assert: Both passages are locked and both count.
      Assert.NotNull(candidate);
      Assert.Equal([0, 1], candidate.LockEdges);
      Assert.Equal(2, candidate.OrdinaryWidth);
      Assert.Equal(ObstacleWorkGraph.CostChokepoint + ObstacleWorkGraph.CostOpen, candidate.Cost);
    }

    [Fact]
    public void BuildCandidate_WithStackedPassage_ReportsHighestConditionCount()
    {
      // Arrange: Chain 1 - 2 - 3 whose second passage already has two conditions.
      var work = CreateWorkGraph(3, new Passage(1, 2), new Passage(2, 3, Conditions: 2));
      var costs = work.BuildMainCutCosts();

      // Act: Cut at the stacked passage.
      var candidate = work.BuildCandidate(0, 2, [1], costs);

      // Assert: The candidate knows it stacks.
      Assert.NotNull(candidate);
      Assert.Equal(2, candidate.MaxConditions);
      Assert.Equal(ObstacleWorkGraph.CostOpen + 2 * ObstacleWorkGraph.CostStack, candidate.Cost);
    }

    [Fact]
    public void BuildCandidate_WhenUnlockablePassageCrosses_ReturnsNull()
    {
      // Arrange: Chain 1 - 2 - 3 plus a one-way passage 1 -> 3, declared free by custom costs.
      var work = CreateWorkGraph(3, new Passage(1, 2), new Passage(2, 3), new Passage(1, 3, OneWay: true));
      long[] costs = [ObstacleWorkGraph.CostOpen, ObstacleWorkGraph.CostOpen, 0];

      // Act/Assert: The one-way passage would have to be locked, which is not allowed.
      Assert.Null(work.BuildCandidate(0, 2, [1], costs));
    }

    [Fact]
    public void BuildCandidate_WithBadArguments_Throws()
    {
      // Arrange: Any graph.
      var work = CreateWorkGraph(2, Chain(2));

      // Act/Assert: Missing or mis-sized arguments are rejected.
      Assert.Throws<ArgumentNullException>(() => work.BuildCandidate(0, 1, null!, [1]));
      Assert.Throws<ArgumentNullException>(() => work.BuildCandidate(0, 1, [0], null!));
      Assert.Throws<ArgumentException>(() => work.BuildCandidate(0, 1, [0], [1, 1]));
    }
  }
}
