using LostInLimbo.LevelGen.Data.Config;
using LostInLimbo.LevelGen.Data.Config.Graph;
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
    var world = new LilWorld();
    var generator = new GraphGenerator(config, new GenerationStatusStore(), world);

    // Act: Run graph generation.
    var success = generator.Execute();

    // Assert: The placeholder always succeeds; builders emit nothing yet, so the graph
    // stays empty. Phase advancement stays LevelGenerator's job.
    Assert.True(success);
    Assert.Empty(world.Graph.Nodes);
    Assert.Empty(world.Graph.Edges);
  }

  [Fact]
  public void Execute_WithPreCancelledToken_ThrowsOperationCanceledException()
  {
    // Arrange: Generator with default builder and a token cancelled before the run.
    var config = CnfLevelGeneration.Default;
    var generator = new GraphGenerator(
      config, new GenerationStatusStore(), new LilWorld());
    using var cancellationTokenSource = new CancellationTokenSource();
    cancellationTokenSource.Cancel();

    // Act/Assert: The pass boundary rejects the cancelled token before any building starts.
    Assert.Throws<OperationCanceledException>(
      () => generator.Execute(cancellationTokenSource.Token));
  }
}
