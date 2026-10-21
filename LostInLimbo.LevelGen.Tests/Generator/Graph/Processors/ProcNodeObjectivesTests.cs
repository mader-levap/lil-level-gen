using LostInLimbo.LevelGen.Data.Config.Graph;
using LostInLimbo.LevelGen.Data.Result.Graph;
using LostInLimbo.LevelGen.Internal.Generator.Graph.Processors;
using static LostInLimbo.LevelGen.Tests.Generator.Graph.GraphTestCreate;
using static LostInLimbo.LevelGen.Tests.Generator.Graph.GraphTestSupport;

namespace LostInLimbo.LevelGen.Tests.Generator.Graph.Processors;

/// <summary>
/// Tests <see cref="ProcNodeObjectives"/>: option validation, placement shape, the
/// secret-node rules, one-goal-per-node, excess handling, determinism and the hand-off to
/// the later finalisation passes.
/// </summary>
/// <remarks>
/// The pass works on a finished <see cref="LilGraph"/>, so most tests construct graphs
/// directly instead of going through a builder - that keeps the eligible-node pool exactly
/// controlled per test. Secret-node expectations average over many seeds because the exact
/// host depends on the random stream.
/// </remarks>
public sealed class ProcNodeObjectivesTests
{
  /// <summary>Graph sub-seed used by all tests unless a test varies the seed itself.</summary>
  private const long Seed = GraphTestSupport.DefaultSeedGraph;

  /// <summary>Amount of seeds the statistical tests average over.</summary>
  private const int StatisticalSeedCount = 20;

  /// <summary>Tests for the hard constraints: nulls and out-of-range values.</summary>
  public sealed class ValidationTests
  {
    [Fact]
    public void Constructor_WithNullGraph_ThrowsArgumentNullException()
    {
      // Act & Assert: A missing graph is rejected.
      Assert.Throws<ArgumentNullException>(() => new ProcNodeObjectives(null!, CnfProcNodeObjectivesOpts.Default, Seed));
    }

    [Fact]
    public void Constructor_WithNullOptions_ThrowsArgumentNullException()
    {
      // Act & Assert: Missing options are rejected.
      Assert.Throws<ArgumentNullException>(() => new ProcNodeObjectives(CreateStartExitChainGraph(4), null!, Seed));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(-10)]
    public void Execute_WithNegativeGoalAmount_ThrowsArgumentOutOfRangeException(int amount)
    {
      // Arrange: Options demanding a forbidden goal count.
      var options = CnfProcNodeObjectivesOpts.Default with { GoalAmount = amount };

      // Act & Assert: The pass rejects the options before touching the graph.
      var exception = Assert.Throws<ArgumentOutOfRangeException>(
        () => new ProcNodeObjectives(CreateStartExitChainGraph(4), options, Seed).Execute());
      Assert.Equal("GoalAmount", exception.ParamName);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(-10)]
    public void Execute_WithNegativeOptGoalAmount_ThrowsArgumentOutOfRangeException(int amount)
    {
      // Arrange: Options demanding a forbidden optional goal count.
      var options = CnfProcNodeObjectivesOpts.Default with { OptGoalAmount = amount };

      // Act & Assert: The pass rejects the options before touching the graph.
      var exception = Assert.Throws<ArgumentOutOfRangeException>(
        () => new ProcNodeObjectives(CreateStartExitChainGraph(4), options, Seed).Execute());
      Assert.Equal("OptGoalAmount", exception.ParamName);
    }

    [Fact]
    public void Constructor_WithInvalidOptions_ThrowsBeforeExecute()
    {
      // Arrange: Options rejected by validation.
      var options = CnfProcNodeObjectivesOpts.Default with { GoalAmount = -1 };

      // Act & Assert: Construction alone rejects the options, before Execute can run.
      Assert.Throws<ArgumentOutOfRangeException>(
        () => new ProcNodeObjectives(CreateStartExitChainGraph(4), options, Seed));
    }

    [Fact]
    public void Execute_WithInvalidOptionsOnEmptyGraph_ThrowsArgumentOutOfRangeException()
    {
      // Arrange: Empty graph; validation must still run.
      var options = CnfProcNodeObjectivesOpts.Default with { OptGoalAmount = -1 };

      // Act & Assert: Bad options never pass silently, even without nodes.
      var exception = Assert.Throws<ArgumentOutOfRangeException>(
        () => new ProcNodeObjectives(new LilGraph(), options, Seed).Execute());
      Assert.Equal("OptGoalAmount", exception.ParamName);
    }
  }

  /// <summary>Tests for the placement shape: counts, node eligibility, one-goal-per-node.</summary>
  public sealed class BehaviorTests
  {
    [Fact]
    public void Execute_WithBothAmountsZero_LeavesGraphUntouched()
    {
      // Arrange: A chain graph and options that disable the pass.
      var graph = CreateStartExitChainGraph(4);
      var options = CnfProcNodeObjectivesOpts.Default with { GoalAmount = 0, OptGoalAmount = 0 };

      // Act: Run the pass.
      new ProcNodeObjectives(graph, options, Seed).Execute();

      // Assert: No objective POI was added; only Start and Exit remain.
      Assert.Equal(4, graph.Nodes.Count);
      Assert.Empty(GetPois(graph, EnPoiType.Goal));
      Assert.Empty(GetPois(graph, EnPoiType.OptionalGoal));
      Assert.Single(GetPois(graph, EnPoiType.Start));
      Assert.Single(GetPois(graph, EnPoiType.Exit));
    }

    [Fact]
    public void Execute_OnEmptyGraph_DoesNothing()
    {
      // Arrange: Empty graph; there is nothing to place onto.
      var graph = new LilGraph();

      // Act: Run the pass with the default options.
      new ProcNodeObjectives(graph, CnfProcNodeObjectivesOpts.Default, Seed).Execute();

      // Assert: The graph stays empty instead of failing.
      Assert.Empty(graph.Nodes);
      Assert.Empty(graph.Edges);
    }

    [Fact]
    public void Execute_WithGoalAmount_PlacesExactlyThatManyGoalsOnDistinctNodes()
    {
      // Arrange: A six-node chain without secrets.
      var graph = CreateStartExitChainGraph(6);
      var options = CnfProcNodeObjectivesOpts.Default with { GoalAmount = 3, OptGoalAmount = 0 };

      // Act: Place three mandatory goals.
      new ProcNodeObjectives(graph, options, Seed).Execute();

      // Assert: Exactly three goals exist, each on its own node.
      Assert.Equal(3, GetPois(graph, EnPoiType.Goal).Count);
      Assert.Equal(3, graph.Nodes.Count(node => node.Pois.Any(poi => poi.Type == EnPoiType.Goal)));
      Assert.Empty(GetPois(graph, EnPoiType.OptionalGoal));
    }

    [Fact]
    public void Execute_WithGoalAmountOverManySeeds_NeverPlacesGoalInSecretNode()
    {
      for (var seed = 0; seed < StatisticalSeedCount; seed++)
      {
        // Arrange: A chain graph with two appended secret nodes.
        var graph = CreateStartExitChainGraph(4);
        new ProcRelSecrets(graph, CnfProcRelSecretsOpts.Default with { Amount = 2 }, seed).Execute();
        var options = CnfProcNodeObjectivesOpts.Default with { GoalAmount = 100, OptGoalAmount = 0 };

        // Act: Demand far more goals than non-secret nodes exist.
        new ProcNodeObjectives(graph, options, seed).Execute();

        // Assert: Every goal sits on a non-secret node; the excess is ignored and the
        // count matches the non-secret node count exactly.
        Assert.Equal(4, GetPois(graph, EnPoiType.Goal).Count);
        foreach (var secret in graph.Nodes.Where(node => node.Secret))
        {
          // Assert: The secret node holds no goal POI of either type.
          Assert.DoesNotContain(secret.Pois, poi => poi.Type == EnPoiType.Goal);
          Assert.DoesNotContain(secret.Pois, poi => poi.Type == EnPoiType.OptionalGoal);
        }
      }
    }

    [Fact]
    public void Execute_WithOptGoalAmount_CoversEveryNodeIncludingSecrets()
    {
      // Arrange: A chain graph with two appended secret nodes; goals are disabled, so
      // every node is an eligible optional-goal host.
      var graph = CreateStartExitChainGraph(4);
      new ProcRelSecrets(graph, CnfProcRelSecretsOpts.Default with { Amount = 2 }, Seed).Execute();
      var options = CnfProcNodeObjectivesOpts.Default with { GoalAmount = 0, OptGoalAmount = 10 };

      // Act: Demand more optional goals than nodes exist.
      new ProcNodeObjectives(graph, options, Seed).Execute();

      // Assert: Every node - secret ones included - holds exactly one optional goal.
      Assert.Equal(6, GetPois(graph, EnPoiType.OptionalGoal).Count);
      foreach (var node in graph.Nodes)
      {
        // Assert: One optional goal per node, nothing else added.
        Assert.Single(node.Pois.Where(poi => poi.Type == EnPoiType.OptionalGoal));
      }
    }

    [Fact]
    public void Execute_WithCombinedAmounts_PlacesAtMostOneGoalPerNode()
    {
      // Arrange: A four-node chain without secrets; the optional amount alone would
      // saturate the graph, so the goal pass must claim its share first.
      var graph = CreateStartExitChainGraph(4);
      var options = CnfProcNodeObjectivesOpts.Default with { GoalAmount = 2, OptGoalAmount = 10 };

      // Act: Place two mandatory goals, then let optional goals fill the rest.
      new ProcNodeObjectives(graph, options, Seed).Execute();

      // Assert: The mandatory goals claim two nodes, optional goals fill the remaining
      // two and then stop - four nodes, four goal POIs, at most one per node.
      Assert.Equal(2, GetPois(graph, EnPoiType.Goal).Count);
      Assert.Equal(2, GetPois(graph, EnPoiType.OptionalGoal).Count);
      foreach (var node in graph.Nodes)
      {
        // Assert: Every node holds exactly one goal POI of either type in total.
        Assert.Single(node.Pois.Where(poi => poi.Type is EnPoiType.Goal or EnPoiType.OptionalGoal));
      }
    }

    [Fact]
    public void Execute_NodeWithStartPoi_IsEligibleForGoals()
    {
      // Arrange: A four-node chain; node 1 holds the Start POI and must still qualify.
      var graph = CreateStartExitChainGraph(4);
      var options = CnfProcNodeObjectivesOpts.Default with { GoalAmount = 4, OptGoalAmount = 0 };

      // Act: Demand one goal per node.
      new ProcNodeObjectives(graph, options, Seed).Execute();

      // Assert: The Start node hosts a goal POI next to its Start POI.
      var startNode = Assert.Single(graph.Nodes.Where(node => node.Pois.Any(poi => poi.Type == EnPoiType.Start)));
      Assert.Single(startNode.Pois.Where(poi => poi.Type == EnPoiType.Goal));
      Assert.Single(startNode.Pois.Where(poi => poi.Type == EnPoiType.Start));
    }

    [Fact]
    public void Execute_PlacedPois_HaveUniqueNonEmptyIdentifiers()
    {
      // Arrange: A chain graph with secrets, saturated with objectives.
      var graph = CreateStartExitChainGraph(4);
      new ProcRelSecrets(graph, CnfProcRelSecretsOpts.Default with { Amount = 2 }, Seed).Execute();
      var options = CnfProcNodeObjectivesOpts.Default with { GoalAmount = 10, OptGoalAmount = 10 };

      // Act: Demand more objectives than the graph can hold.
      new ProcNodeObjectives(graph, options, Seed).Execute();

      // Assert: Every POI identifier in the whole graph - builder and objectives alike -
      // is non-empty and unique.
      var poiIds = graph.Nodes.SelectMany(node => node.Pois).Select(poi => poi.Id).ToList();
      Assert.NotEmpty(poiIds);
      Assert.All(poiIds, id => Assert.NotEqual(Guid.Empty, id));
      Assert.Equal(poiIds.Count, poiIds.Distinct().Count());
    }
  }

  /// <summary>Tests for reproducibility of the placements.</summary>
  public sealed class DeterminismTests
  {
    [Fact]
    public void Execute_WithSameSeed_ProducesIdenticalGraphs()
    {
      // Arrange: Two identical chain graphs and the same inputs.
      var first = CreateStartExitChainGraph(5);
      var second = CreateStartExitChainGraph(5);
      var options = CnfProcNodeObjectivesOpts.Default with { GoalAmount = 2, OptGoalAmount = 2 };

      // Act: Run the pass on both graphs.
      new ProcNodeObjectives(first, options, Seed).Execute();
      new ProcNodeObjectives(second, options, Seed).Execute();

      // Assert: Nodes and POIs match one by one - host picks and identifiers included.
      GraphTestAssert.AssertEqualGraphs(first, second);
    }

    [Fact]
    public void Execute_WithDifferentSeeds_ProducesDifferentPlacements()
    {
      // Collect the placement sequences of many seeds.
      var sequences = new HashSet<string>();
      for (var seed = 0; seed < StatisticalSeedCount; seed++)
      {
        // Arrange: A chain graph with several objectives.
        var graph = CreateStartExitChainGraph(6);
        var options = CnfProcNodeObjectivesOpts.Default with { GoalAmount = 3, OptGoalAmount = 2 };

        // Act: Place the objectives and read back the hosting node ids in node order.
        new ProcNodeObjectives(graph, options, seed).Execute();
        var sequence = string.Join(",", graph.Nodes
          .Where(node => node.Pois.Any(poi => poi.Type is EnPoiType.Goal or EnPoiType.OptionalGoal))
          .Select(node => node.Id));
        sequences.Add(sequence);
      }

      // Assert: The seed drives the host picks - more than one distinct sequence exists.
      Assert.True(sequences.Count > 1, $"All {StatisticalSeedCount} seeds produced the same placement.");
    }
  }

  /// <summary>Tests for the hand-off to the passes that run after this one.</summary>
  public sealed class FinalisationTests
  {
    [Fact]
    public void Execute_AfterSecretsAndBeforeRelSizeAndStyle_GraphStaysValid()
    {
      // Arrange: A chain graph; the pass runs second, exactly like in the builders.
      var graph = CreateStartExitChainGraph(5);
      var options = CnfProcNodeObjectivesOpts.Default with { GoalAmount = 2, OptGoalAmount = 1 };

      // Act: Run the finalisation passes in builder order.
      new ProcRelSecrets(graph, CnfProcRelSecretsOpts.Default, Seed).Execute();
      new ProcNodeObjectives(graph, options, Seed).Execute();
      new ProcNodeRelSize(graph, CnfProcNodeRelSizeOpts.Default, Seed).Execute();
      new ProcNodeStyle(graph, CnfProcNodeStyleOpts.Default, Seed).Execute();

      // Assert: All three objectives survived the later passes.
      Assert.Equal(2, GetPois(graph, EnPoiType.Goal).Count);
      Assert.Single(GetPois(graph, EnPoiType.OptionalGoal));

      // Assert: No mandatory goal leaked into the secret node, and the complete graph
      // passes the beatability simulation.
      foreach (var secret in graph.Nodes.Where(node => node.Secret))
      {
        // Assert: The secret node holds no mandatory goal.
        Assert.DoesNotContain(secret.Pois, poi => poi.Type == EnPoiType.Goal);
      }
      GraphTestAssert.AssertBeatable(graph, "Finalised graph must stay connected.");
    }
  }

  //
  // Graph fixtures
  //

  /// <summary>
  /// Wraps the shared chain shape and adds the real Start and Exit POIs this suite needs:
  /// node 1 holds the start POI, the last node the exit POI, so the graph can be checked
  /// for beatability.
  /// </summary>
  /// <param name="nodeCount">Amount of nodes; at least 1.</param>
  /// <returns>The constructed graph with <c>nodeCount - 1</c> open edges.</returns>
  private static LilGraph CreateStartExitChainGraph(int nodeCount)
  {
    var graph = new LilGraph();
    for (var i = 1; i <= nodeCount; i++)
    {
      var pois = new List<LilPoi>();
      if (i == 1) pois.Add(Poi(1000 + i, EnPoiType.Start));
      if (i == nodeCount) pois.Add(Poi(2000 + i, EnPoiType.Exit));
      graph.Nodes.Add(Node(i, [.. pois]));
    }

    for (var i = 1; i < nodeCount; i++)
    {
      graph.Edges.Add(Edge(i, i, i + 1));
    }

    return graph;
  }
}
