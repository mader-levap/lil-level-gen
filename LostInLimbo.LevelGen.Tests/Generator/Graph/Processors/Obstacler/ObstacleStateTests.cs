using LostInLimbo.LevelGen.Data.Config.Graph.Proc;
using LostInLimbo.LevelGen.Data.Result.Graph;
using LostInLimbo.LevelGen.Internal.Generator.Graph.Meta;
using LostInLimbo.LevelGen.Internal.Generator.Graph.Processors.Obstacler;
using static LostInLimbo.LevelGen.Tests.Generator.Graph.Processors.Obstacler.ObstaclerTestSupport;

namespace LostInLimbo.LevelGen.Tests.Generator.Graph.Processors.Obstacler;

/// <summary>
/// Tests <see cref="ObstacleState"/>: writing one obstacle into the graph under the softlock rule.
/// </summary>
public sealed class ObstacleStateTests
{
  /// <summary>Seed used where the value does not matter.</summary>
  private const long Seed = 42;

  /// <summary>Creates the state of a graph with default options.</summary>
  /// <param name="graph">Graph that receives obstacles.</param>
  /// <returns>The state.</returns>
  private static ObstacleState CreateState(LilGraph graph) => new(graph, CnfProcObstaclePlacerOpt.Default, Seed);

  [Fact]
  public void Constructor_WithNullArguments_ThrowsArgumentNullException()
  {
    // Act/Assert: Each missing argument is rejected naming the parameter.
    Assert.Equal("graph", Assert.Throws<ArgumentNullException>(
      () => new ObstacleState(null!, CnfProcObstaclePlacerOpt.Default, Seed)).ParamName);
    Assert.Equal("options", Assert.Throws<ArgumentNullException>(
      () => new ObstacleState(new LilGraph(), null!, Seed)).ParamName);
  }

  [Fact]
  public void TryPlaceObstacle_LocksEveryPassageAndPlacesOneSolutionBeforeThem()
  {
    for (var seed = 0; seed < 30; seed++)
    {
      // Arrange: Chain 1 - 2 - 3 - 4, start in node 1.
      var graph = CreateLevel(4, 1, 4, Chain(4));
      var state = new ObstacleState(graph, CnfProcObstaclePlacerOpt.Default, seed);

      // Act: Lock the passage between 2 and 3.
      var placed = state.TryPlaceObstacle([1], null, out var solutionNode);

      // Assert: One condition on that passage only, its solution in a node before it.
      Assert.True(placed);
      Assert.Equal(1, state.ObstacleCount);
      Assert.InRange(solutionNode, 0, 1);
      var condition = Assert.Single(graph.Edges[1].Conditions);
      Assert.Equal(EnConditionType.ReqSolution, condition.Type);
      Assert.Empty(graph.Edges[0].Conditions);
      Assert.Empty(graph.Edges[2].Conditions);
      var solution = graph.Nodes[solutionNode].FindPoi(EnPoiType.Solution);
      Assert.NotNull(solution);
      Assert.Equal(condition.RequiresPoi, solution.Id);
      GraphTestAssert.AssertBeatable(graph);
    }
  }

  [Fact]
  public void TryPlaceObstacle_WithSeveralPassages_UsesOneSolutionForAll()
  {
    // Arrange: Cycle 1 - 2 - 3 - 4 - 1, start in node 1, exit in node 3.
    var graph = CreateLevel(4, 1, 3, new Passage(1, 2), new Passage(2, 3), new Passage(3, 4), new Passage(4, 1));
    var state = CreateState(graph);

    // Act: Lock both passages into node 3.
    Assert.True(state.TryPlaceObstacle([1, 2], null, out _));

    // Assert: Both passages require the same solution.
    Assert.Equal(graph.Edges[1].Conditions[0].RequiresPoi, graph.Edges[2].Conditions[0].RequiresPoi);
  }

  [Fact]
  public void TryPlaceObstacle_Twice_UsesDistinctSolutions()
  {
    // Arrange: Chain of four nodes.
    var graph = CreateLevel(4, 1, 4, Chain(4));
    var state = CreateState(graph);

    // Act: Two obstacles on different passages.
    Assert.True(state.TryPlaceObstacle([2], null, out _));
    Assert.True(state.TryPlaceObstacle([1], null, out _));

    // Assert: Two different solution identifiers, both counted.
    Assert.Equal(2, state.ObstacleCount);
    Assert.NotEqual(graph.Edges[2].Conditions[0].RequiresPoi, graph.Edges[1].Conditions[0].RequiresPoi);
    GraphTestAssert.AssertBeatable(graph);
  }

  [Fact]
  public void TryPlaceObstacle_NeverPutsSolutionBehindItsOwnObstacle()
  {
    for (var seed = 0; seed < 60; seed++)
    {
      // Arrange: Chain with a first obstacle near the exit, its solution anywhere before it.
      var graph = CreateLevel(5, 1, 5, Chain(5));
      var state = new ObstacleState(graph, CnfProcObstaclePlacerOpt.Default, seed);
      Assert.True(state.TryPlaceObstacle([3], null, out var firstNode));

      // Act: A second obstacle right behind the start.
      Assert.True(state.TryPlaceObstacle([0], null, out var secondNode));

      // Assert: The second solution can only be in the start node; the first stays where it was.
      Assert.Equal(0, secondNode);
      Assert.InRange(firstNode, 0, 3);
      GraphTestAssert.AssertBeatable(graph, $"seed {seed}");
    }
  }

  [Fact]
  public void TryPlaceObstacle_IgnoresSecretPassagesWhenLookingForSolutionNodes()
  {
    for (var seed = 0; seed < 60; seed++)
    {
      // Arrange: Chain 1 - 2 - 3 plus a secret passage 1 - 3; the chain is locked right at the start.
      var graph = CreateLevel(3, 1, 3, new Passage(1, 2), new Passage(2, 3), new Passage(1, 3, Secret: true));
      var state = new ObstacleState(graph, CnfProcObstaclePlacerOpt.Default, seed);

      // Act: Lock only the ordinary passage 1 - 2.
      Assert.True(state.TryPlaceObstacle([0], null, out var solutionNode));

      // Assert: Nodes 2 and 3 are reachable through the secret passage only, so the solution
      // must not go there.
      Assert.Equal(0, solutionNode);
    }
  }

  [Fact]
  public void TryPlaceObstacle_WhenEveryNodeIsExcluded_RestoresTheGraph()
  {
    // Arrange: Chain whose first passage already carries a condition.
    var graph = CreateLevel(3, 1, 3, new Passage(1, 2, Conditions: 1), new Passage(2, 3));
    graph.Nodes[0].Pois.Add(GraphTestCreate.Poi(9000, EnPoiType.Solution));
    var state = CreateState(graph);

    // Act: Try to place an obstacle while no node may hold its solution.
    var placed = state.TryPlaceObstacle([0, 1], [true, true, true], out var solutionNode);

    // Assert: Nothing changed: the old condition is still alone, nothing was counted.
    Assert.False(placed);
    Assert.Equal(ObstacleWorkGraph.NoNode, solutionNode);
    Assert.Equal(0, state.ObstacleCount);
    Assert.Single(graph.Edges[0].Conditions);
    Assert.Empty(graph.Edges[1].Conditions);
    Assert.Single(graph.Nodes[0].Pois.FindAll(poi => poi.Type == EnPoiType.Solution));
  }

  [Fact]
  public void TryPlaceObstacle_WithBadPassages_Throws()
  {
    // Arrange: A full passage and a one-way passage.
    var graph = CreateLevel(3, 1, 3, new Passage(1, 2, Conditions: 3), new Passage(2, 3, OneWay: true));
    var state = CreateState(graph);

    // Act/Assert: Missing, empty and unlockable passage lists are rejected; nothing is written.
    Assert.Throws<ArgumentNullException>(() => state.TryPlaceObstacle(null!, null, out _));
    Assert.Throws<ArgumentException>(() => state.TryPlaceObstacle([], null, out _));
    Assert.Throws<InvalidOperationException>(() => state.TryPlaceObstacle([0], null, out _));
    Assert.Throws<InvalidOperationException>(() => state.TryPlaceObstacle([1], null, out _));
    Assert.Equal(3, graph.Edges[0].Conditions.Count);
    Assert.Empty(graph.Edges[1].Conditions);
  }

  [Fact]
  public void TryPlaceObstacle_WithOneWayAllowed_LocksOneWayPassageButNotFullOnes()
  {
    // Arrange: Chain 1 - 2 with a one-way detour 2 -> 3 -> 2; the way back is full.
    var graph = CreateLevel(
      3, 1, 2,
      new Passage(1, 2), new Passage(2, 3, OneWay: true), new Passage(3, 2, OneWay: true, Conditions: 3));
    var state = CreateState(graph);

    // Act: Lock the one-way entry with the flag set.
    var placed = state.TryPlaceObstacle([1], null, out var solutionNode, allowOneWay: true);

    // Assert: The entry is locked, its solution lies before it; the cap still applies.
    Assert.True(placed);
    Assert.InRange(solutionNode, 0, 1);
    Assert.Single(graph.Edges[1].Conditions);
    Assert.Throws<InvalidOperationException>(() => state.TryPlaceObstacle([2], null, out _, allowOneWay: true));
  }

  [Fact]
  public void TryPlaceObstacle_WithSameSeed_DerivesSameSolutionIds()
  {
    // Arrange: Two equal graphs and states.
    var first = CreateLevel(4, 1, 4, Chain(4));
    var second = CreateLevel(4, 1, 4, Chain(4));

    // Act: The same obstacle on both.
    CreateState(first).TryPlaceObstacle([2], null, out var firstNode);
    CreateState(second).TryPlaceObstacle([2], null, out var secondNode);

    // Assert: Same node, same derived identifier, never the empty one.
    Assert.Equal(firstNode, secondNode);
    Assert.Equal(first.Edges[2].Conditions[0].RequiresPoi, second.Edges[2].Conditions[0].RequiresPoi);
    Assert.NotEqual(Guid.Empty, first.Edges[2].Conditions[0].RequiresPoi);
  }
}
