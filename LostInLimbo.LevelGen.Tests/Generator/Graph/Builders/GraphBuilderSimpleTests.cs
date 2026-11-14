using LostInLimbo.LevelGen.Data.Config.Graph;
using LostInLimbo.LevelGen.Data.Config.Graph.Builder;
using LostInLimbo.LevelGen.Data.Config.Graph.Proc;
using LostInLimbo.LevelGen.Data.Result.Graph;
using LostInLimbo.LevelGen.Internal.Generator.Graph.Builders;

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
  /// <summary>Mirrors <c>CnfGraphBuilderSimple.Default.MaxExtraDegree</c>: cap of the random-relationships phase.</summary>
  private static readonly int MaxDegree = CnfGraphBuilderSimple.Default.MaxExtraDegree;

  /// <summary>Mirrors <c>CnfGraphBuilderSimple.Default.MaxExtraDegreeStar</c>: cap of the skeleton phase.</summary>
  private static readonly int MaxDegreeStar = CnfGraphBuilderSimple.Default.MaxExtraDegreeStar;

  /// <summary>
  /// Builds a builder config for skeleton-shape tests: <c>null</c> keeps the default
  /// skeleton cap, any other value overrides <c>MaxExtraDegreeStar</c>.
  /// </summary>
  /// <param name="starCap">Skeleton cap to enforce, or <c>null</c> for the default.</param>
  /// <returns>Builder config for the build, or <c>null</c> when the default fits.</returns>
  private static CnfGraphBuilder? CreateSimpleSkeletonConfig(int? starCap)
  {
    return starCap is null
      ? null
      : CnfGraphBuilder.Default with
      {
        Builder = EnGraphBuilder.Simple,
        CnfSimple = CnfGraphBuilderSimple.Default with { MaxExtraDegreeStar = starCap.Value },
      };
  }

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
      // Act: Build a graph of the given size, without the default secret node.
      var (result, graph) = GraphTestSupport.Build(EnGraphBuilder.Simple, new CnfGraph
      {
        Size = size,
        ProcOpts = GraphTestSupport.NoSecrets,
      });

      // Assert: The node count follows the size setting exactly.
      Assert.True(result.Success);
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
      // Act: Build a graph without any extra relationships, without the default secret
      // node.
      var (result, graph) = GraphTestSupport.Build(EnGraphBuilder.Simple, new CnfGraph
      {
        Size = size,
        ProcOpts = GraphTestSupport.NoSecrets,
      });

      // Assert: The skeleton is a tree - size minus one relationships - and the graph
      // stays connected.
      Assert.True(result.Success);
      Assert.Equal(Math.Max(0, size - 1), graph.Edges.Count);
      GraphTestAssert.AssertBeatable(graph);
    }

    [Theory]
    [InlineData(10, 3)]
    [InlineData(5, 4)]
    [InlineData(20, 8)]
    [InlineData(100, 50)]
    public void Build_WithConnectivityBelowCapacity_ConsumesAllConnectivity(int size, int connectivity)
    {
      // Act: Build a graph whose requested extras fit under the degree cap, without the
      // default secret node.
      var (result, graph) = GraphTestSupport.Build(EnGraphBuilder.Simple, new CnfGraph
      {
        Size = size,
        Connectivity = connectivity,
        ProcOpts = GraphTestSupport.NoSecrets,
      });

      // Assert: Every requested extra relationship exists on top of the tree and the
      // graph stays connected.
      Assert.True(result.Success);
      Assert.Equal(size - 1 + connectivity, graph.Edges.Count);
      GraphTestAssert.AssertBeatable(graph);
    }

    [Fact]
    public void Build_WithVariousSeeds_ObservesDifferentSkeletonShapes()
    {
      // Act: Build many minimal graphs of the same size and look at the highest degree.
      var maxDegrees = new List<int>();
      for (var seed = 0; seed < 30; seed++)
      {
        var (_, graph) = GraphTestSupport.Build(
          EnGraphBuilder.Simple,
          new CnfGraph { Size = 10, ProcOpts = GraphTestSupport.NoSecrets },
          seed);
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
      // Act: Build with configs ranging from trivial to far beyond capacity, without the
      // default secret node.
      var (result, graph) = GraphTestSupport.Build(EnGraphBuilder.Simple, new CnfGraph
      {
        Size = size,
        Connectivity = connectivity,
        ProcOpts = GraphTestSupport.NoSecrets,
      });

      // Assert: The builder never fails - unsatisfiable settings are stretched silently.
      Assert.True(result.Success);
      Assert.Equal(size, graph.Nodes.Count);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(10)]
    public void Build_WithSizeOne_WithAnyConnectivity_ProducesSingleNodeWithBothPois(int connectivity)
    {
      // Act: Build the degenerate one-node graph with and without extra connectivity.
      var (result, graph) = GraphTestSupport.Build(EnGraphBuilder.Simple, new CnfGraph
      {
        Size = 1,
        Connectivity = connectivity,
      });

      // Assert: No relationships are possible, yet both POIs and the mandatory goal live
      // on the single ordinary node; the processor pass appends one secret node
      // hanging off it, which hosts the optional goal (allowed in secret nodes).
      Assert.True(result.Success);
      var ordinary = Assert.Single(graph.Nodes.Where(node => !node.Secret));
      var secret = Assert.Single(graph.Nodes.Where(node => node.Secret));
      Assert.Single(ordinary.Pois.Where(poi => poi.Type == EnPoiType.Start));
      Assert.Single(ordinary.Pois.Where(poi => poi.Type == EnPoiType.Exit));
      Assert.Single(ordinary.Pois.Where(poi => poi.Type == EnPoiType.Goal));
      Assert.Single(secret.Pois);
      Assert.Single(secret.Pois.Where(poi => poi.Type == EnPoiType.OptionalGoal));

      // Assert: The only relationship is the secret passage into the secret node.
      var edge = Assert.Single(graph.Edges);
      Assert.True(edge.Secret);
      Assert.Equal(ordinary.Id, edge.From);
      Assert.Equal(secret.Id, edge.To);
    }

    [Fact]
    public void Build_WithExcessiveConnectivityAtSmallSize_ObeysDegreeCap()
    {
      // Act: Ask for far more relationships than a five-node graph can hold, without the
      // default secret node.
      var (result, graph) = GraphTestSupport.Build(EnGraphBuilder.Simple, new CnfGraph
      {
        Size = 5,
        Connectivity = 10000,
        ProcOpts = GraphTestSupport.NoSecrets,
      });

      // Assert: The build succeeds, no node exceeds the plain degree cap on builder
      // passages (at size five even a skeleton central collects at most four nodes) and
      // the complete graph bound is respected.
      Assert.True(result.Success);
      GraphTestAssert.AssertDegreeCap(graph, MaxDegree);
      GraphTestAssert.AssertMaxEdges(graph);
      Assert.All(graph.Nodes, node => Assert.Equal(MaxDegreeStar, node.MaxEdges));
      Assert.True(graph.Edges.Count <= 10, "Five nodes cannot hold more than ten pair passages.");
      GraphTestAssert.AssertValidEdges(graph);
      GraphTestAssert.AssertOpenPassages(graph);
      GraphTestAssert.AssertBeatable(graph);
    }

    /// <summary>
    /// Builds a dense twenty-node Simple graph without secrets, using the given degree caps.
    /// </summary>
    /// <param name="maxExtraDegree">Total-degree threshold of the extras phase.</param>
    /// <param name="maxExtraDegreeStar">Skeleton-phase degree cap.</param>
    /// <returns>The built graph.</returns>
    private static LilGraph BuildDenseWithCaps(int maxExtraDegree, int maxExtraDegreeStar)
    {
      var builderConfig = CnfGraphBuilder.Default with
      {
        CnfSimple = CnfGraphBuilderSimple.Default with
        {
          MaxExtraDegree = maxExtraDegree,
          MaxExtraDegreeStar = maxExtraDegreeStar,
        },
      };
      var (result, graph) = GraphTestSupport.Build(
        EnGraphBuilder.Simple,
        new CnfGraph { Size = 20, Connectivity = 1000, ProcOpts = GraphTestSupport.NoSecrets },
        builderConfig: builderConfig);
      Assert.True(result.Success);
      return graph;
    }

    [Fact]
    public void Build_WithMaxExtraDegreeAboveStarCap_NoNodeExceedsItsMaxEdges()
    {
      // Act: Extras threshold (5) above the skeleton cap (4), with connectivity that fills nodes.
      var graph = BuildDenseWithCaps(maxExtraDegree: 5, maxExtraDegreeStar: 4);

      // Assert: Every node's hard cap covers its actual relationship count.
      GraphTestAssert.AssertMaxEdges(graph);
    }

    [Fact]
    public void Build_WithMaxExtraDegreeAboveStarCap_ExtrasStillReachMaxExtraDegree()
    {
      // Act: Same caps as above.
      var graph = BuildDenseWithCaps(maxExtraDegree: 5, maxExtraDegreeStar: 4);

      // Assert: The extras threshold is honoured, not silently clamped to the skeleton cap.
      var degrees = GraphTestSupport.GetDegrees(graph);
      Assert.Contains(degrees.Values, degree => degree == 5);
      Assert.All(graph.Nodes, node => Assert.Equal(5, node.MaxEdges));
    }

    [Fact]
    public void Build_WithStarCapAboveMaxExtraDegree_MaxEdgesIsStarCap()
    {
      // Act: Common case: skeleton cap (7) above the extras threshold (4).
      var graph = BuildDenseWithCaps(maxExtraDegree: 4, maxExtraDegreeStar: 7);

      // Assert: The node cap is the skeleton cap, and it holds.
      Assert.All(graph.Nodes, node => Assert.Equal(7, node.MaxEdges));
      GraphTestAssert.AssertMaxEdges(graph);
    }

    [Fact]
    public void Build_WithExcessiveConnectivityAtLargeSize_SucceedsWithValidStructure()
    {
      // Act: Ask for far more relationships than a twenty-node graph can hold, without
      // the default secret node.
      var (result, graph) = GraphTestSupport.Build(EnGraphBuilder.Simple, new CnfGraph
      {
        Size = 20,
        Connectivity = 10000,
        ProcOpts = GraphTestSupport.NoSecrets,
      });

      // Assert: The build succeeds, keeps at least the skeleton, stays structurally
      // valid and never exceeds the skeleton degree cap on builder passages - central
      // handover keeps even star-shaped skeletons bounded.
      Assert.True(result.Success);
      Assert.True(graph.Edges.Count >= 19, "The skeleton alone already holds 19 relationships.");
      Assert.True(graph.Edges.Count <= 190, "Twenty nodes cannot hold more than 190 pair passages.");
      GraphTestAssert.AssertDegreeCap(graph, MaxDegreeStar);
      GraphTestAssert.AssertMaxEdges(graph);
      GraphTestAssert.AssertValidEdges(graph);
      GraphTestAssert.AssertOpenPassages(graph);
      GraphTestAssert.AssertBeatable(graph);
    }

    [Theory]
    [InlineData(6, null)]
    [InlineData(8, null)]
    [InlineData(10, null)]
    [InlineData(50, null)]
    [InlineData(100, null)]
    [InlineData(20, 2)] // tightest valid cap - forces central handover
    public void Build_WithAnySkeletonCap_ProducesBoundedConnectedSkeleton(int size, int? starCap)
    {
      // Arrange: A null cap keeps the default skeleton cap; two is the tightest valid
      // value, which forces central handover often.
      var builderConfig = CreateSimpleSkeletonConfig(starCap);
      var expectedCap = starCap ?? MaxDegreeStar;

      // Act: Build minimal skeletons across many seeds; every scenario must respect the
      // skeleton degree cap and stay a connected tree.
      for (var seed = 0; seed < 30; seed++)
      {
        var (result, graph) = GraphTestSupport.Build(
          EnGraphBuilder.Simple,
          new CnfGraph { Size = size, ProcOpts = GraphTestSupport.NoSecrets },
          seed,
          builderConfig);

        // Assert: Success, exactly size minus one skeleton relationships, connectivity
        // and the degree cap hold no matter which scenario or cap was rolled.
        Assert.True(result.Success, $"Build failed for seed {seed}.");
        Assert.Equal(size - 1, graph.Edges.Count);
        GraphTestAssert.AssertDegreeCap(graph, expectedCap);
        GraphTestAssert.AssertBeatable(graph, $"Skeleton graph must be connected (size {size}, seed {seed}).");
      }
    }

    [Theory]
    [InlineData(null)]
    [InlineData(2)] // tightest valid cap - handover is exercised almost every seed
    public void Build_WithLargeSizeAcrossSeeds_SkeletonCentralHandsOverAtCap(int? starCap)
    {
      // Arrange: A null cap keeps the default skeleton cap; two is the tightest valid
      // value, which forces central handover often.
      var builderConfig = CreateSimpleSkeletonConfig(starCap);
      var cap = starCap ?? MaxDegreeStar;

      // Act: Build 100-node skeletons across many seeds; star and mixed rolls fill a
      // central node to the cap and must hand over instead of growing beyond it.
      var maxDegrees = new List<int>();
      for (var seed = 0; seed < 30; seed++)
      {
        var (_, graph) = GraphTestSupport.Build(
          EnGraphBuilder.Simple,
          new CnfGraph { Size = 100, ProcOpts = GraphTestSupport.NoSecrets },
          seed,
          builderConfig);
        maxDegrees.Add(GraphTestSupport.GetDegrees(graph).Values.Max());
      }

      // Assert: At least one seed reached the cap exactly - proving the handover path
      // was exercised - and no seed ever went beyond it.
      Assert.Contains(maxDegrees, maxDegree => maxDegree == cap);
      Assert.All(maxDegrees, maxDegree => Assert.True(maxDegree <= cap, $"Max degree {maxDegree} exceeds cap {cap}."));
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
      var (result, graph) = GraphTestSupport.Build(EnGraphBuilder.Simple, new CnfGraph
      {
        Size = size,
        Connectivity = connectivity,
      });

      // Assert: Edges are well-formed, all passages open and every node uses one of the
      // default relative-size steps.
      Assert.True(result.Success);
      GraphTestAssert.AssertValidEdges(graph);
      GraphTestAssert.AssertOpenPassages(graph);
      var stepValues = CnfProcNodeRelSizeOpt.Default.Prepare();
      Assert.All(graph.Nodes, node => Assert.Contains(node.RelSize, stepValues));
    }
  }

  /// <summary>Tests for the secret-node pass run first in the processor pass.</summary>
  public sealed class SecretTests
  {
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(5)]
    [InlineData(50)]
    public void Build_AlwaysAddsExactlyOneSecretHangingOffOrdinaryNode(int size)
    {
      // Act: Build a graph of the given size.
      var (result, graph) = GraphTestSupport.Build(EnGraphBuilder.Simple, new CnfGraph { Size = size });

      // Assert: One secret node exists, reached only over its own secret chokepoint
      // whose source is an ordinary node.
      Assert.True(result.Success);
      var secret = Assert.Single(graph.Nodes.Where(node => node.Secret));
      var edge = Assert.Single(graph.Edges.Where(edge => edge.Secret));
      Assert.True(edge.Chokepoint);
      Assert.Equal(EnPassageDirection.Bidirectional, edge.Direction);
      Assert.Empty(edge.Conditions);
      Assert.Equal(secret.Id, edge.To);
      Assert.Contains(edge.From, graph.Nodes.Where(node => !node.Secret).Select(node => node.Id));
    }

    [Fact]
    public void Build_WithSecrets_StillProducesBeatableGraph()
    {
      // Act: Build graphs with several seeds.
      for (var seed = 0; seed < 10; seed++)
      {
        var (_, graph) = GraphTestSupport.Build(EnGraphBuilder.Simple, new CnfGraph { Size = 8, Connectivity = 3 }, seed);

        // Assert: The hidden node stays reachable - the secret passage is open - so the
        // beatability simulation still passes.
        GraphTestAssert.AssertBeatable(graph, $"Secret graph must stay connected (seed {seed}).");
      }
    }
  }

  /// <summary>Tests for the debug <see cref="LilNode.BuildOrder"/> numbering.</summary>
  public sealed class BuildOrderTests
  {
    /// <summary>Number of seeds checked; enough to hit every skeleton scenario.</summary>
    private const int SeedCount = 30;

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(8)]
    [InlineData(50)]
    public void Build_BuildOrder_MatchesNodeIndex(int size)
    {
      for (var seed = 0; seed < SeedCount; seed++)
      {
        // Act: Build a graph with extras and the default secret node.
        var (result, graph) = GraphTestSupport.Build(
          EnGraphBuilder.Simple, new CnfGraph { Size = size, Connectivity = 3 }, seed);

        // Assert: Every node - builder nodes first, the appended secret last - is numbered
        // by its position in the node list.
        Assert.True(result.Success);
        for (var index = 0; index < graph.Nodes.Count; index++)
          Assert.Equal(index, graph.Nodes[index].BuildOrder);
      }
    }

    [Theory]
    [InlineData(3)]
    [InlineData(8)]
    [InlineData(50)]
    public void Build_BuildOrder_EveryNonRootNodeHasEarlierNeighbor(int size)
    {
      for (var seed = 0; seed < SeedCount; seed++)
      {
        // Act: Build a minimal skeleton only, so every edge is a skeleton edge.
        var (_, graph) = GraphTestSupport.Build(
          EnGraphBuilder.Simple, new CnfGraph { Size = size, ProcOpts = GraphTestSupport.NoSecrets }, seed);

        // Assert: Each node but the root is linked to a node built before it - so the build
        // order really is the order nodes joined the skeleton.
        var orderById = graph.Nodes.ToDictionary(node => node.Id, node => node.BuildOrder);
        foreach (var node in graph.Nodes.Where(node => node.BuildOrder > 0))
        {
          var hasEarlierNeighbor = graph.Edges.Any(edge =>
            (edge.From == node.Id && orderById[edge.To] < node.BuildOrder) ||
            (edge.To == node.Id && orderById[edge.From] < node.BuildOrder));
          Assert.True(hasEarlierNeighbor, $"Node {node.BuildOrder} has no earlier neighbor (seed {seed}).");
        }
      }
    }
  }

  /// <summary>Tests for reproducibility of the generated graph.</summary>
  public sealed class DeterminismTests
  {
    [Fact]
    public void Build_WithSameSeed_ProducesIdenticalGraphs()
    {
      // Arrange: Same graph config and same seed for two builds - once with the default
      // builder config, once with every builder knob set away from its default.
      var config = new CnfGraph { Size = 12, Connectivity = 6 };
      CnfGraphBuilder?[] builderConfigs =
      {
        null,
        CnfGraphBuilder.Default with
        {
          Builder = EnGraphBuilder.Simple,
          CnfSimple = CnfGraphBuilderSimple.Default with
          {
            ChokepointChance = 0.25f,
            MaxExtraDegree = 2,
            MaxExtraDegreeStar = 3,
          },
        },
      };

      foreach (var builderConfig in builderConfigs)
      {
        // Act: Build both graphs.
        var first = GraphTestSupport.Build(EnGraphBuilder.Simple, config, builderConfig: builderConfig);
        var second = GraphTestSupport.Build(EnGraphBuilder.Simple, config, builderConfig: builderConfig);

        // Assert: Nodes, edges and POIs match one by one.
        Assert.Equal(first.Result.Success, second.Result.Success);
        GraphTestAssert.AssertEqualGraphs(first.Graph, second.Graph);
      }
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

  /// <summary>Tests for obstacles: <c>Complexity</c> spent by the obstacle placer at the end of shaping.</summary>
  public sealed class ObstacleTests
  {
    /// <summary>Mirrors <c>CnfProcObstaclePlacerOpt.Default.MaxConditionsPerEdge</c>.</summary>
    private static readonly int MaxConditionsPerEdge = CnfProcObstaclePlacerOpt.Default.MaxConditionsPerEdge;

    /// <summary>Proc options with frequent secrets of every kind, including two-passage routes.</summary>
    private static readonly CnfProcOpt ManySecrets = CnfProcOpt.Default with
    {
      RelSecrets = CnfProcRelSecretsOpt.Default with { Amount = 3, ChanceTwoPassages = 0.5f },
    };

    /// <summary>Counts the solution POIs of a graph; one per obstacle.</summary>
    /// <param name="graph">Graph to count in.</param>
    /// <returns>Number of solution POIs.</returns>
    private static int CountSolutions(LilGraph graph) => GraphTestSupport.GetPois(graph, EnPoiType.Solution).Count;

    [Fact]
    public void Build_WithZeroComplexity_LeavesEveryPassageOpen()
    {
      // Act: Build a well connected graph without complexity.
      var (_, graph) = GraphTestSupport.Build(EnGraphBuilder.Simple, new CnfGraph { Size = 12, Connectivity = 6 });

      // Assert: No conditions and no solutions anywhere.
      GraphTestAssert.AssertOpenPassages(graph);
      Assert.Equal(0, CountSolutions(graph));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(50)]
    public void Build_WithComplexity_KeepsShapeAndAddsObstacles(int complexity)
    {
      // Arrange: Reference build without obstacles.
      var reference = GraphTestSupport.Build(EnGraphBuilder.Simple, new CnfGraph { Size = 12, Connectivity = 4 }).Graph;

      // Act: Same config and seed with the complexity knob turned up.
      var (_, graph) = GraphTestSupport.Build(EnGraphBuilder.Simple, new CnfGraph
      {
        Size = 12,
        Connectivity = 4,
        Complexity = complexity,
      });

      // Assert: Obstacles are added on top - nodes and passages themselves are the same.
      Assert.Equal(reference.Nodes.Select(node => node.Id), graph.Nodes.Select(node => node.Id));
      Assert.Equal(
        reference.Edges.Select(edge => (edge.Id, edge.From, edge.To, edge.Secret)),
        graph.Edges.Select(edge => (edge.Id, edge.From, edge.To, edge.Secret)));

      // Assert: At least one obstacle, never more than Complexity, and the level is beatable.
      Assert.InRange(CountSolutions(graph), 1, complexity);
      Assert.Contains(graph.Edges, edge => edge.Conditions.Count > 0);
      GraphTestAssert.AssertBeatable(graph);
    }

    [Fact]
    public void Build_WithComplexityAndSameSeed_ProducesIdenticalGraphs()
    {
      // Arrange: Config with obstacles and secrets.
      var config = new CnfGraph { Size = 20, Connectivity = 8, Complexity = 6, ProcOpts = ManySecrets };

      // Act: Build twice with the same seed.
      var first = GraphTestSupport.Build(EnGraphBuilder.Simple, config, seedGraph: 7);
      var second = GraphTestSupport.Build(EnGraphBuilder.Simple, config, seedGraph: 7);

      // Assert: Same graph, obstacles included.
      Assert.True(CountSolutions(first.Graph) > 0);
      GraphTestAssert.AssertEqualGraphs(first.Graph, second.Graph);
    }

    [Fact]
    public void Build_WithSingleNode_PlacesNoObstacles()
    {
      // Act: One node cannot hold an obstacle, whatever the complexity.
      var (_, graph) = GraphTestSupport.Build(EnGraphBuilder.Simple, new CnfGraph
      {
        Size = 1,
        Complexity = 5,
        ProcOpts = GraphTestSupport.NoSecrets,
      });

      // Assert: Nothing was placed.
      Assert.Equal(0, CountSolutions(graph));
      GraphTestAssert.AssertOpenPassages(graph);
    }

    [Fact]
    public void Build_OverManyConfigs_IsAlwaysBeatableWithinLimits()
    {
      // Arrange: Sweep of sizes, connectivity and complexity, secrets of every kind enabled.
      int[] sizes = [2, 3, 5, 8, 15, 30, 60];
      int[] connectivities = [0, 3, 10, 40];
      int[] complexities = [1, 2, 4, 8, 20];
      const int seedCount = 8;
      var totalObstacles = 0;

      foreach (var size in sizes)
      foreach (var connectivity in connectivities)
      foreach (var complexity in complexities)
      {
        for (var seed = 0; seed < seedCount; seed++)
        {
          var label = $"size {size}, connectivity {connectivity}, complexity {complexity}, seed {seed}";

          // Act: Build the graph.
          var (_, graph) = GraphTestSupport.Build(EnGraphBuilder.Simple, new CnfGraph
          {
            Size = size,
            Connectivity = connectivity,
            Complexity = complexity,
            ProcOpts = ManySecrets,
          }, seedGraph: seed);

          // Assert: Beatable, with obstacle and per-passage limits kept.
          GraphTestAssert.AssertBeatable(graph, label);
          var solutions = GraphTestSupport.GetPois(graph, EnPoiType.Solution);
          Assert.True(solutions.Count <= complexity, $"{label}: {solutions.Count} obstacles.");
          var solutionIds = solutions.Select(poi => poi.Id).ToHashSet();
          Assert.Equal(solutions.Count, solutionIds.Count);
          totalObstacles += solutions.Count;

          foreach (var edge in graph.Edges)
          {
            Assert.True(edge.Conditions.Count <= MaxConditionsPerEdge, $"{label}: passage {edge.Id} is overloaded.");
            // A one-way passage is locked at most once (entry of a secret loop); every
            // condition names its own existing solution.
            if (edge.Direction != EnPassageDirection.Bidirectional) Assert.True(edge.Conditions.Count <= 1, label);
            var required = edge.Conditions.Select(condition => condition.RequiresPoi).ToList();
            Assert.All(required, id => Assert.True(solutionIds.Contains(id), label));
            Assert.Equal(required.Count, required.Distinct().Count());
          }

          // Assert: Every solution locks something, and none hides in a secret node.
          var usedIds = graph.Edges.SelectMany(edge => edge.Conditions).Select(condition => condition.RequiresPoi).ToHashSet();
          Assert.All(solutionIds, id => Assert.Contains(id, usedIds));
          Assert.DoesNotContain(graph.Nodes, node => node.Secret && node.Pois.Any(poi => poi.Type == EnPoiType.Solution));
        }
      }

      // Assert: The sweep really exercised the placer.
      Assert.True(totalObstacles > 0);
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

      // Assert: Exactly one POI of each type exists across the graph - Start, Exit and
      // the two objectives placed by the processor pass.
      var pois = graph.Nodes.SelectMany(node => node.Pois).ToList();
      Assert.Equal(4, pois.Count);
      Assert.Single(pois.Where(poi => poi.Type == EnPoiType.Start));
      Assert.Single(pois.Where(poi => poi.Type == EnPoiType.Exit));
      Assert.Single(pois.Where(poi => poi.Type == EnPoiType.Goal));
      Assert.Single(pois.Where(poi => poi.Type == EnPoiType.OptionalGoal));
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

  /// <summary>Tests that non-default builder configs flow into generation.</summary>
  public sealed class CustomConfigTests
  {
    [Theory]
    [InlineData(0f)]
    [InlineData(1f)]
    public void Build_WithExtremeChokepointChance_AppliesItToEveryEdge(float chokepointChance)
    {
      // Arrange: A builder config forcing every edge either open or choked.
      var builderConfig = CnfGraphBuilder.Default with
      {
        Builder = EnGraphBuilder.Simple,
        CnfSimple = CnfGraphBuilderSimple.Default with { ChokepointChance = chokepointChance },
      };

      for (var seed = 0; seed < 5; seed++)
      {
        // Act: Build with default secret node disabled, so every edge is builder-made.
        var (result, graph) = GraphTestSupport.Build(EnGraphBuilder.Simple, new CnfGraph
        {
          Size = 10,
          Connectivity = 20,
          ProcOpts = GraphTestSupport.NoSecrets,
        }, seed, builderConfig);

        // Assert: The edge list is non-empty and every edge carries the forced outcome.
        Assert.True(result.Success, $"Build failed for seed {seed}.");
        Assert.NotEmpty(graph.Edges);
        Assert.All(
          graph.Edges,
          edge => Assert.Equal(chokepointChance == 1f, edge.Chokepoint));
      }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void Build_WithMaxExtraDegreeBelowTwo_AddsNoExtraRelationships(int maxExtraDegree)
    {
      // Arrange: Every connected node has degree at least one, so a total-degree
      // eligibility threshold below two excludes every candidate pair.
      const int size = 10;
      var builderConfig = CnfGraphBuilder.Default with
      {
        Builder = EnGraphBuilder.Simple,
        CnfSimple = CnfGraphBuilderSimple.Default with { MaxExtraDegree = maxExtraDegree },
      };

      // Act: Request far more extras than the cap can ever allow.
      var (result, graph) = GraphTestSupport.Build(EnGraphBuilder.Simple, new CnfGraph
      {
        Size = size,
        Connectivity = 100,
        ProcOpts = GraphTestSupport.NoSecrets,
      }, builderConfig: builderConfig);

      // Assert: Only the skeleton remains - a tree over all nodes - and it stays connected.
      Assert.True(result.Success);
      Assert.Equal(size - 1, graph.Edges.Count);
      GraphTestAssert.AssertBeatable(graph);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public void Build_WithCustomMaxExtraDegree_NeverPushesNodesAboveCap(int maxExtraDegree)
    {
      // Arrange: Skeleton-only baseline for the same seed; the build is identical up to
      // the extras pass, so its degrees are the starting point under the custom cap.
      var size = 10;
      var builderConfig = CnfGraphBuilder.Default with
      {
        Builder = EnGraphBuilder.Simple,
        CnfSimple = CnfGraphBuilderSimple.Default with { MaxExtraDegree = maxExtraDegree },
      };

      for (var seed = 0; seed < 10; seed++)
      {
        var (_, skeleton) = GraphTestSupport.Build(EnGraphBuilder.Simple, new CnfGraph
        {
          Size = size,
          ProcOpts = GraphTestSupport.NoSecrets,
        }, seed, builderConfig);
        var skeletonDegrees = GraphTestSupport.GetDegrees(skeleton);

        // Act: Same seed with connectivity that saturates the extras pass.
        var (result, graph) = GraphTestSupport.Build(EnGraphBuilder.Simple, new CnfGraph
        {
          Size = size,
          Connectivity = 100,
          ProcOpts = GraphTestSupport.NoSecrets,
        }, seed, builderConfig);
        var degrees = GraphTestSupport.GetDegrees(graph);

        // Assert: No node ends above max(skeleton degree, cap): nodes already at or
        // above the cap never gain an extra, nodes below it stop at the cap.
        Assert.True(result.Success, $"Build failed for seed {seed}.");
        foreach (var node in graph.Nodes)
        {
          var allowed = Math.Max(skeletonDegrees[node.Id], maxExtraDegree);
          Assert.True(
            degrees[node.Id] <= allowed,
            $"Node {node.Id} has degree {degrees[node.Id]}, skeleton {skeletonDegrees[node.Id]}, cap {maxExtraDegree} (seed {seed}).");
        }

        GraphTestAssert.AssertBeatable(graph, $"Graph must stay connected (seed {seed}).");
      }
    }

    [Fact]
    public void Build_WithCustomMaxExtraDegreeStar_SetsNodeMaxEdges()
    {
      // Arrange: A custom skeleton cap that is not the default value.
      const int skeletonCap = 4;
      var builderConfig = CnfGraphBuilder.Default with
      {
        Builder = EnGraphBuilder.Simple,
        CnfSimple = CnfGraphBuilderSimple.Default with { MaxExtraDegreeStar = skeletonCap },
      };

      // Act: Build a plain graph with the custom builder config.
      var (result, graph) = GraphTestSupport.Build(EnGraphBuilder.Simple, new CnfGraph
      {
        Size = 10,
        ProcOpts = GraphTestSupport.NoSecrets,
      }, builderConfig: builderConfig);

      // Assert: Every node carries the configured hard cap.
      Assert.True(result.Success);
      Assert.All(graph.Nodes, node => Assert.Equal(skeletonCap, node.MaxEdges));
    }
  }

  /// <summary>Tests for config validation.</summary>
  public sealed class ConfigValidationTests
  {
    [Fact]
    public void Validate_Default_DoesNotThrow()
    {
      // Arrange: The default instance should be valid.
      var config = CnfGraphBuilderSimple.Default;

      // Assert: No exception is thrown.
      config.Validate();
    }

    [Theory]
    [InlineData(-0.1f)]
    [InlineData(1.1f)]
    [InlineData(float.NaN)]
    public void Validate_InvalidChokepointChance_ThrowsArgumentOutOfRange(float chance)
    {
      // Arrange: A chokepoint chance outside [0, 1] - including NaN, which comparisons
      // never catch - is invalid.
      var config = CnfGraphBuilderSimple.Default with { ChokepointChance = chance };

      // Assert: Validation rejects the value.
      var ex = Assert.Throws<ArgumentOutOfRangeException>(() => config.Validate());
      Assert.Equal(nameof(CnfGraphBuilderSimple.ChokepointChance), ex.ParamName);
    }

    [Theory]
    [InlineData(-1)]
    public void Validate_NegativeMaxExtraDegree_ThrowsArgumentOutOfRange(int maxExtraDegree)
    {
      // Arrange: A negative extra-degree cap is invalid.
      var config = CnfGraphBuilderSimple.Default with { MaxExtraDegree = maxExtraDegree };

      // Assert: Validation rejects the value.
      var ex = Assert.Throws<ArgumentOutOfRangeException>(() => config.Validate());
      Assert.Equal(nameof(CnfGraphBuilderSimple.MaxExtraDegree), ex.ParamName);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_MaxExtraDegreeStarBelowTwo_ThrowsArgumentOutOfRange(int maxExtraDegreeStar)
    {
      // Arrange: The skeleton cap must be at least 2 - the builder's central-handover
      // eligibility window (degree >= 1 && degree < cap) is empty at a cap of 1.
      var config = CnfGraphBuilderSimple.Default with { MaxExtraDegreeStar = maxExtraDegreeStar };

      // Assert: Validation rejects the value.
      var ex = Assert.Throws<ArgumentOutOfRangeException>(() => config.Validate());
      Assert.Equal(nameof(CnfGraphBuilderSimple.MaxExtraDegreeStar), ex.ParamName);
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(1f)]
    public void Validate_BoundaryChokepointChance_DoesNotThrow(float chance)
    {
      // Arrange: The inclusive endpoints of the allowed probability range.
      var config = CnfGraphBuilderSimple.Default with { ChokepointChance = chance };

      // Assert: The boundary value is inside the allowed range.
      config.Validate();
    }

    [Fact]
    public void Validate_MinimalDegreeCaps_DoesNotThrow()
    {
      // Arrange: The smallest valid values for both degree caps.
      var config = CnfGraphBuilderSimple.Default with
      {
        MaxExtraDegree = 0,
        MaxExtraDegreeStar = 2,
      };

      // Assert: The boundary values are inside the allowed ranges.
      config.Validate();
    }
  }
}
