using LostInLimbo.LevelGen.Data.Config;
using LostInLimbo.LevelGen.Data.Config.Graph;
using LostInLimbo.LevelGen.Data.Prepass;
using LostInLimbo.LevelGen.Data.Result;
using LostInLimbo.LevelGen.Internal.Generator.Graph;
using LostInLimbo.LevelGen.Internal.Status;

namespace LostInLimbo.LevelGen.Tests.Generator.Graph;

/// <summary>
/// Tests graph generation dispatch: placeholder contract and the cancellation boundary.
/// </summary>
public sealed class GraphGeneratorTests
{
  [Theory]
  [InlineData(EnGraphBuilder.Simple)]
  [InlineData(EnGraphBuilder.Linear)]
  public void Execute_WithEachDefinedBuilder_ReturnsTrueAndLeavesGraphEmpty(EnGraphBuilder builder)
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
}
