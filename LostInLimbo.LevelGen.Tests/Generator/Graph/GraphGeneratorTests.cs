using LostInLimbo.LevelGen.Data.Config;
using LostInLimbo.LevelGen.Data.Config.Graph;
using LostInLimbo.LevelGen.Data.Prepass;
using LostInLimbo.LevelGen.Data.Result;
using LostInLimbo.LevelGen.Data.Result.Graph;
using LostInLimbo.LevelGen.Internal.Generator.Graph;
using LostInLimbo.LevelGen.Internal.Status;

namespace LostInLimbo.LevelGen.Tests.Generator.Graph;

/// <summary>
/// Tests graph generation dispatch: builder contract, placeholder boundary and the
/// single-node builder result.
/// </summary>
public sealed class GraphGeneratorTests
{
  [Theory]
  [InlineData(EnGraphBuilder.Simple)]
  [InlineData(EnGraphBuilder.Linear)]
  public void Execute_WithPlaceholderBuilders_ReturnsTrueAndLeavesGraphEmpty(EnGraphBuilder builder)
  {
    // Arrange: Generator for the given builder kind over a fresh world.
    var config = CnfLevelGeneration.Default with
    {
      Graph = new CnfGraph { Builder = builder },
    };
    var prepass = new PrepData();
    var world = new LilWorld();
    var generator = new GraphGenerator(config, prepass, world);

    // Act: Run graph generation.
    var success = generator.Execute();

    // Assert: The placeholder always succeeds; builders emit nothing yet, so the graph
    // stays empty. Phase advancement stays LevelGenerator's job.
    Assert.True(success);
    Assert.Empty(world.Graph.Nodes);
    Assert.Empty(world.Graph.Edges);
  }

  [Fact]
  public void Execute_WithOneNodeBuilder_ReturnsTrueAndProducesSingleNodeGraph()
  {
    // Arrange: Generator for the OneNode builder over a fresh world.
    var config = CnfLevelGeneration.Default with
    {
      Graph = new CnfGraph { Builder = EnGraphBuilder.OneNode },
    };
    var prepass = new PrepData();
    var world = new LilWorld();
    var generator = new GraphGenerator(config, prepass, world);

    // Act: Run graph generation.
    var success = generator.Execute();

    // Assert: The graph holds one node with start and exit POI and no edges.
    Assert.True(success);
    var node = Assert.Single(world.Graph.Nodes);
    Assert.Equal(2, node.Pois.Count);
    Assert.Contains(node.Pois, poi => poi.Type == EnPoiType.Start);
    Assert.Contains(node.Pois, poi => poi.Type == EnPoiType.Exit);
    Assert.Empty(world.Graph.Edges);
  }
}
