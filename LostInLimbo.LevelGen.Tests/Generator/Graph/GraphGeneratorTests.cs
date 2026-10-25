using LostInLimbo.LevelGen.Data.Config;
using LostInLimbo.LevelGen.Data.Config.Graph;
using LostInLimbo.LevelGen.Data.Config.Graph.Validator;
using LostInLimbo.LevelGen.Data.Result;
using LostInLimbo.LevelGen.Data.Result.Graph;
using LostInLimbo.LevelGen.Internal.Generator.Graph;
using LostInLimbo.LevelGen.Internal.Generator.Prepass;

namespace LostInLimbo.LevelGen.Tests.Generator.Graph;

/// <summary>
/// Tests graph generation dispatch: builder contract and the built graph shapes per builder.
/// </summary>
public sealed class GraphGeneratorTests
{
  [Fact]
  public void Execute_WithLinearBuilder_ReturnsTrueAndProducesChainGraph()
  {
    // Arrange: Generator for the Linear builder with a small configured graph.
    var config = CnfLevelGeneration.Default with
    {
      Graph = new CnfGraph { Builder = EnGraphBuilder.Linear, Size = 4, Connectivity = 2 },
    };
    var prepass = new PrepData { SeedGraph = 42 };
    var world = new LilWorld();
    var generator = new GraphGenerator(config, prepass, world);

    // Act: Run graph generation.
    var result = generator.Execute();

    // Assert: The graph holds the configured number of nodes plus the one secret node of
    // the finalization pass, the chain skeleton on top of which every requested extra
    // relationship was placed plus the secret relationship, and start/exit POIs.
    Assert.Equal(EnGenerationOutcome.Success, result.Outcome);
    Assert.Null(result.FailureReason);
    Assert.Equal(4 + 1, world.Graph.Nodes.Count);
    Assert.Equal(3 + 2 + 1, world.Graph.Edges.Count);
    Assert.Contains(world.Graph.Nodes, node => node.Pois.Any(poi => poi.Type == EnPoiType.Start));
    Assert.Contains(world.Graph.Nodes, node => node.Pois.Any(poi => poi.Type == EnPoiType.Exit));
  }

  [Fact]
  public void Execute_WithSimpleBuilder_ReturnsTrueAndProducesConfiguredGraph()
  {
    // Arrange: Generator for the Simple builder with a small configured graph.
    var config = CnfLevelGeneration.Default with
    {
      Graph = new CnfGraph { Builder = EnGraphBuilder.Simple, Size = 5, Connectivity = 3 },
    };
    var prepass = new PrepData { SeedGraph = 42 };
    var world = new LilWorld();
    var generator = new GraphGenerator(config, prepass, world);

    // Act: Run graph generation.
    var result = generator.Execute();

    // Assert: The graph holds the configured number of nodes plus the one secret node of
    // the finalization pass, a start and an exit POI.
    Assert.Equal(EnGenerationOutcome.Success, result.Outcome);
    Assert.Null(result.FailureReason);
    Assert.Equal(5 + 1, world.Graph.Nodes.Count);
    Assert.Contains(world.Graph.Nodes, node => node.Pois.Any(poi => poi.Type == EnPoiType.Start));
    Assert.Contains(world.Graph.Nodes, node => node.Pois.Any(poi => poi.Type == EnPoiType.Exit));
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
    var result = generator.Execute();

    // Assert: The graph holds one node with start and exit POI and no edges.
    Assert.Equal(EnGenerationOutcome.Success, result.Outcome);
    Assert.Null(result.FailureReason);
    var node = Assert.Single(world.Graph.Nodes);
    Assert.Equal(2, node.Pois.Count);
    Assert.Contains(node.Pois, poi => poi.Type == EnPoiType.Start);
    Assert.Contains(node.Pois, poi => poi.Type == EnPoiType.Exit);
    Assert.Empty(world.Graph.Edges);
  }

  [Fact]
  public void Execute_WithFailGraphBuildDebug_ReturnsBuildFailedWithReason()
  {
    // Arrange: Generator whose debug hook fails the build before anything is written.
    var config = CnfLevelGeneration.Default with
    {
      Debug = new CnfDebug { FailGraphBuild = true },
    };
    var prepass = new PrepData { SeedGraph = 42 };
    var world = new LilWorld();
    var generator = new GraphGenerator(config, prepass, world);

    // Act: Run graph generation.
    var result = generator.Execute();

    // Assert: The build failure is reported as such, carrying the builder's reason, so the
    // caller can tell it apart from a validation failure.
    Assert.Equal(EnGenerationOutcome.BuildFailed, result.Outcome);
    Assert.Equal("Deliberate failure at the very start.", result.FailureReason);
  }

  [Fact]
  public void Execute_WithFailGraphValidationDebug_ReturnsValidationFailedWithReason()
  {
    // Arrange: Generator whose debug hook fails validation after a successful build.
    var config = CnfLevelGeneration.Default with
    {
      Graph = new CnfGraph { Builder = EnGraphBuilder.OneNode },
      Debug = new CnfDebug { FailGraphValidation = true },
    };
    var prepass = new PrepData();
    var world = new LilWorld();
    var generator = new GraphGenerator(config, prepass, world);

    // Act: Run graph generation.
    var result = generator.Execute();

    // Assert: The build succeeded and the deliberate validation failure is reported as such.
    Assert.Equal(EnGenerationOutcome.ValidationFailed, result.Outcome);
    Assert.Equal("Deliberate validation failure.", result.FailureReason);
    Assert.Single(world.Graph.Nodes);
  }

  [Fact]
  public void Execute_WithValidationEnabled_ReturnsSuccessForValidGraph()
  {
    // Arrange: Generator running the real validator over a graph the builder produces.
    var config = CnfLevelGeneration.Default with
    {
      Graph = new CnfGraph { Builder = EnGraphBuilder.OneNode, ValidatorOpts = CnfValidGraphOpt.DefEnabled },
    };
    var prepass = new PrepData();
    var world = new LilWorld();
    var generator = new GraphGenerator(config, prepass, world);

    // Act: Run graph generation.
    var result = generator.Execute();

    // Assert: The built graph passes validation, so the run succeeds without a reason.
    Assert.Equal(EnGenerationOutcome.Success, result.Outcome);
    Assert.Null(result.FailureReason);
  }
}
