using LostInLimbo.LevelGen.Data.Config;
using LostInLimbo.LevelGen.Data.Config.Graph;
using LostInLimbo.LevelGen.Data.Config.Graph.Validator;
using LostInLimbo.LevelGen.Data.Result;
using LostInLimbo.LevelGen.Data.Result.Graph;
using LostInLimbo.LevelGen.Internal.Generator;
using LostInLimbo.LevelGen.Internal.Generator.Graph;
using LostInLimbo.LevelGen.Internal.Generator.Graph.Builders;
using LostInLimbo.LevelGen.Internal.Generator.Graph.Meta;
using LostInLimbo.LevelGen.Internal.Generator.Graph.Validator;
using LostInLimbo.LevelGen.Internal.Generator.Prepass;
using static LostInLimbo.LevelGen.Tests.Generator.Graph.GraphTestCreate;

namespace LostInLimbo.LevelGen.Tests.Generator.Graph;

/// <summary>
/// Tests that the graph phase observes the cancellation token inside its passes, not only at
/// phase boundaries, and leaves no partial graph behind when cancelled.
/// </summary>
public sealed class GraphCancellationTests
{
  /// <summary>Graph sub-seed shared by the tests.</summary>
  private const long SeedGraph = 42;

  /// <summary>Config for the given builder with a small graph that has obstacles.</summary>
  /// <param name="builder">Builder to use.</param>
  /// <returns>Generation config with validation enabled.</returns>
  private static CnfLevelGeneration Config(EnGraphBuilder builder)
  {
    return CnfLevelGeneration.Default with
    {
      Graph = new CnfGraph
      {
        GraphBuilder = GraphTestSupport.CreateDefBuilder(builder),
        Size = 6,
        Connectivity = 2,
        Complexity = 2,
        ValidatorOpts = CnfValidGraphOpt.DefEnabled,
      },
    };
  }

  /// <summary>Returns a token that is already cancelled.</summary>
  private static CancellationToken CancelledToken()
  {
    using var source = new CancellationTokenSource();
    source.Cancel();
    return source.Token;
  }

  /// <summary>Tests <see cref="GraphGenerator.Execute"/> cancellation and cleanup.</summary>
  public sealed class GeneratorTests
  {
    [Theory]
    [InlineData(EnGraphBuilder.Simple)]
    [InlineData(EnGraphBuilder.Linear)]
    public void Execute_WithCancelledToken_ThrowsAndLeavesGraphEmpty(EnGraphBuilder builder)
    {
      // Arrange: Generator over a fresh world and an already cancelled token.
      var world = new LilWorld();
      var generator = new GraphGenerator(Config(builder), new PrepData { SeedGraph = SeedGraph }, world);

      // Act/Assert: The run is cancelled and no graph is left behind.
      Assert.ThrowsAny<OperationCanceledException>(() => generator.Execute(CancelledToken()));
      Assert.Empty(world.Graph.Nodes);
      Assert.Empty(world.Graph.Edges);
    }

    [Fact]
    public void Execute_WhenCancelledAfterBuild_ThrowsAndResetsBuiltGraph()
    {
      // Arrange: Generator whose test hook cancels the token once the graph is built.
      using var source = new CancellationTokenSource();
      var world = new LilWorld();
      var nodesAtHook = 0;
      var generator = new GraphGenerator(Config(EnGraphBuilder.Linear), new PrepData { SeedGraph = SeedGraph }, world)
      {
        AfterBuildHook = () =>
        {
          nodesAtHook = world.Graph.Nodes.Count;
          source.Cancel();
        },
      };

      // Act/Assert: The metadata pass observes the cancellation.
      Assert.ThrowsAny<OperationCanceledException>(() => generator.Execute(source.Token));

      // Assert: The graph was filled when cancelled, but the partial graph was discarded,
      // so a resumed run starts again from an empty graph.
      Assert.True(nodesAtHook > 0);
      Assert.Empty(world.Graph.Nodes);
      Assert.Empty(world.Graph.Edges);
    }

    [Theory]
    [InlineData(EnGraphBuilder.Simple)]
    [InlineData(EnGraphBuilder.Linear)]
    public void Execute_WithLiveToken_ProducesSameGraphAsDefaultToken(EnGraphBuilder builder)
    {
      // Arrange: Two identical generators; one gets a live token, the other none.
      using var source = new CancellationTokenSource();
      var withToken = new LilWorld();
      var withoutToken = new LilWorld();

      // Act: Run both.
      var resultWithToken = new GraphGenerator(Config(builder), new PrepData { SeedGraph = SeedGraph }, withToken).Execute(source.Token);
      var resultWithoutToken = new GraphGenerator(Config(builder), new PrepData { SeedGraph = SeedGraph }, withoutToken).Execute();

      // Assert: An uncancelled token changes nothing about the output.
      Assert.Equal(EnGenerationOutcome.Success, resultWithToken.Outcome);
      Assert.Equal(EnGenerationOutcome.Success, resultWithoutToken.Outcome);
      GraphTestAssert.AssertEqualGraphs(withoutToken.Graph, withToken.Graph);
    }
  }

  /// <summary>Tests that builders, metadata and validator each observe the token on their own.</summary>
  public sealed class PassTests
  {
    [Theory]
    [InlineData(EnGraphBuilder.Simple)]
    [InlineData(EnGraphBuilder.Linear)]
    public void Run_WithCancelledToken_ThrowsOperationCanceledException(EnGraphBuilder builder)
    {
      // Arrange: Fresh builder, empty graph and an already cancelled token.
      var config = Config(builder);
      var graphBuilder = GraphBuilderFactory.Create(config.Graph.GraphBuilder);

      // Act/Assert: The build is cancelled.
      Assert.ThrowsAny<OperationCanceledException>(
        () => graphBuilder.Run(config, new PrepData { SeedGraph = SeedGraph }, new LilGraph(), CancelledToken()));
    }

    [Fact]
    public void Generate_WithCancelledToken_ThrowsOperationCanceledException()
    {
      // Arrange: Metadata pass over a graph with a start POI.
      var metadata = new GraphMetadata(CreateStartExitChainGraph(4));

      // Act/Assert: The pass is cancelled.
      Assert.ThrowsAny<OperationCanceledException>(() => metadata.Generate(CancelledToken()));
    }

    [Fact]
    public void Validate_WithCancelledToken_ThrowsOperationCanceledException()
    {
      // Arrange: Enabled validator over a valid graph.
      var validator = new GraphValidator(CreateStartExitChainGraph(4), CnfValidGraphOpt.DefEnabled);

      // Act/Assert: Validation is cancelled.
      Assert.ThrowsAny<OperationCanceledException>(() => validator.Validate(CancelledToken()));
    }
  }
}
