using LostInLimbo.LevelGen.Data.Config.Graph;
using LostInLimbo.LevelGen.Data.Result.Graph;

namespace LostInLimbo.LevelGen.Tests.Generator.Graph.Builders;

/// <summary>
/// Tests <see cref="OneNodeGraphBuilder"/>: graph shape, identifier validity and
/// determinism. The builder must ignore all graph config and never use randomness.
/// </summary>
/// <remarks>Shared build runner and POI queries live in <see cref="GraphTestSupport"/>.</remarks>
public sealed class GraphBuilderOneNodeTests
{
  [Fact]
  public void Build_WithDefaultConfig_ProducesSingleNodeWithStartAndExitAndNoEdges()
  {
    // Act: Build with default configuration.
    var (_, graph) = GraphTestSupport.Build(EnGraphBuilder.OneNode);

    // Assert: Exactly one node holding exactly the start and exit POI, and no passages.
    var node = Assert.Single(graph.Nodes);
    Assert.Equal(2, node.Pois.Count);
    Assert.Single(GraphTestSupport.GetPois(graph, EnPoiType.Start));
    Assert.Single(GraphTestSupport.GetPois(graph, EnPoiType.Exit));
    Assert.Empty(graph.Edges);
  }

  [Fact]
  public void Build_ProducesNonEmptyDistinctIdentifiers()
  {
    // Act: Build with default configuration.
    var (_, graph) = GraphTestSupport.Build(EnGraphBuilder.OneNode);

    // Assert: Node and both POI identifiers are set and pairwise distinct.
    var node = Assert.Single(graph.Nodes);
    var startPoi = Assert.Single(GraphTestSupport.GetPois(graph, EnPoiType.Start));
    var exitPoi = Assert.Single(GraphTestSupport.GetPois(graph, EnPoiType.Exit));
    Assert.NotEqual(Guid.Empty, node.Id);
    Assert.NotEqual(Guid.Empty, startPoi.Id);
    Assert.NotEqual(Guid.Empty, exitPoi.Id);
    Assert.NotEqual(startPoi.Id, exitPoi.Id);
    Assert.DoesNotContain(node.Id, new[] { startPoi.Id, exitPoi.Id });
  }

  [Fact]
  public void Build_WithSameSeed_ProducesIdenticalIdentifiers()
  {
    // Arrange: Two builds with the same graph sub-seed.
    // Act: Build both graphs.
    var first = GraphTestSupport.Build(EnGraphBuilder.OneNode);
    var second = GraphTestSupport.Build(EnGraphBuilder.OneNode);

    // Assert: Node and POI identifiers match across runs.
    Assert.Equal(Assert.Single(first.Graph.Nodes).Id, Assert.Single(second.Graph.Nodes).Id);
    Assert.Equal(
      Assert.Single(GraphTestSupport.GetPois(first.Graph, EnPoiType.Start)).Id,
      Assert.Single(GraphTestSupport.GetPois(second.Graph, EnPoiType.Start)).Id);
    Assert.Equal(
      Assert.Single(GraphTestSupport.GetPois(first.Graph, EnPoiType.Exit)).Id,
      Assert.Single(GraphTestSupport.GetPois(second.Graph, EnPoiType.Exit)).Id);
  }

  [Fact]
  public void Build_WithDifferentSeed_ProducesDifferentIdentifiers()
  {
    // Act: Build the same shape from two different graph sub-seeds.
    var first = GraphTestSupport.Build(EnGraphBuilder.OneNode, seedGraph: 1);
    var second = GraphTestSupport.Build(EnGraphBuilder.OneNode, seedGraph: 2);

    // Assert: Identifiers change with the seed even though the shape does not.
    Assert.NotEqual(Assert.Single(first.Graph.Nodes).Id, Assert.Single(second.Graph.Nodes).Id);
    Assert.NotEqual(
      Assert.Single(GraphTestSupport.GetPois(first.Graph, EnPoiType.Start)).Id,
      Assert.Single(GraphTestSupport.GetPois(second.Graph, EnPoiType.Start)).Id);
    Assert.NotEqual(
      Assert.Single(GraphTestSupport.GetPois(first.Graph, EnPoiType.Exit)).Id,
      Assert.Single(GraphTestSupport.GetPois(second.Graph, EnPoiType.Exit)).Id);
  }

  [Theory]
  [InlineData(1, 0, 0)]
  [InlineData(1, 0, 5)]
  [InlineData(10, 5, 0)]
  [InlineData(100, 10, 50)]
  public void Build_WithAnyGraphConfig_ProducesIdenticalGraph(int size, int connectivity, int complexity)
  {
    // Arrange: Reference graph from default config and a graph with the given knobs set.
    var reference = GraphTestSupport.Build(EnGraphBuilder.OneNode);
    var varied = GraphTestSupport.Build(EnGraphBuilder.OneNode, new CnfGraph
    {
      Size = size,
      Connectivity = connectivity,
      Complexity = complexity,
    });

    // Assert: Size, connectivity and complexity are ignored - same shape, same identifiers.
    var referenceNode = Assert.Single(reference.Graph.Nodes);
    var variedNode = Assert.Single(varied.Graph.Nodes);
    Assert.Equal(referenceNode.Id, variedNode.Id);
    Assert.Equal(
      Assert.Single(GraphTestSupport.GetPois(reference.Graph, EnPoiType.Start)).Id,
      Assert.Single(GraphTestSupport.GetPois(varied.Graph, EnPoiType.Start)).Id);
    Assert.Equal(
      Assert.Single(GraphTestSupport.GetPois(reference.Graph, EnPoiType.Exit)).Id,
      Assert.Single(GraphTestSupport.GetPois(varied.Graph, EnPoiType.Exit)).Id);
    Assert.Equal(2, variedNode.Pois.Count);
    Assert.Empty(varied.Graph.Edges);
  }
}
