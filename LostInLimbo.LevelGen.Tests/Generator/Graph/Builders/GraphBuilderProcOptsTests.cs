using LostInLimbo.LevelGen.Data.Config.Graph;
using LostInLimbo.LevelGen.Data.Config.Graph.Proc;
using LostInLimbo.LevelGen.Data.Result.Graph;

namespace LostInLimbo.LevelGen.Tests.Generator.Graph.Builders;

/// <summary>
/// Tests that the graph builders consume processor options from
/// <see cref="CnfGraph.ProcOpts"/> instead of the hardcoded defaults.
/// </summary>
/// <remarks>
/// Both finalizing builders run the same four processors, so the wiring tests cover
/// <c>Simple</c> and <c>Linear</c> alike. Count assertions only ever count up to the
/// requested number, because processor counts are soft when eligible nodes run out.
/// </remarks>
public sealed class GraphBuilderProcOptsTests
{
  [Theory]
  [InlineData(EnGraphBuilder.Simple)]
  [InlineData(EnGraphBuilder.Linear)]
  public void Build_WithZeroSecretAmount_ProducesNoSecretNodes(EnGraphBuilder builder)
  {
    // Arrange: Config disabling the secrets processor via proc options.
    var graphConfig = new CnfGraph
    {
      Size = 5,
      ProcOpts = CnfProcOpt.Default with
      {
        RelSecrets = new CnfProcRelSecretsOpt { Amount = 0 },
      },
    };

    // Act: Build through the factory like production dispatch does.
    var (result, graph) = GraphTestSupport.Build(builder, graphConfig);

    // Assert: No secret node exists and no secret edge was appended.
    Assert.True(result.Success);
    Assert.Empty(graph.Nodes.Where(node => node.Secret));
    Assert.Empty(graph.Edges.Where(edge => edge.Secret));
  }

  [Theory]
  [InlineData(EnGraphBuilder.Simple)]
  [InlineData(EnGraphBuilder.Linear)]
  public void Build_WithCustomSecretAmount_ProducesThatManySecretNodes(EnGraphBuilder builder)
  {
    // Arrange: Config raising the secret count above the default of one.
    var graphConfig = new CnfGraph
    {
      Size = 10,
      ProcOpts = CnfProcOpt.Default with
      {
        RelSecrets = new CnfProcRelSecretsOpt { Amount = 3 },
      },
    };

    // Act: Build through the factory like production dispatch does.
    var (result, graph) = GraphTestSupport.Build(builder, graphConfig);

    // Assert: The configured number of secret nodes exists.
    Assert.True(result.Success);
    Assert.Equal(3, graph.Nodes.Count(node => node.Secret));
  }

  [Theory]
  [InlineData(EnGraphBuilder.Simple)]
  [InlineData(EnGraphBuilder.Linear)]
  public void Build_WithZeroObjectiveAmounts_ProducesNoGoalPois(EnGraphBuilder builder)
  {
    // Arrange: Config disabling the objectives processor via proc options.
    var graphConfig = new CnfGraph
    {
      Size = 5,
      ProcOpts = CnfProcOpt.Default with
      {
        NodeObjectives = new CnfProcNodeObjectivesOpt { GoalAmount = 0, OptGoalAmount = 0 },
      },
    };

    // Act: Build through the factory like production dispatch does.
    var (result, graph) = GraphTestSupport.Build(builder, graphConfig);

    // Assert: Neither goal nor optional-goal POIs exist; start and exit still do.
    Assert.True(result.Success);
    Assert.Empty(GraphTestSupport.GetPois(graph, EnPoiType.Goal));
    Assert.Empty(GraphTestSupport.GetPois(graph, EnPoiType.OptionalGoal));
    Assert.Single(GraphTestSupport.GetPois(graph, EnPoiType.Start));
    Assert.Single(GraphTestSupport.GetPois(graph, EnPoiType.Exit));
  }

  [Theory]
  [InlineData(EnGraphBuilder.Simple)]
  [InlineData(EnGraphBuilder.Linear)]
  public void Build_WithCustomGoalAmount_ProducesThatManyGoalPois(EnGraphBuilder builder)
  {
    // Arrange: Config raising the mandatory goal count; enough nodes to host them.
    var graphConfig = new CnfGraph
    {
      Size = 10,
      ProcOpts = CnfProcOpt.Default with
      {
        NodeObjectives = new CnfProcNodeObjectivesOpt { GoalAmount = 3, OptGoalAmount = 0 },
      },
    };

    // Act: Build through the factory like production dispatch does.
    var (result, graph) = GraphTestSupport.Build(builder, graphConfig);

    // Assert: The requested mandatory goals exist (node capacity is ample) and no
    // optional goals were placed.
    Assert.True(result.Success);
    Assert.Equal(3, GraphTestSupport.GetPois(graph, EnPoiType.Goal).Count);
    Assert.Empty(GraphTestSupport.GetPois(graph, EnPoiType.OptionalGoal));
  }

  [Theory]
  [InlineData(EnGraphBuilder.Simple)]
  [InlineData(EnGraphBuilder.Linear)]
  public void Build_WithSingleRelSizeStep_AssignsMinSizeToEveryNode(EnGraphBuilder builder)
  {
    // Arrange: A single step at a distinctive size, biases off to satisfy the
    // single-step contradiction rule; secrets stay at the default of one.
    var graphConfig = new CnfGraph
    {
      Size = 5,
      ProcOpts = CnfProcOpt.Default with
      {
        NodeRelSize = new CnfProcNodeRelSizeOpt
        {
          Steps = 1,
          MinSize = 7,
          BiasRelationships = 0f,
          BiasPois = 0f,
        },
      },
    };

    // Act: Build through the factory like production dispatch does.
    var (result, graph) = GraphTestSupport.Build(builder, graphConfig);

    // Assert: Every node - including the secret one - carries the single step value.
    Assert.True(result.Success);
    Assert.All(graph.Nodes, node => Assert.Equal(7, node.RelSize));
  }

  [Theory]
  [InlineData(EnGraphBuilder.Simple)]
  [InlineData(EnGraphBuilder.Linear)]
  public void Build_WithDefaultRelSizeOpts_ProducesVaryingRelSizes(EnGraphBuilder builder)
  {
    // Act: Build moderately sized graphs across several seeds with the default options.
    var varyingGraphs = 0;
    for (var seed = 0; seed < 10; seed++)
    {
      var (_, graph) = GraphTestSupport.Build(builder, new CnfGraph
      {
        Size = 15,
        Connectivity = 3,
      }, seed);
      if (graph.Nodes.Select(node => node.RelSize).Distinct().Count() > 1) varyingGraphs++;
    }

    // Assert: Almost every graph uses more than one size - the pass must not collapse
    // onto a single value in practice.
    Assert.True(varyingGraphs >= 8, $"Only {varyingGraphs} of 10 graphs used more than one relative size.");
  }

  [Theory]
  [InlineData(EnGraphBuilder.Simple)]
  [InlineData(EnGraphBuilder.Linear)]
  public void Build_WithSingleGroup_AssignsOneGroupToEveryNode(EnGraphBuilder builder)
  {
    // Arrange: Config collapsing the grouping into a single group.
    var graphConfig = new CnfGraph
    {
      Size = 5,
      ProcOpts = CnfProcOpt.Default with
      {
        NodeStyle = new CnfProcNodeStyleOpt { GroupCount = 1 },
      },
    };

    // Act: Build through the factory like production dispatch does.
    var (result, graph) = GraphTestSupport.Build(builder, graphConfig);

    // Assert: Every node was grouped and all groups are identical.
    Assert.True(result.Success);
    Assert.All(graph.Nodes, node => Assert.NotNull(node.Style.Group));
    Assert.Single(graph.Nodes.Select(node => node.Style.Group).Distinct());
  }

  [Theory]
  [InlineData(EnGraphBuilder.Simple)]
  [InlineData(EnGraphBuilder.Linear)]
  public void Build_WithSameProcOptsAndSeed_ProducesIdenticalData(EnGraphBuilder builder)
  {
    // Arrange: Custom options for every processor and a fixed seed.
    var graphConfig = new CnfGraph
    {
      Size = 10,
      Connectivity = 3,
      ProcOpts = CnfProcOpt.Default with
      {
        RelSecrets = new CnfProcRelSecretsOpt { Amount = 2, ChanceSecretInSecret = 0.5f },
        NodeObjectives = new CnfProcNodeObjectivesOpt { GoalAmount = 2, OptGoalAmount = 1 },
        NodeRelSize = new CnfProcNodeRelSizeOpt { Steps = 4, Skew = 0.5f },
        NodeStyle = new CnfProcNodeStyleOpt { GroupCount = 2 },
      },
    };

    // Act: Build twice with the same config and seed.
    var (_, first) = GraphTestSupport.Build(builder, graphConfig);
    var (_, second) = GraphTestSupport.Build(builder, graphConfig);

    // Assert: Sizes, groups, POIs and edges are identical - options did not break
    // determinism.
    Assert.Equal(GraphTestSupport.GetAssignments(first), GraphTestSupport.GetAssignments(second));
    Assert.Equal(GraphTestSupport.GetGroupAssignments(first), GraphTestSupport.GetGroupAssignments(second));
    Assert.Equal(
      GraphTestSupport.GetPois(first, EnPoiType.Goal).Select(poi => poi.Id),
      GraphTestSupport.GetPois(second, EnPoiType.Goal).Select(poi => poi.Id));
    Assert.Equal(
      first.Edges.Select(edge => edge.Id),
      second.Edges.Select(edge => edge.Id));
  }
}
