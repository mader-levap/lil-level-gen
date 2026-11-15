using LostInLimbo.LevelGen.Data.Config.Graph.Proc;
using LostInLimbo.LevelGen.Data.Result.Graph;
using LostInLimbo.LevelGen.Internal.Generator.Graph.Meta;
using LostInLimbo.LevelGen.Internal.Generator.Graph.Processors.Obstacler;
using static LostInLimbo.LevelGen.Tests.Generator.Graph.Processors.Obstacler.ObstaclerTestSupport;

namespace LostInLimbo.LevelGen.Tests.Generator.Graph.Processors.Obstacler;

/// <summary>
/// Tests <see cref="ProcObstaclePlacer"/> and, through it, the main pass
/// (<see cref="ObstaclePassMain"/>) on hand-made graphs. The graph validator and the reach
/// metadata serve as independent oracles.
/// </summary>
public sealed class ProcObstaclePlacerTests
{
  #region Helpers

  /// <summary>Number of seeds used by the sweep tests.</summary>
  private const int SweepSeeds = 40;

  /// <summary>Default options without bypasses: every passage of a cut stays locked.</summary>
  private static readonly CnfProcObstaclePlacerOpt NoBypass =
    CnfProcObstaclePlacerOpt.Default with { ChanceSecretBypass = 0f, ChanceBypass = 0f };

  /// <summary>Runs the placer on a graph.</summary>
  /// <param name="graph">Graph to change in place.</param>
  /// <param name="complexity">Largest number of obstacles.</param>
  /// <param name="seed">Graph sub-seed.</param>
  /// <param name="options">Options; defaults when null.</param>
  /// <returns>The placer, after <see cref="ProcObstaclePlacer.Execute"/>.</returns>
  private static ProcObstaclePlacer Run(LilGraph graph, int complexity, long seed, CnfProcObstaclePlacerOpt? options = null)
  {
    var placer = new ProcObstaclePlacer(graph, options ?? CnfProcObstaclePlacerOpt.Default, complexity, seed);
    placer.Execute();
    return placer;
  }

  /// <summary>Lists the identifiers of all solution POIs in a graph.</summary>
  /// <param name="graph">Graph to scan.</param>
  /// <returns>Solution identifiers in node list order.</returns>
  private static List<Guid> SolutionIds(LilGraph graph)
  {
    var ids = new List<Guid>();
    foreach (var node in graph.Nodes)
    {
      foreach (var poi in node.Pois)
      {
        if (poi.Type == EnPoiType.Solution) ids.Add(poi.Id);
      }
    }

    return ids;
  }

  /// <summary>Lists the passages that require a given solution.</summary>
  /// <param name="graph">Graph to scan.</param>
  /// <param name="solutionId">Solution to look for.</param>
  /// <returns>Passages carrying a condition on that solution, in edge list order.</returns>
  private static List<LilEdge> EdgesRequiring(LilGraph graph, Guid solutionId)
  {
    var edges = new List<LilEdge>();
    foreach (var edge in graph.Edges)
    {
      if (edge.Conditions.Exists(condition => condition.RequiresPoi == solutionId)) edges.Add(edge);
    }

    return edges;
  }

  /// <summary>Counts all conditions in a graph.</summary>
  /// <param name="graph">Graph to scan.</param>
  /// <returns>Total number of conditions on all passages.</returns>
  private static int CountConditions(LilGraph graph)
  {
    var count = 0;
    foreach (var edge in graph.Edges) count += edge.Conditions.Count;
    return count;
  }

  /// <summary>
  /// Asserts everything that must hold after any run: limits, consistency and no softlock.
  /// </summary>
  /// <param name="graph">Graph after the run; must have held no obstacles before it.</param>
  /// <param name="placer">Placer that ran.</param>
  /// <param name="complexity">Complexity the placer ran with.</param>
  /// <param name="options">Options the placer ran with.</param>
  /// <param name="context">Text naming the case, for failure messages.</param>
  private static void AssertInvariants(
    LilGraph graph,
    ProcObstaclePlacer placer,
    int complexity,
    CnfProcObstaclePlacerOpt options,
    string context)
  {
    var solutions = SolutionIds(graph);

    // Assert: One solution per obstacle, never more than Complexity.
    Assert.True(placer.PlacedObstacles <= complexity, context);
    Assert.Equal(placer.PlacedObstacles, solutions.Count);
    Assert.Equal(placer.PlacedObstacles, placer.PlacedMainObstacles + placer.PlacedSideObstacles);
    Assert.Equal(solutions.Count, new HashSet<Guid>(solutions).Count);

    // Assert: Per passage - cap respected, only known solutions, none twice; a one-way passage
    // can only be locked once, as the entry of a side pocket.
    foreach (var edge in graph.Edges)
    {
      Assert.True(edge.Conditions.Count <= options.MaxConditionsPerEdge, context);
      if (edge.Direction == EnPassageDirection.OneWay) Assert.True(edge.Conditions.Count <= 1, context);

      var seen = new HashSet<Guid>();
      foreach (var condition in edge.Conditions)
      {
        Assert.Equal(EnConditionType.ReqSolution, condition.Type);
        Assert.Contains(condition.RequiresPoi, solutions);
        Assert.True(seen.Add(condition.RequiresPoi), context);
      }
    }

    // Assert: Per solution - it locks something, not too many ordinary passages, and it does
    // not lie in a secret node.
    foreach (var solutionId in solutions)
    {
      var locked = EdgesRequiring(graph, solutionId);
      Assert.NotEmpty(locked);
      Assert.True(locked.FindAll(edge => !edge.Secret).Count <= options.MaxEdgesPerObstacle, context);
    }

    foreach (var node in graph.Nodes)
    {
      if (node.Secret) Assert.False(node.HasPoi(EnPoiType.Solution), context);
    }

    // Assert: No softlock by the validator's rules ...
    GraphTestAssert.AssertBeatable(graph, context);

    // ... and by the stricter rule: every solution is reachable without itself, without
    // secret passages and without one-way passages.
    var startNode = graph.Nodes.Find(node => node.HasPoi(EnPoiType.Start))!;
    var flood = new GraphFlood(graph, edge => !edge.Secret && edge.Direction == EnPassageDirection.Bidirectional);
    var collectible = new HashSet<Guid>(solutions);
    foreach (var solutionId in solutions)
    {
      var holder = graph.Nodes.Find(node => node.Pois.Exists(poi => poi.Id == solutionId))!;
      var (reached, _) = flood.Run(startNode.Id, [], collectible, solutionId);
      Assert.True(reached.Contains(holder.Id), context);
    }
  }

  /// <summary>
  /// Asserts that every main pass solution is unavoidable on the way to the exit, using the
  /// reach metadata. Side pass solutions guard dead ends and are usually not needed.
  /// </summary>
  /// <param name="graph">Graph after the run.</param>
  /// <param name="placer">Placer that ran.</param>
  /// <param name="context">Text naming the case, for failure messages.</param>
  private static void AssertMainSolutionsMandatoryForExit(LilGraph graph, ProcObstaclePlacer placer, string context)
  {
    new GraphMetadata(graph).Generate();
    var exitNode = graph.Nodes.Find(node => node.HasPoi(EnPoiType.Exit))!;
    var needed = exitNode.Reach[GraphTestCreate.Id(StartPoiId)];

    // Assert: At least as many solutions are needed on every route to the exit as the main
    // pass placed.
    var mandatory = SolutionIds(graph).FindAll(needed.Contains).Count;
    Assert.True(mandatory >= placer.PlacedMainObstacles, context);
  }

  /// <summary>Named graph factories for the sweep tests; each call builds a fresh graph.</summary>
  /// <returns>Shape name and factory pairs.</returns>
  private static List<(string Name, Func<LilGraph> Create)> Shapes()
  {
    return
    [
      ("chain", () => CreateLevel(6, 1, 6, Chain(6))),
      ("cycle", () => CreateLevel(
        6, 1, 4,
        new Passage(1, 2), new Passage(2, 3), new Passage(3, 4), new Passage(4, 5), new Passage(5, 6), new Passage(6, 1))),
      ("star", () => CreateLevel(
        6, 2, 6,
        new Passage(1, 2), new Passage(1, 3), new Passage(1, 4), new Passage(1, 5), new Passage(1, 6))),
      ("adjacent", CreateAdjacentLevel),
      ("mixed", CreateMixedLevel),
    ];
  }

  /// <summary>
  /// Start (node 1) right next to the exit (node 2); five leaves on the start, none on the exit.
  /// Edge 0 is the direct passage.
  /// </summary>
  /// <returns>The graph.</returns>
  private static LilGraph CreateAdjacentLevel()
  {
    return CreateLevel(
      7, 1, 2,
      new Passage(1, 2),
      new Passage(1, 3), new Passage(1, 4), new Passage(1, 5), new Passage(1, 6), new Passage(1, 7));
  }

  /// <summary>
  /// Chain 1 - 5 with a shortcut, a leaf, parallel passages, a secret bridge from start to
  /// exit (node 7), a secret leaf (node 8) and a one-way secret loop (node 9).
  /// </summary>
  /// <returns>The graph; start in node 1, exit in node 5.</returns>
  private static LilGraph CreateMixedLevel()
  {
    var graph = CreateLevel(
      9, 1, 5,
      new Passage(1, 2),
      new Passage(2, 3, Chokepoint: true),
      new Passage(3, 4),
      new Passage(4, 5),
      new Passage(4, 5, Chokepoint: true),
      new Passage(2, 4),
      new Passage(3, 6),
      new Passage(1, 7, Secret: true),
      new Passage(7, 5, Secret: true),
      new Passage(3, 8, Secret: true),
      new Passage(2, 9, Secret: true, OneWay: true),
      new Passage(9, 2, Secret: true, OneWay: true));
    MarkSecret(graph, 7, 8, 9);
    return graph;
  }

  #endregion

  /// <summary>Tests constructor argument validation.</summary>
  public sealed class ValidationTests
  {
    [Fact]
    public void Constructor_WithNullArguments_ThrowsArgumentNullException()
    {
      // Act/Assert: Each missing argument is rejected naming the parameter.
      Assert.Equal("graph", Assert.Throws<ArgumentNullException>(
        () => new ProcObstaclePlacer(null!, CnfProcObstaclePlacerOpt.Default, 1, 42)).ParamName);
      Assert.Equal("options", Assert.Throws<ArgumentNullException>(
        () => new ProcObstaclePlacer(new LilGraph(), null!, 1, 42)).ParamName);
    }

    [Fact]
    public void Constructor_WithNegativeComplexity_ThrowsArgumentOutOfRangeException()
    {
      // Act/Assert: A negative amount of obstacles is rejected.
      var exception = Assert.Throws<ArgumentOutOfRangeException>(
        () => new ProcObstaclePlacer(new LilGraph(), CnfProcObstaclePlacerOpt.Default, -1, 42));
      Assert.Equal("complexity", exception.ParamName);
    }

    [Fact]
    public void Constructor_WithInvalidOptions_ThrowsBeforeExecute()
    {
      // Arrange: Options violating their own invariants; Complexity 0 would make the pass a no-op.
      var options = CnfProcObstaclePlacerOpt.Default with { MaxEdgesPerObstacle = 0 };

      // Act/Assert: Bad options never pass silently.
      var exception = Assert.Throws<ArgumentOutOfRangeException>(
        () => new ProcObstaclePlacer(new LilGraph(), options, 0, 42));
      Assert.Equal("MaxEdgesPerObstacle", exception.ParamName);
    }
  }

  /// <summary>Tests the cases where the pass must leave the graph alone.</summary>
  public sealed class EarlyExitTests
  {
    /// <summary>Asserts that a run placed nothing.</summary>
    /// <param name="graph">Graph after the run.</param>
    /// <param name="placer">Placer that ran.</param>
    private static void AssertUntouched(LilGraph graph, ProcObstaclePlacer placer)
    {
      // Assert: No solution, no condition, nothing counted.
      Assert.Equal(0, placer.PlacedObstacles);
      Assert.Empty(SolutionIds(graph));
      Assert.Equal(0, CountConditions(graph));
    }

    [Fact]
    public void Execute_WithZeroComplexity_LeavesGraphUntouched()
    {
      // Arrange: A graph that could take obstacles.
      var graph = CreateLevel(4, 1, 4, Chain(4));

      // Act/Assert: Complexity 0 disables the pass.
      AssertUntouched(graph, Run(graph, 0, 42));
    }

    [Fact]
    public void Execute_OnEmptyGraph_DoesNothing()
    {
      // Arrange: No nodes at all.
      var graph = new LilGraph();

      // Act/Assert: Nothing to lock.
      AssertUntouched(graph, Run(graph, 3, 42));
    }

    [Fact]
    public void Execute_WithSingleNode_LeavesGraphUntouched()
    {
      // Arrange: One node holding start and exit.
      var graph = CreateLevel(1, 1, 1);

      // Act/Assert: Nothing to lock.
      AssertUntouched(graph, Run(graph, 3, 42));
    }

    [Fact]
    public void Execute_WithoutPassages_LeavesGraphUntouched()
    {
      // Arrange: Two nodes, no passage.
      var graph = CreateLevel(2, 1, 2);

      // Act/Assert: Nothing to lock.
      AssertUntouched(graph, Run(graph, 3, 42));
    }

    [Theory]
    [InlineData(EnPoiType.Start)]
    [InlineData(EnPoiType.Exit)]
    public void Execute_WithMissingStartOrExit_LeavesGraphUntouched(EnPoiType present)
    {
      // Arrange: Chain with only one of the two terminals.
      var graph = CreateGraph(4, Chain(4));
      graph.Nodes[0].Pois.Add(GraphTestCreate.Poi(StartPoiId, present));

      // Act/Assert: Without both terminals the pass is a no-op.
      AssertUntouched(graph, Run(graph, 3, 42));
    }

    [Fact]
    public void Execute_WithStartsInTwoNodes_LeavesGraphUntouched()
    {
      // Arrange: A second start node.
      var graph = CreateLevel(4, 1, 4, Chain(4));
      graph.Nodes[1].Pois.Add(GraphTestCreate.Poi(777, EnPoiType.Start));

      // Act/Assert: Several start nodes are not supported.
      AssertUntouched(graph, Run(graph, 3, 42));
    }

    [Fact]
    public void Execute_WithStartAndExitInSameNode_PlacesNoMainObstacles()
    {
      // Arrange: Start and exit share node 2 of a chain.
      var graph = CreateLevel(4, 2, 2, Chain(4));

      // Act/Assert: Without goals the main pass has no target, and without a treasure the side
      // pass has nothing to guard.
      AssertUntouched(graph, Run(graph, 3, 42));
    }

    [Fact]
    public void Execute_WhenCancelled_ThrowsOperationCanceledException()
    {
      // Arrange: A graph with work to do and a token cancelled up front.
      var graph = CreateLevel(4, 1, 4, Chain(4));
      var placer = new ProcObstaclePlacer(graph, CnfProcObstaclePlacerOpt.Default, 3, 42);
      using var source = new CancellationTokenSource();
      source.Cancel();

      // Act/Assert: Cancellation is respected and nothing was placed.
      Assert.Throws<OperationCanceledException>(() => placer.Execute(source.Token));
      Assert.Empty(SolutionIds(graph));
      Assert.Equal(0, CountConditions(graph));
    }
  }

  /// <summary>
  /// Tests start and exit in one node: the main pass then cuts goal nodes off the start
  /// instead of the exit.
  /// </summary>
  public sealed class SharedStartExitTests
  {
    /// <summary>Tells whether a node is reachable from the start without any solution.</summary>
    private static bool IsReachableWithoutSolutions(LilGraph graph, int node)
    {
      var reachable = LostInLimbo.LevelGen.Api.LevelGraphReach.GetReachableNodes(graph, GraphTestCreate.Id(StartPoiId), []);
      return reachable.Contains(GraphTestCreate.Id(node));
    }

    [Fact]
    public void Execute_WithStartAndExitInSameNodeOnTriangle_LocksAGoal()
    {
      for (var seed = 0; seed < SweepSeeds; seed++)
      {
        // Arrange: The shape of the reported level - a triangle without pockets; start, exit
        // and an optional goal in node 3, goals in nodes 1 and 2.
        var graph = CreateLevel(3, 3, 3, new Passage(1, 2), new Passage(1, 3), new Passage(2, 3));
        AddGoal(graph, 1);
        AddGoal(graph, 2);
        AddGoal(graph, 3, optional: true);

        // Act: Complexity 1, no bypasses, so every lock stays closed.
        var placer = Run(graph, 1, seed, NoBypass);

        // Assert: The one obstacle is placed by the main pass and locks a goal away.
        var context = $"seed {seed}";
        Assert.Equal(1, placer.PlacedObstacles);
        Assert.Equal(1, placer.PlacedMainObstacles);
        Assert.False(IsReachableWithoutSolutions(graph, 1) && IsReachableWithoutSolutions(graph, 2), context);
        AssertInvariants(graph, placer, 1, NoBypass, context);
      }
    }

    [Fact]
    public void Execute_WithStartAndExitInSameNodeOnCycle_LocksTheGoal()
    {
      for (var seed = 0; seed < SweepSeeds; seed++)
      {
        // Arrange: A 4-cycle with start and exit in node 1 and the goal opposite, in node 3.
        var graph = CreateLevel(4, 1, 1, new Passage(1, 2), new Passage(2, 3), new Passage(3, 4), new Passage(4, 1));
        AddGoal(graph, 3);

        // Act: Complexity 2, no bypasses, so every lock stays closed.
        var placer = Run(graph, 2, seed, NoBypass);

        // Assert: The goal needs at least one solution; everything stays beatable.
        var context = $"seed {seed}";
        Assert.True(placer.PlacedMainObstacles >= 1, context);
        Assert.False(IsReachableWithoutSolutions(graph, 3), context);
        AssertInvariants(graph, placer, 2, NoBypass, context);
      }
    }

    [Fact]
    public void Execute_WithStartAndExitInSameNodeAndGoalOnStart_LocksTheOptionalGoal()
    {
      for (var seed = 0; seed < SweepSeeds; seed++)
      {
        // Arrange: A triangle; start, exit and the only goal in node 3, an optional goal in node 1.
        var graph = CreateLevel(3, 3, 3, new Passage(1, 2), new Passage(1, 3), new Passage(2, 3));
        AddGoal(graph, 3);
        AddGoal(graph, 1, optional: true);

        // Act: Complexity 1, no bypasses, so every lock stays closed.
        var placer = Run(graph, 1, seed, NoBypass);

        // Assert: The optional goal is the fallback target and ends up locked.
        var context = $"seed {seed}";
        Assert.Equal(1, placer.PlacedMainObstacles);
        Assert.False(IsReachableWithoutSolutions(graph, 1), context);
        AssertInvariants(graph, placer, 1, NoBypass, context);
      }
    }
  }

  /// <summary>Tests determinism.</summary>
  public sealed class DeterminismTests
  {
    [Fact]
    public void Execute_WithSameSeed_ProducesIdenticalGraphs()
    {
      // Arrange: Two equal graphs.
      var first = CreateMixedLevel();
      var second = CreateMixedLevel();

      // Act: Same complexity and seed on both.
      Run(first, 5, 1234);
      Run(second, 5, 1234);

      // Assert: Identical obstacles, and the run did place some.
      GraphTestAssert.AssertEqualGraphs(first, second);
      Assert.NotEmpty(SolutionIds(first));
    }

    [Fact]
    public void Execute_WithDifferentSeeds_ProducesDifferentPlacements()
    {
      // Arrange/Act: The same graph under many seeds; record where the solutions land.
      var placements = new HashSet<string>();
      for (var seed = 0; seed < SweepSeeds; seed++)
      {
        var graph = CreateMixedLevel();
        Run(graph, 4, seed);
        placements.Add(string.Join("|", graph.Nodes.ConvertAll(node => node.Pois.Count)));
      }

      // Assert: The seed matters.
      Assert.True(placements.Count > 1);
    }

    [Fact]
    public void Execute_WithReorderedLists_ProducesSameObstacles()
    {
      // Arrange: The same graph twice, the second with reversed node and edge lists.
      var ordered = CreateMixedLevel();
      var reversed = CreateMixedLevel();
      reversed.Nodes.Reverse();
      reversed.Edges.Reverse();

      // Act: Same complexity and seed on both.
      Run(ordered, 5, 99);
      Run(reversed, 5, 99);

      // Assert: List order has no influence: put the lists back and compare.
      reversed.Nodes.Reverse();
      reversed.Edges.Reverse();
      GraphTestAssert.AssertEqualGraphs(ordered, reversed);
    }
  }

  /// <summary>Tests limits and softlock freedom over many seeds and shapes.</summary>
  public sealed class SweepTests
  {
    [Fact]
    public void Execute_OverShapesSeedsAndComplexities_KeepsEveryInvariant()
    {
      var options = CnfProcObstaclePlacerOpt.Default;
      foreach (var (name, create) in Shapes())
      {
        for (var complexity = 1; complexity <= 6; complexity++)
        {
          for (var seed = 0; seed < SweepSeeds; seed++)
          {
            // Arrange/Act: A fresh graph per case.
            var graph = create();
            var placer = Run(graph, complexity, seed, options);

            // Assert: Limits hold and the graph is beatable.
            AssertInvariants(graph, placer, complexity, options, $"{name}, complexity {complexity}, seed {seed}");
          }
        }
      }
    }

    [Fact]
    public void Execute_OverShapesAndSeeds_PlacesAtLeastOneObstacle()
    {
      foreach (var (name, create) in Shapes())
      {
        for (var seed = 0; seed < SweepSeeds; seed++)
        {
          // Arrange/Act: Complexity 1 on a fresh graph.
          var graph = create();
          var placer = Run(graph, 1, seed);

          // Assert: Every shape offers a cut in front of the exit.
          Assert.True(placer.PlacedObstacles == 1, $"{name}, seed {seed}");
        }
      }
    }

    [Fact]
    public void Execute_OverShapesAndSeeds_MakesEverySolutionMandatoryForExit()
    {
      foreach (var (name, create) in Shapes())
      {
        for (var seed = 0; seed < SweepSeeds; seed++)
        {
          // Arrange/Act: A fresh graph with a generous Complexity.
          var graph = create();
          var placer = Run(graph, 5, seed, NoBypass);

          // Assert: Without bypasses, no obstacle of the main pass can be skipped.
          AssertMainSolutionsMandatoryForExit(graph, placer, $"{name}, seed {seed}");
        }
      }
    }

    [Fact]
    public void Execute_WithTightLimits_RespectsThem()
    {
      // Arrange: One condition per passage, one passage per obstacle.
      var options = CnfProcObstaclePlacerOpt.Default with { MaxConditionsPerEdge = 1, MaxEdgesPerObstacle = 1 };
      for (var seed = 0; seed < SweepSeeds; seed++)
      {
        var graph = CreateMixedLevel();

        // Act: Ask for more than fits.
        var placer = Run(graph, 8, seed, options);

        // Assert: The tight limits hold; the mixed level still has width-1 cuts to use.
        AssertInvariants(graph, placer, 8, options, $"seed {seed}");
      }
    }
  }

  /// <summary>Tests where the main pass puts its obstacles.</summary>
  public sealed class MainPassTests
  {
    [Fact]
    public void Execute_OnChain_LocksOnePassagePerObstacle()
    {
      for (var seed = 0; seed < SweepSeeds; seed++)
      {
        // Arrange/Act: Chain of six nodes, four obstacles.
        var graph = CreateLevel(6, 1, 6, Chain(6));
        Run(graph, 4, seed);

        // Assert: Every passage of a chain is a bottleneck, so no obstacle needs two.
        foreach (var solutionId in SolutionIds(graph)) Assert.Single(EdgesRequiring(graph, solutionId));
      }
    }

    [Fact]
    public void Execute_OnCycle_LocksTwoPassagesPerObstacle()
    {
      for (var seed = 0; seed < SweepSeeds; seed++)
      {
        // Arrange/Act: Cycle of six nodes, start opposite the exit.
        var graph = Shapes()[1].Create();
        Run(graph, 2, seed, NoBypass);

        // Assert: Both ways round must carry the same solution.
        Assert.NotEmpty(SolutionIds(graph));
        foreach (var solutionId in SolutionIds(graph)) Assert.Equal(2, EdgesRequiring(graph, solutionId).Count);
      }
    }

    [Fact]
    public void Execute_WithParallelPassages_LocksThemWithTheSameSolution()
    {
      // Arrange: Start and exit joined by two parallel passages only.
      var graph = CreateLevel(2, 1, 2, new Passage(1, 2, Chokepoint: true), new Passage(1, 2));

      // Act: One obstacle.
      Run(graph, 1, 42);

      // Assert: One solution on both passages.
      var solution = Assert.Single(SolutionIds(graph));
      Assert.Equal(2, EdgesRequiring(graph, solution).Count);
    }

    [Fact]
    public void Execute_WithChoiceOfCuts_AvoidsTheCutThatLeavesOnlyTheStart()
    {
      for (var seed = 0; seed < SweepSeeds; seed++)
      {
        // Arrange: Chain 1 - 2 - 3. Both passages cost the same, but locking the first one
        // would leave only the start node for the solution.
        var graph = CreateLevel(3, 1, 3, Chain(3));

        // Act: One obstacle.
        Run(graph, 1, seed);

        // Assert: Always the passage in front of the exit.
        Assert.Empty(graph.Edges[0].Conditions);
        Assert.Single(graph.Edges[1].Conditions);
      }
    }

    [Fact]
    public void Execute_WhenOnlyCrampedCutsAreLeft_UsesThemAsLastResort()
    {
      for (var seed = 0; seed < SweepSeeds; seed++)
      {
        // Arrange: The same chain, one condition per passage.
        var options = NoBypass with { MaxConditionsPerEdge = 1 };
        var graph = CreateLevel(3, 1, 3, Chain(3));

        // Act: Two obstacles; after the first, only the passage at the start can still be locked.
        var placer = Run(graph, 2, seed, options);

        // Assert: Complexity is not wasted - the cramped cut is used for the second obstacle,
        // and its solution can only be in the start node.
        Assert.Equal(2, placer.PlacedObstacles);
        Assert.Single(graph.Edges[1].Conditions);
        var atStart = Assert.Single(graph.Edges[0].Conditions);
        Assert.Contains(graph.Nodes[0].Pois, poi => poi.Id == atStart.RequiresPoi);
        AssertInvariants(graph, placer, 2, options, $"seed {seed}");
      }
    }

    [Fact]
    public void Execute_OnTwoNodeGraph_StacksUpToTheCapAndStops()
    {
      // Arrange: Start and exit joined by one passage; nothing else to lock.
      var graph = CreateLevel(2, 1, 2, Chain(2));

      // Act: Ask for more obstacles than a passage can carry.
      var placer = Run(graph, 5, 42);

      // Assert: Stacking is forced here; it stops at three conditions, all solutions at the start.
      Assert.Equal(3, placer.PlacedObstacles);
      Assert.Equal(3, graph.Edges[0].Conditions.Count);
      Assert.Equal(3, graph.Nodes[0].Pois.FindAll(poi => poi.Type == EnPoiType.Solution).Count);
      GraphTestAssert.AssertBeatable(graph);
    }

    [Fact]
    public void Execute_WithAdjacentStartAndExit_AlwaysLocksTheDirectPassageFirst()
    {
      for (var seed = 0; seed < SweepSeeds; seed++)
      {
        // Arrange/Act: One obstacle on the adjacent level.
        var graph = CreateAdjacentLevel();
        Run(graph, 1, seed);

        // Assert: The first obstacle always guards the exit.
        Assert.Single(graph.Edges[0].Conditions);
      }
    }

    [Fact]
    public void Execute_WithAdjacentStartAndExit_RarelyStacksTheDirectPassage()
    {
      // Arrange/Act: Three obstacles on the adjacent level, many seeds.
      const int seeds = 300;
      var doubled = 0;
      var tripled = 0;
      for (var seed = 0; seed < seeds; seed++)
      {
        var graph = CreateAdjacentLevel();
        Run(graph, 3, seed);
        if (graph.Edges[0].Conditions.Count >= 2) doubled++;
        if (graph.Edges[0].Conditions.Count == 3) tripled++;
      }

      // Assert: Locking away earlier solutions dominates; three stacked obstacles are rare.
      Assert.True(doubled < seeds / 2, $"direct passage stacked in {doubled} of {seeds} seeds");
      Assert.True(tripled < seeds / 10, $"direct passage tripled in {tripled} of {seeds} seeds");
    }

    [Fact]
    public void Execute_WithStackingAlwaysAccepted_StacksTheDirectPassage()
    {
      // Arrange: Always re-target the exit and always accept stacking.
      var options = CnfProcObstaclePlacerOpt.Default with { ChanceNestSolution = 0f, ChanceStack = 1f };
      for (var seed = 0; seed < SweepSeeds; seed++)
      {
        var graph = CreateAdjacentLevel();

        // Act: Three obstacles.
        Run(graph, 3, seed, options);

        // Assert: All three sit on the direct passage.
        Assert.Equal(3, graph.Edges[0].Conditions.Count);
      }
    }

    [Fact]
    public void Execute_WithStackingNeverAccepted_NestsSolutionsInstead()
    {
      // Arrange: Always try the latest solution first and never accept stacking by roll.
      var options = CnfProcObstaclePlacerOpt.Default with { ChanceNestSolution = 1f, ChanceStack = 0f };
      var checkedSeeds = 0;
      for (var seed = 0; seed < SweepSeeds; seed++)
      {
        var graph = CreateAdjacentLevel();

        // Act: Three obstacles.
        Run(graph, 3, seed, options);

        // A solution in the start node cannot be locked away, which forces stacking; skip those.
        if (graph.Nodes[0].HasPoi(EnPoiType.Solution)) continue;
        checkedSeeds++;

        // Assert: The direct passage keeps its single condition; the other obstacles guard
        // leaves holding earlier solutions.
        Assert.Single(graph.Edges[0].Conditions);
        Assert.Equal(3, SolutionIds(graph).Count);
      }

      Assert.True(checkedSeeds > SweepSeeds / 2);
    }

    [Fact]
    public void Execute_WithSecretRouteAroundTheCut_LocksTheSecretPassage()
    {
      // Arrange: Chain 1 - 2 - 3 plus secret node 4 bridging start and exit.
      for (var seed = 0; seed < SweepSeeds; seed++)
      {
        var graph = CreateLevel(
          4, 1, 3,
          new Passage(1, 2),
          new Passage(2, 3),
          new Passage(1, 4, Secret: true),
          new Passage(4, 3, Secret: true));
        MarkSecret(graph, 4);

        // Act: One obstacle, bypasses off.
        var placer = Run(graph, 1, seed, NoBypass);

        // Assert: The secret way into the exit carries the same solution, so the obstacle
        // cannot be bypassed.
        var solution = Assert.Single(SolutionIds(graph));
        Assert.Contains(graph.Edges[3], EdgesRequiring(graph, solution));
        Assert.Equal(1, placer.PlacedMainObstacles);
        AssertMainSolutionsMandatoryForExit(graph, placer, $"seed {seed}");
      }
    }

    [Fact]
    public void Execute_WithExistingObstacle_KeepsItAndStaysBeatable()
    {
      for (var seed = 0; seed < SweepSeeds; seed++)
      {
        // Arrange: Chain whose middle passage already needs a solution lying at the start.
        const int oldSolution = 700;
        var graph = CreateLevel(4, 1, 4, Chain(4));
        graph.Nodes[0].Pois.Add(GraphTestCreate.Poi(oldSolution, EnPoiType.Solution));
        graph.Edges[1].Conditions.Add(GraphTestCreate.Condition(oldSolution));

        // Act: Two more obstacles.
        var placer = Run(graph, 2, seed);

        // Assert: The old obstacle is intact, the new ones are added and nothing softlocks.
        Assert.Contains(graph.Edges[1].Conditions, condition => condition.RequiresPoi == GraphTestCreate.Id(oldSolution));
        Assert.Equal(placer.PlacedObstacles + 1, SolutionIds(graph).Count);
        Assert.Equal(2, placer.PlacedObstacles);
        GraphTestAssert.AssertBeatable(graph, $"seed {seed}");
      }
    }
  }

  /// <summary>Tests how Complexity is split between the main pass and the side pass.</summary>
  public sealed class BudgetSplitTests
  {
    /// <summary>Number of goal leaves, and so of pockets, in <see cref="CreateLevelWithPockets"/>.</summary>
    private const int PocketCount = 3;

    /// <summary>
    /// Chain 1 - 6 from start to exit, with goal leaves 7, 8 and 9 hanging on nodes 2, 3 and 4.
    /// Edges 5, 6 and 7 are the leaf passages.
    /// </summary>
    /// <returns>The graph.</returns>
    private static LilGraph CreateLevelWithPockets()
    {
      var passages = new List<Passage>(Chain(6)) { new(2, 7), new(3, 8), new(4, 9) };
      var graph = CreateLevel(9, 1, 6, [.. passages]);
      AddGoal(graph, 7);
      AddGoal(graph, 8, optional: true);
      AddGoal(graph, 9);
      return graph;
    }

    [Theory]
    [InlineData(4, 0.7f, 1)] // round(2.8) = 3 for main.
    [InlineData(5, 0.7f, 1)] // round(3.5) = 4 for main.
    [InlineData(10, 0.7f, 3)]
    [InlineData(10, 0.5f, 3)] // 5 wanted, capped at the three pockets.
    [InlineData(10, 1f, 0)]
    [InlineData(4, 0f, 3)]
    public void Execute_WithLargeComplexity_ReservesByMainShare(int complexity, float mainShare, int expectedReserve)
    {
      var options = CnfProcObstaclePlacerOpt.Default with { MainShare = mainShare };
      for (var seed = 0; seed < SweepSeeds; seed++)
      {
        // Arrange/Act: Complexity above the small limit.
        var graph = CreateLevelWithPockets();
        var placer = Run(graph, complexity, seed, options);

        // Assert: The reserve follows the share, never exceeds the pocket count and is spent.
        Assert.Equal(expectedReserve, placer.ReservedSideBudget);
        Assert.True(placer.PlacedSideObstacles >= expectedReserve, $"seed {seed}");
        Assert.True(placer.PlacedMainObstacles <= complexity - expectedReserve, $"seed {seed}");
        AssertInvariants(graph, placer, complexity, options, $"seed {seed}");
      }
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public void Execute_WithSmallComplexity_ReservesOneByChance(int complexity)
    {
      var always = CnfProcObstaclePlacerOpt.Default with { ChanceReserveSide = 1f, ChanceReserveSideOnly = 0f };
      var never = CnfProcObstaclePlacerOpt.Default with { ChanceReserveSide = 0f, ChanceReserveSideOnly = 1f };
      for (var seed = 0; seed < SweepSeeds; seed++)
      {
        // Act: The same level with the reserve forced on and off.
        var reserved = Run(CreateLevelWithPockets(), complexity, seed, always);
        var unreserved = Run(CreateLevelWithPockets(), complexity, seed, never);

        // Assert: Exactly one point is reserved, or none; the chance for Complexity 1 is not used.
        Assert.Equal(1, reserved.ReservedSideBudget);
        Assert.Equal(complexity - 1, reserved.PlacedMainObstacles);
        Assert.Equal(1, reserved.PlacedSideObstacles);
        Assert.Equal(0, unreserved.ReservedSideBudget);
        Assert.Equal(complexity, unreserved.PlacedMainObstacles);
      }
    }

    [Fact]
    public void Execute_WithComplexityOne_UsesItsOwnReserveChance()
    {
      var always = CnfProcObstaclePlacerOpt.Default with { ChanceReserveSide = 0f, ChanceReserveSideOnly = 1f };
      var never = CnfProcObstaclePlacerOpt.Default with { ChanceReserveSide = 1f, ChanceReserveSideOnly = 0f };
      for (var seed = 0; seed < SweepSeeds; seed++)
      {
        // Act: Complexity 1 with the reserve forced on and off.
        var sideOnly = Run(CreateLevelWithPockets(), 1, seed, always);
        var mainOnly = Run(CreateLevelWithPockets(), 1, seed, never);

        // Assert: The single obstacle goes to the side pass or to the main pass.
        Assert.Equal((0, 1), (sideOnly.PlacedMainObstacles, sideOnly.PlacedSideObstacles));
        Assert.Equal((1, 0), (mainOnly.PlacedMainObstacles, mainOnly.PlacedSideObstacles));
      }
    }

    [Fact]
    public void Execute_WithoutPockets_GivesEverythingToTheMainPass()
    {
      // Arrange: Reserve chances forced on, but the chain has no treasure.
      var options = CnfProcObstaclePlacerOpt.Default with { ChanceReserveSide = 1f, ChanceReserveSideOnly = 1f, MainShare = 0f };
      foreach (var complexity in new[] { 1, 3, 5 })
      {
        var graph = CreateLevel(6, 1, 6, Chain(6));

        // Act: Run the placer.
        var placer = Run(graph, complexity, 42, options);

        // Assert: Nothing is reserved; the chain has five passages for the main pass.
        Assert.Equal(0, placer.ReservedSideBudget);
        Assert.Equal(0, placer.PlacedSideObstacles);
        Assert.Equal(complexity, placer.PlacedMainObstacles);
      }
    }

    [Fact]
    public void Execute_WithReservedPockets_KeepsMainSolutionsOutOfThem()
    {
      for (var seed = 0; seed < SweepSeeds; seed++)
      {
        // Arrange/Act: All three pockets are reserved.
        var graph = CreateLevelWithPockets();
        var placer = Run(graph, 10, seed);

        // Assert: Every pocket is locked exactly once and holds no solution.
        Assert.Equal(PocketCount, placer.PlacedSideObstacles);
        for (var leaf = 7; leaf <= 9; leaf++)
        {
          Assert.False(graph.Nodes[leaf - 1].HasPoi(EnPoiType.Solution), $"seed {seed}");
          Assert.Single(graph.Edges[leaf - 2].Conditions);
        }
      }
    }

    [Fact]
    public void Execute_WithStartAndExitInSameNode_SplitsBudgetBetweenMainAndSidePass()
    {
      for (var seed = 0; seed < SweepSeeds; seed++)
      {
        // Arrange: Start and exit share node 1; goal leaves as before, so the goals are both
        // main targets and pocket treasures.
        var passages = new List<Passage>(Chain(6)) { new(2, 7), new(3, 8), new(4, 9) };
        var graph = CreateLevel(9, 1, 1, [.. passages]);
        AddGoal(graph, 7);
        AddGoal(graph, 8);
        AddGoal(graph, 9);

        // Act: More Complexity than pockets.
        var placer = Run(graph, 5, seed);

        // Assert: The main pass now has goal targets and places obstacles; the side pass never
        // exceeds the pocket count.
        Assert.True(placer.PlacedMainObstacles >= 1, $"seed {seed}");
        Assert.True(placer.PlacedSideObstacles <= PocketCount, $"seed {seed}");
        AssertInvariants(graph, placer, 5, CnfProcObstaclePlacerOpt.Default, $"seed {seed}");
      }
    }

    [Fact]
    public void Execute_WhenMainPassCannotSpendItsBudget_LeavesTheRestToTheSidePass()
    {
      // Arrange: Nothing reserved, one condition per passage: the main pass can lock the
      // direct passage 1 - 2 once, and at most the goal leaf 3 when its solution landed there.
      var options = CnfProcObstaclePlacerOpt.Default with { MainShare = 1f, MaxConditionsPerEdge = 1 };
      const int seeds = 300; // The solution lands in the start node only rarely.
      var sideUsedLeftover = false;
      for (var seed = 0; seed < seeds; seed++)
      {
        var graph = CreateLevel(3, 1, 2, new Passage(1, 2), new Passage(1, 3));
        AddGoal(graph, 3);

        // Act: Far more Complexity than fits.
        var placer = Run(graph, 4, seed, options);

        // Assert: Nothing was reserved, both passages end up locked, by either pass.
        Assert.Equal(0, placer.ReservedSideBudget);
        Assert.Equal(2, placer.PlacedObstacles);
        AssertInvariants(graph, placer, 4, options, $"seed {seed}");
        sideUsedLeftover |= placer.PlacedSideObstacles == 1;
      }

      // Assert: In some runs the leaf was locked by the side pass, from the unspent budget.
      Assert.True(sideUsedLeftover);
    }
  }

  /// <summary>Tests the deliberate open ways around obstacles: secret and ordinary bypasses.</summary>
  public sealed class BypassTests
  {
    /// <summary>Options where every possible secret bypass happens and no ordinary one.</summary>
    private static readonly CnfProcObstaclePlacerOpt SecretBypassAlways =
      CnfProcObstaclePlacerOpt.Default with { ChanceSecretBypass = 1f, ChanceBypass = 0f };

    /// <summary>Options where every possible ordinary bypass happens and no secret one.</summary>
    private static readonly CnfProcObstaclePlacerOpt BypassAlways =
      CnfProcObstaclePlacerOpt.Default with { ChanceSecretBypass = 0f, ChanceBypass = 1f };

    [Fact]
    public void Execute_WithSecretBypassAlways_LeavesSecretPassagesOpen()
    {
      for (var seed = 0; seed < SweepSeeds; seed++)
      {
        // Arrange: Chain 1 - 2 - 3 plus secret node 4 bridging start and exit.
        var graph = CreateLevel(
          4, 1, 3,
          new Passage(1, 2),
          new Passage(2, 3),
          new Passage(1, 4, Secret: true),
          new Passage(4, 3, Secret: true));
        MarkSecret(graph, 4);

        // Act: One obstacle.
        var placer = Run(graph, 1, seed, SecretBypassAlways);

        // Assert: One ordinary passage is locked; the secret route stays an open shortcut.
        Assert.Equal(1, placer.PlacedObstacles);
        Assert.Equal(1, CountConditions(graph));
        Assert.Empty(graph.Edges[2].Conditions);
        Assert.Empty(graph.Edges[3].Conditions);
        AssertInvariants(graph, placer, 1, SecretBypassAlways, $"seed {seed}");
      }
    }

    [Fact]
    public void Execute_WithSecretBypassNever_LocksSecretPassages()
    {
      // Arrange: The same level; bypasses off.
      var graph = CreateLevel(
        4, 1, 3,
        new Passage(1, 2),
        new Passage(2, 3),
        new Passage(1, 4, Secret: true),
        new Passage(4, 3, Secret: true));
      MarkSecret(graph, 4);

      // Act: One obstacle.
      Run(graph, 1, 42, NoBypass);

      // Assert: An ordinary and a secret passage carry the condition.
      Assert.Equal(2, CountConditions(graph));
    }

    [Fact]
    public void Execute_WithOnlySecretPassagesInCut_KeepsThemLocked()
    {
      for (var seed = 0; seed < SweepSeeds; seed++)
      {
        // Arrange: The exit can only be reached through a secret passage.
        var graph = CreateLevel(3, 1, 3, new Passage(1, 2), new Passage(2, 3, Secret: true));

        // Act: One obstacle with every secret bypass wanted.
        var placer = Run(graph, 1, seed, SecretBypassAlways);

        // Assert: Whatever was locked stays locked - an obstacle never ends up locking nothing.
        AssertInvariants(graph, placer, 1, SecretBypassAlways, $"seed {seed}");
        Assert.Equal(placer.PlacedObstacles, CountConditions(graph));
      }
    }

    [Fact]
    public void Execute_WithBypassAlways_LeavesOnePassageOfWideCutOpen()
    {
      for (var seed = 0; seed < SweepSeeds; seed++)
      {
        // Arrange: Cycle of six nodes, start opposite the exit: every cut has two passages.
        var graph = Shapes()[1].Create();

        // Act: One obstacle.
        var placer = Run(graph, 1, seed, BypassAlways);

        // Assert: Only one of the two ways round is locked.
        var solution = Assert.Single(SolutionIds(graph));
        Assert.Single(EdgesRequiring(graph, solution));
        AssertInvariants(graph, placer, 1, BypassAlways, $"seed {seed}");
      }
    }

    [Fact]
    public void Execute_WithBypassAlways_NeverOpensSinglePassageCut()
    {
      for (var seed = 0; seed < SweepSeeds; seed++)
      {
        // Arrange: Chain of six nodes: every cut is one passage.
        var graph = CreateLevel(6, 1, 6, Chain(6));

        // Act: Three obstacles.
        var placer = Run(graph, 3, seed, BypassAlways);

        // Assert: Every obstacle still locks its passage.
        Assert.Equal(3, placer.PlacedObstacles);
        foreach (var solutionId in SolutionIds(graph)) Assert.Single(EdgesRequiring(graph, solutionId));
      }
    }

    [Fact]
    public void Execute_WithBypassAlways_NeverOpensParallelPassage()
    {
      for (var seed = 0; seed < SweepSeeds; seed++)
      {
        // Arrange: Start and exit joined by two parallel passages and nothing else.
        var twins = CreateLevel(2, 1, 2, new Passage(1, 2), new Passage(2, 1));

        // Arrange: The same pair plus a detour over node 3 (edges 2 and 3).
        var withDetour = CreateLevel(
          3, 1, 2,
          new Passage(1, 2), new Passage(2, 1), new Passage(1, 3), new Passage(3, 2));

        // Act: One obstacle each.
        Run(twins, 1, seed, BypassAlways);
        Run(withDetour, 1, seed, BypassAlways);

        // Assert: Parallel passages stay locked together; with a detour, the detour is the open way.
        Assert.Single(twins.Edges[0].Conditions);
        Assert.Single(twins.Edges[1].Conditions);
        Assert.Single(withDetour.Edges[0].Conditions);
        Assert.Single(withDetour.Edges[1].Conditions);
        Assert.Empty(withDetour.Edges[2].Conditions);
        Assert.Empty(withDetour.Edges[3].Conditions);
        GraphTestAssert.AssertBeatable(withDetour, $"seed {seed}");
      }
    }

    [Fact]
    public void Execute_WithBypassesAlways_NeverOpensSidePockets()
    {
      // Arrange: Start and exit share node 1; a secret leaf 3 and a secret leaf 4 behind
      // parallel ordinary passages. No goals, so the main pass has no target.
      var options = CnfProcObstaclePlacerOpt.Default with { ChanceSecretBypass = 1f, ChanceBypass = 1f };
      for (var seed = 0; seed < SweepSeeds; seed++)
      {
        var graph = CreateLevel(
          5, 1, 1,
          new Passage(1, 2), new Passage(2, 3, Secret: true), new Passage(2, 4), new Passage(4, 2), new Passage(2, 5));
        MarkSecret(graph, 3, 4);

        // Act: Only the side pass can run.
        var placer = Run(graph, 4, seed, options);

        // Assert: Both pockets are fully locked.
        Assert.Equal(2, placer.PlacedSideObstacles);
        Assert.Single(graph.Edges[1].Conditions);
        Assert.Single(graph.Edges[2].Conditions);
        Assert.Single(graph.Edges[3].Conditions);
      }
    }

    [Fact]
    public void Execute_WithBypassesAlways_KeepsEveryInvariant()
    {
      // Arrange: Every bypass that can happen does.
      var options = CnfProcObstaclePlacerOpt.Default with { ChanceSecretBypass = 1f, ChanceBypass = 1f };
      foreach (var (name, create) in Shapes())
      {
        for (var complexity = 1; complexity <= 6; complexity++)
        {
          for (var seed = 0; seed < SweepSeeds; seed++)
          {
            // Act: A fresh graph per case.
            var graph = create();
            var placer = Run(graph, complexity, seed, options);

            // Assert: Still beatable, every solution still locks something.
            AssertInvariants(graph, placer, complexity, options, $"{name}, complexity {complexity}, seed {seed}");
          }
        }
      }
    }
  }

  /// <summary>
  /// Tests the stack limit of the main pass (<c>MaxStackLoad</c>, <c>FreeStackedConditions</c>).
  /// Bypasses are off, so what the limit allowed is exactly what the graph carries afterwards.
  /// </summary>
  public sealed class StackLimitTests
  {
    /// <summary>Options that forbid every stacked condition.</summary>
    private static readonly CnfProcObstaclePlacerOpt NoStacking =
      NoBypass with { MaxStackLoad = 0f, FreeStackedConditions = 0 };

    /// <summary>Measures the condition load of a graph like the placer does.</summary>
    /// <param name="graph">Graph to measure.</param>
    /// <returns>Passages with a condition, and conditions beyond the first on them.</returns>
    private static (int Locked, int Extra) MeasureLoad(LilGraph graph)
    {
      var locked = graph.Edges.Count(edge => edge.Conditions.Count > 0);
      return (locked, CountConditions(graph) - locked);
    }

    [Fact]
    public void Execute_WithStackingForbidden_NeverStacks()
    {
      foreach (var (name, create) in Shapes())
      {
        for (var complexity = 1; complexity <= 6; complexity++)
        {
          for (var seed = 0; seed < SweepSeeds; seed++)
          {
            // Arrange/Act: A fresh graph per case.
            var graph = create();
            var placer = Run(graph, complexity, seed, NoStacking);

            // Assert: No passage carries a second condition; everything else still holds.
            var context = $"{name}, complexity {complexity}, seed {seed}";
            Assert.All(graph.Edges, edge => Assert.True(edge.Conditions.Count <= 1, context));
            AssertInvariants(graph, placer, complexity, NoStacking, context);
          }
        }
      }
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 2)]
    [InlineData(2, 3)]
    [InlineData(5, 3)] // The cap of three conditions per passage still applies.
    public void Execute_OnTwoNodeGraph_StacksOnlyWithinTheFreeAllowance(int freeStacked, int expectedConditions)
    {
      // Arrange: Start and exit joined by one passage; the ratio part of the limit is off.
      var options = NoBypass with { MaxStackLoad = 0f, FreeStackedConditions = freeStacked };
      var graph = CreateLevel(2, 1, 2, Chain(2));

      // Act: Ask for more obstacles than fit.
      var placer = Run(graph, 5, 42, options);

      // Assert: One first condition plus the allowed stacked ones.
      Assert.Equal(expectedConditions, placer.PlacedObstacles);
      Assert.Equal(expectedConditions, graph.Edges[0].Conditions.Count);
      GraphTestAssert.AssertBeatable(graph);
    }

    [Theory]
    [InlineData(0.25f, 2)]
    [InlineData(0.5f, 0)]
    [InlineData(0f, 1)]
    public void Execute_OverShapesAndSeeds_StaysWithinTheLimit(float maxStackLoad, int freeStacked)
    {
      // Arrange: Stacking always wanted, so only the limit holds it back.
      var options = NoBypass with { ChanceStack = 1f, MaxStackLoad = maxStackLoad, FreeStackedConditions = freeStacked };
      foreach (var (name, create) in Shapes())
      {
        for (var seed = 0; seed < SweepSeeds; seed++)
        {
          // Act: A generous Complexity on a fresh graph.
          var graph = create();
          Run(graph, 8, seed, options);

          // Assert: Stacked conditions never exceed the allowance plus the share of locked passages.
          var (locked, extra) = MeasureLoad(graph);
          Assert.True(extra <= freeStacked + maxStackLoad * locked, $"{name}, seed {seed}: {extra} stacked on {locked} locked");
        }
      }
    }

    [Fact]
    public void Execute_WithLimitSwitchedOff_StacksToTheCap()
    {
      // Arrange: Exit always targeted first, stacking always accepted, the limit far out of reach.
      var options = NoBypass with { ChanceStack = 1f, ChanceNestSolution = 0f, MaxStackLoad = 100f, FreeStackedConditions = 0 };
      var graph = CreateAdjacentLevel();

      // Act: Three obstacles.
      Run(graph, 3, 42, options);

      // Assert: All three land on the direct passage.
      Assert.Equal(3, graph.Edges[0].Conditions.Count);
    }

    [Fact]
    public void Execute_WithLimitHit_RefusesStackingEvenWhenTheRollAccepts()
    {
      for (var seed = 0; seed < SweepSeeds; seed++)
      {
        // Arrange: Stacking always accepted by the roll, but forbidden by the limit.
        var options = NoStacking with { ChanceStack = 1f };
        var graph = CreateAdjacentLevel();

        // Act: Four obstacles.
        var placer = Run(graph, 4, seed, options);

        // Assert: The direct passage is locked once; further obstacles guard solutions instead.
        Assert.Single(graph.Edges[0].Conditions);
        Assert.InRange(placer.PlacedObstacles, 1, 4);
        AssertInvariants(graph, placer, 4, options, $"seed {seed}");
      }
    }

    [Fact]
    public void Execute_WhenMainPassEndsOnTheLimit_LeavesTheRestToTheSidePass()
    {
      // Arrange: Start 1 next to exit 2, a goal leaf 3 behind the exit; nothing reserved for the side pass.
      var options = NoStacking with { MainShare = 1f };
      var graph = CreateLevel(3, 1, 2, new Passage(1, 2), new Passage(2, 3));
      AddGoal(graph, 3);

      // Act: More Complexity than the main pass may use.
      var placer = Run(graph, 5, 42, options);

      // Assert: The main pass stops after one obstacle instead of stacking; the side pass locks the leaf.
      Assert.Equal(0, placer.ReservedSideBudget);
      Assert.Equal(1, placer.PlacedMainObstacles);
      Assert.Equal(1, placer.PlacedSideObstacles);
      Assert.Single(graph.Edges[0].Conditions);
      Assert.Single(graph.Edges[1].Conditions);
      AssertInvariants(graph, placer, 5, options, "leftover");
    }
  }
}
