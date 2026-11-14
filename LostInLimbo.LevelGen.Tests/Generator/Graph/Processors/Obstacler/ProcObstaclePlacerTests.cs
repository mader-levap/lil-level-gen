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
    Assert.Equal(solutions.Count, new HashSet<Guid>(solutions).Count);

    // Assert: Per passage - cap respected, only known solutions, none twice, one-way untouched.
    foreach (var edge in graph.Edges)
    {
      Assert.True(edge.Conditions.Count <= options.MaxConditionsPerEdge, context);
      if (edge.Direction == EnPassageDirection.OneWay) Assert.Empty(edge.Conditions);

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
  /// Asserts that every solution is unavoidable on the way to the exit, using the reach metadata.
  /// </summary>
  /// <param name="graph">Graph after the run.</param>
  /// <param name="context">Text naming the case, for failure messages.</param>
  private static void AssertAllSolutionsMandatoryForExit(LilGraph graph, string context)
  {
    new GraphMetadata(graph).Generate();
    var exitNode = graph.Nodes.Find(node => node.HasPoi(EnPoiType.Exit))!;
    var needed = exitNode.Reach[GraphTestCreate.Id(StartPoiId)];

    // Assert: Each placed solution is among the ones every route to the exit needs.
    foreach (var solutionId in SolutionIds(graph)) Assert.True(needed.Contains(solutionId), context);
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

      // Act/Assert: The main pass is skipped. (Until the side pass exists, nothing is placed.)
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
          Run(graph, 5, seed);

          // Assert: No obstacle of the main pass can be skipped.
          AssertAllSolutionsMandatoryForExit(graph, $"{name}, seed {seed}");
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
        Run(graph, 2, seed);

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

        // Act: One obstacle.
        Run(graph, 1, seed);

        // Assert: The secret way into the exit carries the same solution, so the obstacle
        // cannot be bypassed.
        var solution = Assert.Single(SolutionIds(graph));
        Assert.Contains(graph.Edges[3], EdgesRequiring(graph, solution));
        AssertAllSolutionsMandatoryForExit(graph, $"seed {seed}");
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
}
