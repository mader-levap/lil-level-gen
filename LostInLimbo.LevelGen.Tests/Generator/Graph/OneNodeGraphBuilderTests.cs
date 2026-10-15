using LostInLimbo.LevelGen.Data.Config;
using LostInLimbo.LevelGen.Data.Config.Graph;
using LostInLimbo.LevelGen.Data.Prepass;
using LostInLimbo.LevelGen.Data.Result.Graph;
using LostInLimbo.LevelGen.Internal.Generator.Graph;

namespace LostInLimbo.LevelGen.Tests.Generator.Graph;

/// <summary>
/// Tests <see cref="OneNodeGraphBuilder"/>: graph shape, identifier validity and
/// determinism. The builder must ignore all graph config and never use randomness.
/// </summary>
public sealed class OneNodeGraphBuilderTests
{
  /// <summary>Default sub-seed used by the tests unless a test needs another one.</summary>
  private const long TestSeedGraph = 42;

  /// <summary>
  /// Builds a graph through the factory-selected OneNode builder and returns the result.
  /// </summary>
  /// <param name="graphConfig">Graph config to use; defaults to <see cref="CnfGraph.Default"/>.</param>
  /// <param name="seedGraph">Graph sub-seed to use.</param>
  private static LilGraph Build(CnfGraph? graphConfig = null, long seedGraph = TestSeedGraph)
  {
    // Arrange: Fresh builder from the factory, config and an empty target graph.
    var builder = GraphBuilderFactory.Create(EnGraphBuilder.OneNode);
    var config = CnfLevelGeneration.Default with { Graph = graphConfig ?? CnfGraph.Default };
    var prepData = new PrepData { SeedGraph = seedGraph };
    var graph = new LilGraph();

    // Act: Hand over the inputs, then build.
    builder.Setup(config, prepData, graph);
    builder.Build();

    // Assert-adjacent: The builder reports success; shape assertions live in the tests.
    return graph;
  }

  /// <summary>Returns the single POI of the given type from a single-node graph.</summary>
  private static LilPoi GetPoi(LilGraph graph, EnPoiType type)
  {
    return Assert.Single(graph.Nodes[0].Pois.Where(poi => poi.Type == type));
  }

  [Fact]
  public void Build_WithDefaultConfig_ProducesSingleNodeWithStartAndExitAndNoEdges()
  {
    // Act: Build with default configuration.
    var graph = Build();

    // Assert: Exactly one node holding exactly the start and exit POI, and no passages.
    var node = Assert.Single(graph.Nodes);
    Assert.Equal(2, node.Pois.Count);
    Assert.Single(node.Pois.Where(poi => poi.Type == EnPoiType.Start));
    Assert.Single(node.Pois.Where(poi => poi.Type == EnPoiType.Exit));
    Assert.Empty(graph.Edges);
  }

  [Fact]
  public void Build_ProducesNonEmptyDistinctIdentifiers()
  {
    // Act: Build with default configuration.
    var graph = Build();

    // Assert: Node and both POI identifiers are set and pairwise distinct.
    var node = Assert.Single(graph.Nodes);
    Assert.NotEqual(Guid.Empty, node.Id);
    Assert.NotEqual(Guid.Empty, GetPoi(graph, EnPoiType.Start).Id);
    Assert.NotEqual(Guid.Empty, GetPoi(graph, EnPoiType.Exit).Id);
    Assert.NotEqual(GetPoi(graph, EnPoiType.Start).Id, GetPoi(graph, EnPoiType.Exit).Id);
    Assert.DoesNotContain(node.Id, new[] { GetPoi(graph, EnPoiType.Start).Id, GetPoi(graph, EnPoiType.Exit).Id });
  }

  [Fact]
  public void Build_WithSameSeed_ProducesIdenticalIdentifiers()
  {
    // Arrange: Two builds with the same graph sub-seed.
    // Act: Build both graphs.
    var first = Build();
    var second = Build();

    // Assert: Node and POI identifiers match across runs.
    Assert.Equal(Assert.Single(first.Nodes).Id, Assert.Single(second.Nodes).Id);
    Assert.Equal(GetPoi(first, EnPoiType.Start).Id, GetPoi(second, EnPoiType.Start).Id);
    Assert.Equal(GetPoi(first, EnPoiType.Exit).Id, GetPoi(second, EnPoiType.Exit).Id);
  }

  [Fact]
  public void Build_WithDifferentSeed_ProducesDifferentIdentifiers()
  {
    // Act: Build the same shape from two different graph sub-seeds.
    var first = Build(seedGraph: 1);
    var second = Build(seedGraph: 2);

    // Assert: Identifiers change with the seed even though the shape does not.
    Assert.NotEqual(Assert.Single(first.Nodes).Id, Assert.Single(second.Nodes).Id);
    Assert.NotEqual(GetPoi(first, EnPoiType.Start).Id, GetPoi(second, EnPoiType.Start).Id);
    Assert.NotEqual(GetPoi(first, EnPoiType.Exit).Id, GetPoi(second, EnPoiType.Exit).Id);
  }

  [Theory]
  [InlineData(1, 0, 0)]
  [InlineData(1, 0, 5)]
  [InlineData(10, 5, 0)]
  [InlineData(100, 10, 50)]
  public void Build_WithAnyGraphConfig_ProducesIdenticalGraph(int size, int connectivity, int complexity)
  {
    // Arrange: Reference graph from default config and a graph with the given knobs set.
    var reference = Build();
    var varied = Build(new CnfGraph { Size = size, Connectivity = connectivity, Complexity = complexity });

    // Assert: Size, connectivity and complexity are ignored - same shape, same identifiers.
    var referenceNode = Assert.Single(reference.Nodes);
    var variedNode = Assert.Single(varied.Nodes);
    Assert.Equal(referenceNode.Id, variedNode.Id);
    Assert.Equal(GetPoi(reference, EnPoiType.Start).Id, GetPoi(varied, EnPoiType.Start).Id);
    Assert.Equal(GetPoi(reference, EnPoiType.Exit).Id, GetPoi(varied, EnPoiType.Exit).Id);
    Assert.Equal(2, variedNode.Pois.Count);
    Assert.Empty(varied.Edges);
  }
}
