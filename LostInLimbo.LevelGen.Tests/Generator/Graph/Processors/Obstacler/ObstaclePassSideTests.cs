using LostInLimbo.LevelGen.Data.Config.Graph.Proc;
using LostInLimbo.LevelGen.Data.Result.Graph;
using LostInLimbo.LevelGen.Internal.Generator.Graph.Meta;
using LostInLimbo.LevelGen.Internal.Generator.Graph.Processors.Obstacler;
using static LostInLimbo.LevelGen.Tests.Generator.Graph.Processors.Obstacler.ObstaclerTestSupport;

namespace LostInLimbo.LevelGen.Tests.Generator.Graph.Processors.Obstacler;

/// <summary>
/// Tests <see cref="ObstaclePassSide"/>: finding pockets around treasure nodes and locking them.
/// </summary>
/// <remarks>
/// Test graphs number their nodes from 1; the work index of a node is its number minus one,
/// the work index of a passage is its position in the passage list.
/// </remarks>
public sealed class ObstaclePassSideTests
{
  /// <summary>Number of seeds used by tests that depend on rolls.</summary>
  private const int Seeds = 40;

  /// <summary>Creates the side pass of a graph.</summary>
  /// <param name="graph">Graph that receives obstacles.</param>
  /// <param name="seed">Graph sub-seed.</param>
  /// <param name="options">Options; defaults when null.</param>
  /// <returns>The pass.</returns>
  private static ObstaclePassSide CreatePass(LilGraph graph, long seed = 42, CnfProcObstaclePlacerOpt? options = null)
  {
    return new ObstaclePassSide(new ObstacleState(graph, options ?? CnfProcObstaclePlacerOpt.Default, seed));
  }

  /// <summary>
  /// Chain 1 - 2 - 3 from start to exit with a branch on node 2: node 4 leading to the leaves
  /// 5 and 6. Edge 2 joins 2 and 4, edges 3 and 4 the leaves.
  /// </summary>
  /// <returns>The graph, without treasures.</returns>
  private static LilGraph CreateBranchLevel()
  {
    return CreateLevel(
      6, 1, 3,
      new Passage(1, 2), new Passage(2, 3), new Passage(2, 4), new Passage(4, 5), new Passage(4, 6));
  }

  /// <summary>Tests which pockets the scan lists.</summary>
  public sealed class ScanTests
  {
    [Fact]
    public void Constructor_WithNullState_ThrowsArgumentNullException()
    {
      // Act/Assert: The missing state is rejected.
      Assert.Equal("state", Assert.Throws<ArgumentNullException>(() => new ObstaclePassSide(null!)).ParamName);
    }

    [Fact]
    public void Scan_WithoutTreasures_ListsNothing()
    {
      // Arrange: Dead ends exist, but nothing in them is worth guarding.
      var pass = CreatePass(CreateBranchLevel());

      // Act/Assert: No pocket is listed.
      Assert.Empty(pass.Scan());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Scan_WithGoalLeaf_ListsTheLeaf(bool optional)
    {
      // Arrange: Chain 1 - 2 - 3 with a goal leaf 4 on node 2.
      var graph = CreateLevel(4, 1, 3, new Passage(1, 2), new Passage(2, 3), new Passage(2, 4));
      AddGoal(graph, 4, optional);

      // Act: Scan.
      var pocket = Assert.Single(CreatePass(graph).Scan());

      // Assert: The pocket is the leaf, entered from node 2 through edge 2.
      Assert.Equal(3, pocket.GateNode);
      Assert.Equal(1, pocket.HostNode);
      Assert.Equal([3], pocket.Nodes);
      Assert.Equal([2], pocket.PairEdges);
      Assert.Equal([2], pocket.EntryEdges);
      Assert.True(pocket.Contains(3));
      Assert.False(pocket.Contains(1));
    }

    [Fact]
    public void Scan_WithSecretLeaf_ListsItBehindItsSecretPassage()
    {
      // Arrange: Chain 1 - 2 - 3 with a secret leaf 4 on node 2.
      var graph = CreateLevel(4, 1, 3, new Passage(1, 2), new Passage(2, 3), new Passage(2, 4, Secret: true));
      MarkSecret(graph, 4);

      // Act/Assert: A secret node is a treasure of its own.
      var pocket = Assert.Single(CreatePass(graph).Scan());
      Assert.Equal([3], pocket.Nodes);
      Assert.Equal([2], pocket.EntryEdges);
    }

    [Fact]
    public void Scan_WithTreasureDeepInBranch_ListsSmallestOrLargestPocket()
    {
      var sizes = new HashSet<int>();
      for (var seed = 0; seed < Seeds; seed++)
      {
        // Arrange: The goal is in leaf 5 at the end of the branch.
        var graph = CreateBranchLevel();
        AddGoal(graph, 5);

        // Act: Scan.
        var pocket = Assert.Single(CreatePass(graph, seed).Scan());

        // Assert: Either the leaf alone or the whole branch.
        Assert.True(pocket.Contains(4));
        if (pocket.Nodes.Count == 1) Assert.Equal([3], pocket.EntryEdges);
        else
        {
          Assert.Equal([3, 4, 5], pocket.Nodes);
          Assert.Equal([2], pocket.EntryEdges);
        }
        sizes.Add(pocket.Nodes.Count);
      }

      // Assert: Both choices occur.
      Assert.Equal(2, sizes.Count);
    }

    [Fact]
    public void Scan_WithSeveralTreasures_NeverListsOverlappingPockets()
    {
      for (var seed = 0; seed < Seeds; seed++)
      {
        // Arrange: Goals in both leaves and in the branch node itself.
        var graph = CreateBranchLevel();
        AddGoal(graph, 4);
        AddGoal(graph, 5);
        AddGoal(graph, 6, optional: true);

        // Act: Scan.
        var pockets = CreatePass(graph, seed).Scan();

        // Assert: No node is in two pockets; the branch node is only covered by the whole branch.
        var seen = new HashSet<int>();
        foreach (var pocket in pockets)
        {
          foreach (var node in pocket.Nodes) Assert.True(seen.Add(node), $"seed {seed}");
        }

        Assert.InRange(pockets.Count, 1, 2);
      }
    }

    [Fact]
    public void Scan_WithParallelPassages_TreatsThemAsOneEntry()
    {
      // Arrange: Goal leaf 4 hangs on node 2 by two passages.
      var graph = CreateLevel(4, 1, 3, new Passage(1, 2), new Passage(2, 3), new Passage(2, 4), new Passage(4, 2));
      AddGoal(graph, 4);

      // Act/Assert: Still a dead end; both passages are entries.
      var pocket = Assert.Single(CreatePass(graph).Scan());
      Assert.Equal([2, 3], pocket.EntryEdges);
    }

    [Fact]
    public void Scan_WithOneWaySecretLoop_ListsOnlyTheEntry()
    {
      // Arrange: Secret node 4 reached from node 2 and left back to node 2, one way each.
      var graph = CreateLevel(
        4, 1, 3,
        new Passage(1, 2), new Passage(2, 3),
        new Passage(2, 4, Secret: true, OneWay: true), new Passage(4, 2, Secret: true, OneWay: true));
      MarkSecret(graph, 4);

      // Act/Assert: Both passages belong to the pair, only the one pointing in is an entry.
      var pocket = Assert.Single(CreatePass(graph).Scan());
      Assert.Equal([2, 3], pocket.PairEdges);
      Assert.Equal([2], pocket.EntryEdges);
    }

    [Fact]
    public void Scan_WithTwoWaysIntoTreasure_ListsNothing()
    {
      // Arrange: Goal node 4 is joined to nodes 2 and 3 - not a dead end.
      var graph = CreateLevel(4, 1, 3, new Passage(1, 2), new Passage(2, 3), new Passage(2, 4), new Passage(3, 4));
      AddGoal(graph, 4);

      // Act/Assert: No pocket.
      Assert.Empty(CreatePass(graph).Scan());
    }

    [Fact]
    public void Scan_WithInvalidPockets_ListsNothing()
    {
      // Arrange: Goal next to the exit at the chain end - the only pocket around it holds the exit.
      var besideExit = CreateLevel(4, 1, 4, new Passage(1, 2), new Passage(2, 3), new Passage(3, 4), new Passage(3, 4));
      AddGoal(besideExit, 4);

      // Arrange: Goal leaf whose passage already carries a condition.
      var conditioned = CreateLevel(4, 1, 3, new Passage(1, 2), new Passage(2, 3), new Passage(2, 4, Conditions: 1));
      AddGoal(conditioned, 4);

      // Arrange: Goal leaf that already holds a solution.
      var withSolution = CreateLevel(4, 1, 3, new Passage(1, 2), new Passage(2, 3), new Passage(2, 4));
      AddGoal(withSolution, 4);
      withSolution.Nodes[3].Pois.Add(GraphTestCreate.Poi(9000, EnPoiType.Solution));

      // Arrange: Goal leaf behind more parallel passages than one obstacle may lock.
      var tooWide = CreateLevel(
        4, 1, 3,
        new Passage(1, 2), new Passage(2, 3),
        new Passage(2, 4), new Passage(2, 4), new Passage(2, 4), new Passage(2, 4), new Passage(2, 4));
      AddGoal(tooWide, 4);

      // Arrange: Goal leaf that can only be left, never entered.
      var exitOnly = CreateLevel(4, 1, 3, new Passage(1, 2), new Passage(2, 3), new Passage(4, 2, OneWay: true));
      AddGoal(exitOnly, 4);

      // Act/Assert: None of them yields a pocket.
      Assert.Empty(CreatePass(besideExit).Scan());
      Assert.Empty(CreatePass(conditioned).Scan());
      Assert.Empty(CreatePass(withSolution).Scan());
      Assert.Empty(CreatePass(tooWide).Scan());
      Assert.Empty(CreatePass(exitOnly).Scan());
    }

    [Fact]
    public void Scan_WithStartInDeadEnd_NeverListsMostOfTheGraph()
    {
      for (var seed = 0; seed < Seeds; seed++)
      {
        // Arrange: Start and exit share leaf 1 of a chain; the goal is at the far end.
        var graph = CreateLevel(5, 1, 1, Chain(5));
        AddGoal(graph, 5);

        // Act: Scan.
        var pocket = Assert.Single(CreatePass(graph, seed).Scan());

        // Assert: Pockets up to half of the graph only: {5} or {4, 5}, never {3, 4, 5} or more.
        Assert.InRange(pocket.Nodes.Count, 1, 2);
      }
    }

    [Fact]
    public void Scan_CalledTwice_ReturnsTheSameList()
    {
      // Arrange: A level with two possible pockets around the treasure.
      var graph = CreateBranchLevel();
      AddGoal(graph, 5);
      var pass = CreatePass(graph);

      // Act/Assert: The second call does not roll again.
      Assert.Same(pass.Scan(), pass.Scan());
    }

    [Fact]
    public void BuildNodeMask_FlagsOnlyTheFirstPockets()
    {
      // Arrange: Two goal leaves 4 and 5 on node 2.
      var graph = CreateLevel(5, 1, 3, new Passage(1, 2), new Passage(2, 3), new Passage(2, 4), new Passage(2, 5));
      AddGoal(graph, 4);
      AddGoal(graph, 5);
      var pass = CreatePass(graph);
      var pockets = pass.Scan();

      // Act: Masks for no pocket, the first one and more than exist.
      var none = pass.BuildNodeMask(0);
      var first = pass.BuildNodeMask(1);
      var all = pass.BuildNodeMask(10);

      // Assert: Exactly the nodes of the requested pockets are flagged.
      Assert.Equal(2, pockets.Count);
      Assert.DoesNotContain(true, none);
      Assert.Equal(1, first.Count(flag => flag));
      Assert.True(first[pockets[0].GateNode]);
      Assert.Equal([false, false, false, true, true], all);
    }
  }

  /// <summary>Tests locking the listed pockets.</summary>
  public sealed class ExecuteTests
  {
    [Fact]
    public void Execute_LocksEveryPocketWithSolutionsOutsideAllPockets()
    {
      for (var seed = 0; seed < Seeds; seed++)
      {
        // Arrange: Goal leaves 4, 5 and 6 on node 2 of the chain 1 - 2 - 3.
        var graph = CreateLevel(
          6, 1, 3,
          new Passage(1, 2), new Passage(2, 3), new Passage(2, 4), new Passage(2, 5), new Passage(2, 6));
        AddGoal(graph, 4);
        AddGoal(graph, 5);
        AddGoal(graph, 6);

        // Act: Budget for all of them.
        var placed = CreatePass(graph, seed).Execute(5);

        // Assert: One obstacle per leaf, three distinct solutions, none inside a leaf.
        Assert.Equal(3, placed);
        var required = new HashSet<Guid>();
        for (var edge = 2; edge <= 4; edge++) required.Add(Assert.Single(graph.Edges[edge].Conditions).RequiresPoi);
        Assert.Equal(3, required.Count);
        for (var leaf = 4; leaf <= 6; leaf++) Assert.False(graph.Nodes[leaf - 1].HasPoi(EnPoiType.Solution));
        Assert.Empty(graph.Edges[0].Conditions);
        Assert.Empty(graph.Edges[1].Conditions);
        GraphTestAssert.AssertBeatable(graph, $"seed {seed}");
      }
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(-1, 0)]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    public void Execute_RespectsTheBudget(int budget, int expected)
    {
      // Arrange: Two goal leaves.
      var graph = CreateLevel(5, 1, 3, new Passage(1, 2), new Passage(2, 3), new Passage(2, 4), new Passage(2, 5));
      AddGoal(graph, 4);
      AddGoal(graph, 5);

      // Act: Run with the budget.
      var placed = CreatePass(graph).Execute(budget);

      // Assert: Never more obstacles than the budget.
      Assert.Equal(expected, placed);
      Assert.Equal(expected, graph.Edges.Count(edge => edge.Conditions.Count > 0));
    }

    [Fact]
    public void Execute_WithParallelPassages_LocksAllWithOneSolution()
    {
      // Arrange: Goal leaf 4 behind two passages.
      var graph = CreateLevel(4, 1, 3, new Passage(1, 2), new Passage(2, 3), new Passage(2, 4), new Passage(4, 2));
      AddGoal(graph, 4);

      // Act: One obstacle.
      Assert.Equal(1, CreatePass(graph).Execute(1));

      // Assert: The same solution on both passages.
      var first = Assert.Single(graph.Edges[2].Conditions);
      var second = Assert.Single(graph.Edges[3].Conditions);
      Assert.Equal(first.RequiresPoi, second.RequiresPoi);
      GraphTestAssert.AssertBeatable(graph);
    }

    [Fact]
    public void Execute_WithOneWaySecretLoop_LocksOnlyTheEntry()
    {
      // Arrange: Secret node 4 with a one-way entry from node 2 and a one-way way back.
      var graph = CreateLevel(
        4, 1, 3,
        new Passage(1, 2), new Passage(2, 3),
        new Passage(2, 4, Secret: true, OneWay: true), new Passage(4, 2, Secret: true, OneWay: true));
      MarkSecret(graph, 4);

      // Act: One obstacle.
      Assert.Equal(1, CreatePass(graph).Execute(1));

      // Assert: The entry is locked, the way out stays open, and nothing softlocks.
      Assert.Single(graph.Edges[2].Conditions);
      Assert.Empty(graph.Edges[3].Conditions);
      GraphTestAssert.AssertBeatable(graph);
    }

    [Fact]
    public void Execute_WithPocketSpoiledAfterScan_SkipsIt()
    {
      // Arrange: Two goal leaves; after the scan one leaf passage gets a condition from elsewhere.
      var graph = CreateLevel(5, 1, 3, new Passage(1, 2), new Passage(2, 3), new Passage(2, 4), new Passage(2, 5));
      AddGoal(graph, 4);
      AddGoal(graph, 5);
      var pass = CreatePass(graph);
      Assert.Equal(2, pass.Scan().Count);
      graph.Nodes[0].Pois.Add(GraphTestCreate.Poi(9000, EnPoiType.Solution));
      graph.Edges[2].Conditions.Add(GraphTestCreate.Condition(9000));

      // Act: Budget for both.
      var placed = pass.Execute(2);

      // Assert: Only the untouched leaf is locked; the spoiled one keeps its single condition.
      Assert.Equal(1, placed);
      Assert.Single(graph.Edges[2].Conditions);
      Assert.Single(graph.Edges[3].Conditions);
      GraphTestAssert.AssertBeatable(graph);
    }

    [Fact]
    public void Execute_WhenOnlyPocketsCouldHoldTheSolution_PlacesNothing()
    {
      // Arrange: Start and exit share the secret node 2; goal leaf 3 is the only pocket.
      var graph = CreateLevel(3, 2, 2, new Passage(1, 2, Secret: true), new Passage(2, 3));
      MarkSecret(graph, 2);
      AddGoal(graph, 3);

      // Act: The only node outside the pocket is secret and cannot hold a solution.
      var placed = CreatePass(graph).Execute(1);

      // Assert: The pocket stays open and the graph is untouched.
      Assert.Equal(0, placed);
      Assert.All(graph.Edges, edge => Assert.Empty(edge.Conditions));
    }

    [Fact]
    public void Execute_WithSameSeed_ProducesIdenticalGraphs()
    {
      // Arrange: Two equal levels with a treasure deep in the branch.
      var first = CreateBranchLevel();
      var second = CreateBranchLevel();
      AddGoal(first, 5);
      AddGoal(second, 5);

      // Act: Same seed on both.
      CreatePass(first, 7).Execute(2);
      CreatePass(second, 7).Execute(2);

      // Assert: Same obstacles.
      GraphTestAssert.AssertEqualGraphs(first, second);
    }

    [Fact]
    public void Execute_WhenCancelled_ThrowsOperationCanceledException()
    {
      // Arrange: A pocket to lock and a token cancelled up front.
      var graph = CreateLevel(4, 1, 3, new Passage(1, 2), new Passage(2, 3), new Passage(2, 4));
      AddGoal(graph, 4);
      using var source = new CancellationTokenSource();
      source.Cancel();

      // Act/Assert: Cancellation is respected and nothing was placed.
      Assert.Throws<OperationCanceledException>(() => CreatePass(graph).Execute(1, source.Token));
      Assert.All(graph.Edges, edge => Assert.Empty(edge.Conditions));
    }
  }
}
