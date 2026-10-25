using LostInLimbo.LevelGen.Api;
using LostInLimbo.LevelGen.Data.Config;
using LostInLimbo.LevelGen.Data.Config.Graph;
using LostInLimbo.LevelGen.Data.Config.Graph.Builder;
using LostInLimbo.LevelGen.Data.Config.Graph.Validator;
using LostInLimbo.LevelGen.Data.Result;
using LostInLimbo.LevelGen.Data.Result.Graph;
using LostInLimbo.LevelGen.Tests.Generator.Graph;
using static LostInLimbo.LevelGen.Tests.Generator.Graph.GraphTestCreate;

namespace LostInLimbo.LevelGen.Tests.Api;

/// <summary>
/// Tests <see cref="LevelGraphValidation"/>: argument validation, delegation to the internal
/// graph validator and behavior on real builder output.
/// </summary>
/// <remarks>See <c>GraphValidatorTests</c> for the exhaustive validation-rule coverage.</remarks>
public sealed class LevelGraphValidationTests
{
  [Fact]
  public void Validate_NullWorld_ThrowsArgumentNullException()
  {
    // Arrange: No world at all.

    // Act: Try to validate the missing world.
    var act = () => LevelGraphValidation.Validate(null!, CnfValidGraphOpt.DefEnabled);

    // Assert: The argument is rejected up front.
    var exception = Assert.Throws<ArgumentNullException>(act);
    Assert.Equal("world", exception.ParamName);
  }

  [Fact]
  public void Validate_WorldWithEmptyGraph_Fails()
  {
    // Arrange: Fresh world whose graph generation never ran.
    var world = new LilWorld();

    // Act: Validate the not-yet-generated graph.
    var result = LevelGraphValidation.Validate(world, CnfValidGraphOpt.DefEnabled);

    // Assert: Failure reported through the public result shape.
    Assert.False(result.Success);
    Assert.Equal("Graph has no nodes.", result.FailureReason);
  }

  [Fact]
  public void Validate_WorldWithDisconnectedGraph_ReportsReason()
  {
    // Arrange: World holding a graph with an orphan node (exit sits on the start node).
    var world = new LilWorld
    {
      Graph = new LilGraph
      {
        Nodes =
        [
          Node(1, Poi(10, EnPoiType.Start), Poi(11, EnPoiType.Exit)),
          Node(2),
        ],
      },
    };

    // Act: Validate the disconnected graph.
    var result = LevelGraphValidation.Validate(world, CnfValidGraphOpt.DefEnabled);

    // Assert: The leftover node is reported.
    Assert.False(result.Success);
    Assert.Equal("1 node(s) unreachable from start POI.", result.FailureReason);
  }

  [Fact]
  public void Validate_WorldWithGeneratedOneNodeGraph_Succeeds()
  {
    // Arrange: Graph produced by the real OneNode builder through production dispatch.
    var graphConfig = CnfLevelGeneration.Default.Graph with { GraphBuilder = CnfGraphBuilder.Default with { Builder = EnGraphBuilder.OneNode } };
    var (_, graph) = GraphTestSupport.Build(EnGraphBuilder.OneNode, graphConfig);
    var world = new LilWorld { Graph = graph };

    // Act: Validate the generated world.
    var result = LevelGraphValidation.Validate(world, CnfValidGraphOpt.DefEnabled);

    // Assert: The generated graph is beatable.
    Assert.NotEmpty(graph.Nodes);
    Assert.True(result.Success);
    Assert.Null(result.FailureReason);
  }
}
