using LostInLimbo.LevelGen.Data.Config.Graph;
using LostInLimbo.LevelGen.Data.Result.Graph;
using LostInLimbo.LevelGen.Internal.Generator.Graph.Builders;
using LostInLimbo.LevelGen.Internal.Generator.Graph.Processors;

namespace LostInLimbo.LevelGen.Tests.Generator.Graph.Builders;

/// <summary>
/// Tests <see cref="GraphBuilderSimple"/>: graph shape per scenario, soft-constraint
/// behavior, edge validity, determinism and POI placement.
/// </summary>
/// <remarks>
/// The builder must succeed for any validated config: size, connectivity and complexity are
/// soft constraints, so most tests assert shape outcomes rather than failure modes.
/// </remarks>
public sealed class GraphBuilderSimpleTests
{
  /// <summary>Mirrors <c>SimpleGraphBuilder.MaxDegree</c>: cap of the random-relationships phase.</summary>
  private const int MaxDegree = GraphBuilderSimple.MaxDegree;

  /// <summary>Mirrors <c>SimpleGraphBuilder.MaxDegreeStar</c>: cap of the skeleton phase.</summary>
  private const int MaxDegreeStar = GraphBuilderSimple.MaxDegreeStar;

  /// <summary>Tests for the skeleton shape: node count, minimality, connectivity, scenarios.</summary>
  public sealed class ShapeTests
  {
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(50)]
    [InlineData(100)]
    public void Build_WithAnySize_ProducesConfiguredNodeCount(int size)
    {
      // Act: Build a graph of the given size.
      var (success, graph) = GraphTestSupport.Build(EnGraphBuilder.Simple, new CnfGraph { Size = size });

      // Assert: The node count follows the size setting exactly.
      Assert.True(success);
      Assert.Equal(size, graph.Nodes.Count);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(10)]
    [InlineData(100)]
    public void Build_WithZeroConnectivity_ProducesMinimalConnectedGraph(int size)
    {
      // Act: Build a graph without any extra relationships.
      var (success, graph) = GraphTestSupport.Build(EnGraphBuilder.Simple, new CnfGraph { Size = size });

      // Assert: The skeleton is a tree - size minus one relationships - and connected,
      // no matter which scenario was rolled.
      Assert.True(success);
      Assert.Equal(Math.Max(0, size - 1), graph.Edges.Count);
      GraphTestSupport.AssertBeatable(graph);
    }

    [Theory]
    [InlineData(10, 3)]
    [InlineData(5, 4)]
    [InlineData(20, 8)]
    [InlineData(100, 50)]
    public void Build_WithConnectivityBelowCapacity_ConsumesAllConnectivity(int size, int connectivity)
    {
      // Act: Build a graph whose requested extras fit under the degree cap.
      var (success, graph) = GraphTestSupport.Build(EnGraphBuilder.Simple, new CnfGraph
      {
        Size = size,
        Connectivity = connectivity,
      });

      // Assert: Every requested extra relationship exists on top of the tree, and the
      // graph stays connected.
      Assert.True(success);
      Assert.Equal(size - 1 + connectivity, graph.Edges.Count);
      GraphTestSupport.AssertBeatable(graph);
    }

    [Fact]
    public void Build_WithVariousSeeds_ObservesDifferentSkeletonShapes()
    {
      // Act: Build many minimal graphs of the same size and look at the highest degree.
      var maxDegrees = new List<int>();
      for (var seed = 0; seed < 30; seed++)
      {
        var (_, graph) = GraphTestSupport.Build(EnGraphBuilder.Simple, new CnfGraph { Size = 10 }, seed);
        maxDegrees.Add(GraphTestSupport.GetDegrees(graph).Values.Max());
      }

      // Assert: At least one seed produced a plain chain (max degree 2) and at least one
      // a star or mixed skeleton (max degree above 2), so multiple scenarios are reachable.
      Assert.Contains(maxDegrees, maxDegree => maxDegree == 2);
      Assert.Contains(maxDegrees, maxDegree => maxDegree > 2);
    }
  }

  /// <summary>Tests for soft constraints: degree cap, oversized connectivity, edge validity.</summary>
  public sealed class ConstraintTests
  {
    [Theory]
    [InlineData(1, 0)]
    [InlineData(1, 100)]
    [InlineData(2, 50)]
    [InlineData(3, 0)]
    [InlineData(5, 1000)]
    [InlineData(100, 0)]
    [InlineData(100, 100000)]
    public void Build_WithAnyConfig_Succeeds(int size, int connectivity)
    {
      // Act: Build with configs ranging from trivial to far beyond capacity.
      var (success, graph) = GraphTestSupport.Build(EnGraphBuilder.Simple, new CnfGraph
      {
        Size = size,
        Connectivity = connectivity,
      });

      // Assert: The builder never fails - unsatisfiable settings are stretched silently.
      Assert.True(success);
      Assert.Equal(size, graph.Nodes.Count);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(10)]
    public void Build_WithSizeOne_WithAnyConnectivity_ProducesSingleNodeWithBothPois(int connectivity)
    {
      // Act: Build the degenerate one-node graph with and without extra connectivity.
      var (success, graph) = GraphTestSupport.Build(EnGraphBuilder.Simple, new CnfGraph
      {
        Size = 1,
        Connectivity = connectivity,
      });

      // Assert: No relationships are possible, yet both POIs live on the single node.
      Assert.True(success);
      var node = Assert.Single(graph.Nodes);
      Assert.Empty(graph.Edges);
      Assert.Single(node.Pois.Where(poi => poi.Type == EnPoiType.Start));
      Assert.Single(node.Pois.Where(poi => poi.Type == EnPoiType.Exit));
    }

    [Fact]
    public void Build_WithExcessiveConnectivityAtSmallSize_ObeysDegreeCap()
    {
      // Act: Ask for far more relationships than a five-node graph can hold.
      var (success, graph) = GraphTestSupport.Build(EnGraphBuilder.Simple, new CnfGraph
      {
        Size = 5,
        Connectivity = 10000,
      });

      // Assert: The build succeeds, no node exceeds the plain degree cap (at size five
      // even a skeleton central collects at most four nodes) and the complete graph
      // bound is respected.
      Assert.True(success);
      GraphTestSupport.AssertDegreeCap(graph, MaxDegree);
      Assert.True(graph.Edges.Count <= 10, "Five nodes cannot hold more than ten relationships.");
      GraphTestSupport.AssertValidEdges(graph);
      GraphTestSupport.AssertOpenPassages(graph);
      GraphTestSupport.AssertBeatable(graph);
    }

    [Fact]
    public void Build_WithExcessiveConnectivityAtLargeSize_SucceedsWithValidStructure()
    {
      // Act: Ask for far more relationships than a twenty-node graph can hold.
      var (success, graph) = GraphTestSupport.Build(EnGraphBuilder.Simple, new CnfGraph
      {
        Size = 20,
        Connectivity = 10000,
      });

      // Assert: The build succeeds, keeps at least the skeleton, stays structurally
      // valid and never exceeds the skeleton degree cap - central handover keeps even
      // star-shaped skeletons bounded.
      Assert.True(success);
      Assert.True(graph.Edges.Count >= 19, "The skeleton alone already holds 19 relationships.");
      Assert.True(graph.Edges.Count <= 190, "Twenty nodes cannot hold more than 190 relationships.");
      GraphTestSupport.AssertDegreeCap(graph, MaxDegreeStar);
      GraphTestSupport.AssertValidEdges(graph);
      GraphTestSupport.AssertOpenPassages(graph);
      GraphTestSupport.AssertBeatable(graph);
    }

    [Theory]
    [InlineData(6)]
    [InlineData(8)]
    [InlineData(10)]
    [InlineData(50)]
    [InlineData(100)]
    public void Build_WithAnySeed_ProducesBoundedConnectedSkeleton(int size)
    {
      // Act: Build minimal skeletons across many seeds; every scenario must respect the
      // skeleton degree cap and stay a connected tree.
      for (var seed = 0; seed < 30; seed++)
      {
        var (success, graph) = GraphTestSupport.Build(EnGraphBuilder.Simple, new CnfGraph { Size = size }, seed);

        // Assert: Success, exactly size minus one relationships, connectivity and the
        // cap hold no matter which scenario was rolled.
        Assert.True(success);
        Assert.Equal(size - 1, graph.Edges.Count);
        GraphTestSupport.AssertDegreeCap(graph, MaxDegreeStar);
        GraphTestSupport.AssertBeatable(graph, $"Skeleton graph must be connected (size {size}, seed {seed}).");
      }
    }

    [Fact]
    public void Build_WithLargeSizeAcrossSeeds_SkeletonCentralHandsOverAtCap()
    {
      // Act: Build 100-node skeletons across many seeds; star and mixed rolls fill a
      // central node to the cap and must hand over instead of growing beyond it.
      var maxDegrees = new List<int>();
      for (var seed = 0; seed < 30; seed++)
      {
        var (_, graph) = GraphTestSupport.Build(EnGraphBuilder.Simple, new CnfGraph { Size = 100 }, seed);
        maxDegrees.Add(GraphTestSupport.GetDegrees(graph).Values.Max());
      }

      // Assert: At least one seed reached the cap exactly - proving the handover path
      // was exercised - and no seed ever went beyond it.
      Assert.Contains(maxDegrees, maxDegree => maxDegree == MaxDegreeStar);
      Assert.All(maxDegrees, maxDegree => Assert.True(maxDegree <= MaxDegreeStar, $"Max degree {maxDegree} exceeds cap {MaxDegreeStar}."));
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(2, 2)]
    [InlineData(10, 0)]
    [InlineData(10, 5)]
    [InlineData(100, 30)]
    public void Build_WithAnyConfig_ProducesValidGraphStructure(int size, int connectivity)
    {
      // Act: Build a graph across sizes and connectivity levels.
      var (success, graph) = GraphTestSupport.Build(EnGraphBuilder.Simple, new CnfGraph
      {
        Size = size,
        Connectivity = connectivity,
      });

      // Assert: Edges are well-formed, all passages open and every node uses one of the
      // default relative-size steps.
      Assert.True(success);
      GraphTestSupport.AssertValidEdges(graph);
      GraphTestSupport.AssertOpenPassages(graph);
      var stepValues = ProcNodeRelSizeOpt.Default.BuildStepValues();
      Assert.All(graph.Nodes, node => Assert.Contains(node.RelSize, stepValues));
    }
  }

  /// <summary>Tests for the relative-size distribution pass run at the end of the build.</summary>
  public sealed class RelSizeTests
  {
    [Fact]
    public void Build_WithDefaults_ProducesVaryingRelSizes()
    {
      // Act: Build moderately sized graphs across several seeds.
      var varyingGraphs = 0;
      for (var seed = 0; seed < 10; seed++)
      {
        var (_, graph) = GraphTestSupport.Build(EnGraphBuilder.Simple, new CnfGraph
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

    [Fact]
    public void Build_WithSameSeed_ProducesSameRelSizes()
    {
      // Arrange: Same config and seed for two builds.
      var config = new CnfGraph { Size = 20, Connectivity = 8 };

      // Act: Build both graphs.
      var first = GraphTestSupport.Build(EnGraphBuilder.Simple, config);
      var second = GraphTestSupport.Build(EnGraphBuilder.Simple, config);

      // Assert: The distribution is part of the deterministic output, node by node.
      var firstSizes = first.Graph.Nodes.Select(node => node.RelSize).ToList();
      var secondSizes = second.Graph.Nodes.Select(node => node.RelSize).ToList();
      Assert.Equal(firstSizes, secondSizes);
    }
  }

  /// <summary>Tests for reproducibility of the generated graph.</summary>
  public sealed class DeterminismTests
  {
    [Fact]
    public void Build_WithSameSeed_ProducesIdenticalGraphs()
    {
      // Arrange: Same config and same seed for two builds.
      var config = new CnfGraph { Size = 12, Connectivity = 6 };

      // Act: Build both graphs.
      var first = GraphTestSupport.Build(EnGraphBuilder.Simple, config);
      var second = GraphTestSupport.Build(EnGraphBuilder.Simple, config);

      // Assert: Nodes, edges and POIs match one by one.
      Assert.Equal(first.Success, second.Success);
      GraphTestSupport.AssertEqualGraphs(first.Graph, second.Graph);
    }

    [Fact]
    public void Build_WithDifferentSeed_ProducesDifferentGraphs()
    {
      // Arrange: Same config, different seeds.
      var config = new CnfGraph { Size = 12, Connectivity = 6 };

      // Act: Build both graphs.
      var first = GraphTestSupport.Build(EnGraphBuilder.Simple, config, seedGraph: 1);
      var second = GraphTestSupport.Build(EnGraphBuilder.Simple, config, seedGraph: 2);

      // Assert: Node identity changes with the seed.
      Assert.NotEqual(first.Graph.Nodes[0].Id, second.Graph.Nodes[0].Id);
      Assert.NotEqual(first.Graph, second.Graph);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    [InlineData(50)]
    public void Build_WithAnyComplexity_ProducesIdenticalGraph(int complexity)
    {
      // Arrange: Reference build and builds with the complexity knob turned up.
      var reference = GraphTestSupport.Build(EnGraphBuilder.Simple, new CnfGraph { Size = 8, Connectivity = 4 });
      var varied = GraphTestSupport.Build(EnGraphBuilder.Simple, new CnfGraph
      {
        Size = 8,
        Connectivity = 4,
        Complexity = complexity,
      });

      // Assert: Complexity is ignored - same graph in every run.
      GraphTestSupport.AssertEqualGraphs(reference.Graph, varied.Graph);
    }

    [Fact]
    public void Build_ProducesDistinctIdentifiers()
    {
      // Act: Build a graph with many nodes and relationships.
      var (_, graph) = GraphTestSupport.Build(EnGraphBuilder.Simple, new CnfGraph { Size = 50, Connectivity = 20 });

      // Assert: All identifiers are non-empty, unique per collection and POIs do not
      // collide with nodes.
      var nodeIds = graph.Nodes.Select(node => node.Id).ToList();
      Assert.All(nodeIds, id => Assert.NotEqual(Guid.Empty, id));
      Assert.Equal(nodeIds.Count, nodeIds.Distinct().Count());

      var edgeIds = graph.Edges.Select(edge => edge.Id).ToList();
      Assert.All(edgeIds, id => Assert.NotEqual(Guid.Empty, id));
      Assert.Equal(edgeIds.Count, edgeIds.Distinct().Count());

      var poiIds = graph.Nodes.SelectMany(node => node.Pois).Select(poi => poi.Id).ToList();
      Assert.All(poiIds, id => Assert.NotEqual(Guid.Empty, id));
      Assert.Equal(poiIds.Count, poiIds.Distinct().Count());
      Assert.DoesNotContain(poiIds[0], nodeIds);
      Assert.DoesNotContain(poiIds[1], nodeIds);
    }
  }

  /// <summary>Tests for start and exit placement.</summary>
  public sealed class PoiTests
  {
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(5)]
    [InlineData(10)]
    public void Build_AlwaysPlacesExactlyOneStartAndOneExit(int size)
    {
      // Act: Build a graph of the given size.
      var (_, graph) = GraphTestSupport.Build(EnGraphBuilder.Simple, new CnfGraph { Size = size });

      // Assert: Exactly two POIs exist across the graph, one of each required type.
      var pois = graph.Nodes.SelectMany(node => node.Pois).ToList();
      Assert.Equal(2, pois.Count);
      Assert.Single(pois.Where(poi => poi.Type == EnPoiType.Start));
      Assert.Single(pois.Where(poi => poi.Type == EnPoiType.Exit));
      Assert.All(pois, poi => Assert.NotEqual(Guid.Empty, poi.Id));
    }

    [Fact]
    public void Build_WithSameNodeAllowed_MayPlaceStartAndExitTogetherOrApart()
    {
      // Act: Build many two-node graphs and record whether start and exit share a node.
      var outcomes = new List<bool>();
      for (var seed = 0; seed < 50; seed++)
      {
        var (_, graph) = GraphTestSupport.Build(EnGraphBuilder.Simple, new CnfGraph { Size = 2 }, seed);
        var startNode = Assert.Single(graph.Nodes.Where(node => node.Pois.Any(poi => poi.Type == EnPoiType.Start)));
        var exitNode = Assert.Single(graph.Nodes.Where(node => node.Pois.Any(poi => poi.Type == EnPoiType.Exit)));
        outcomes.Add(ReferenceEquals(startNode, exitNode));
      }

      // Assert: Both placements are observable - same node allowed, not enforced.
      Assert.Contains(true, outcomes);
      Assert.Contains(false, outcomes);
    }
  }
}
