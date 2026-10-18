using LostInLimbo.LevelGen.Data.Result.Graph;
using LostInLimbo.LevelGen.Internal.Generator.Graph.Processors;
using static LostInLimbo.LevelGen.Tests.Generator.Graph.GraphTestSupport;

namespace LostInLimbo.LevelGen.Tests.Generator.Graph.Distributor;

/// <summary>
/// Tests <see cref="ProcNodeRelSize"/>: option validation, step assignment rules,
/// relationship/POI bias behavior and determinism of the assignment.
/// </summary>
/// <remarks>
/// The distributor works on a finished <see cref="LilGraph"/>, so most tests construct
/// graphs directly instead of going through a builder - that keeps degrees and POI counts
/// exactly controlled per test.
/// </remarks>
public sealed class ProcNodeRelSizeTests
{
  /// <summary>Graph sub-seed used by all tests unless a test varies the seed itself.</summary>
  private const long Seed = GraphTestSupport.DefaultSeedGraph;

  /// <summary>Tests for the hard constraints: out-of-range values and contradictions.</summary>
  public sealed class ValidationTests
  {
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Distribute_WithStepsBelowOne_ThrowsArgumentOutOfRangeException(int steps)
    {
      // Arrange: Options demanding a forbidden step count.
      var options = ProcNodeRelSizeOpt.Default with { Steps = steps };

      // Act & Assert: The distributor rejects the options before touching the graph.
      var exception = Assert.Throws<ArgumentOutOfRangeException>(
        () => new ProcNodeRelSize(CreateChainGraph(4), options, Seed).Execute());
      Assert.Equal("Steps", exception.ParamName);
    }

    [Theory]
    [InlineData(-0.1f)]
    [InlineData(1.1f)]
    [InlineData(float.NaN)]
    public void Distribute_WithSkewOutOfRange_ThrowsArgumentOutOfRangeException(float skew)
    {
      // Arrange: Options with a skew outside [0, 1] (NaN included).
      var options = ProcNodeRelSizeOpt.Default with { Skew = skew };

      // Act & Assert: Skew is rejected as out of range.
      var exception = Assert.Throws<ArgumentOutOfRangeException>(
        () => new ProcNodeRelSize(CreateChainGraph(4), options, Seed).Execute());
      Assert.Equal("Skew", exception.ParamName);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void Distribute_WithMinSizeBelowOne_ThrowsArgumentOutOfRangeException(int minSize)
    {
      // Arrange: Options asking for an impossible smallest size.
      var options = ProcNodeRelSizeOpt.Default with { MinSize = minSize };

      // Act & Assert: MinSize is rejected as out of range.
      var exception = Assert.Throws<ArgumentOutOfRangeException>(
        () => new ProcNodeRelSize(CreateChainGraph(4), options, Seed).Execute());
      Assert.Equal("MinSize", exception.ParamName);
    }

    [Theory]
    [InlineData(-0.1f)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    public void Distribute_WithInvalidRelationshipBias_ThrowsArgumentOutOfRangeException(float bias)
    {
      // Arrange: Options with a negative or non-finite relationship bias.
      var options = ProcNodeRelSizeOpt.Default with { BiasRelationships = bias };

      // Act & Assert: The relationship bias is rejected as out of range.
      var exception = Assert.Throws<ArgumentOutOfRangeException>(
        () => new ProcNodeRelSize(CreateChainGraph(4), options, Seed).Execute());
      Assert.Equal("BiasRelationships", exception.ParamName);
    }

    [Theory]
    [InlineData(-0.1f)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    public void Distribute_WithInvalidPoiBias_ThrowsArgumentOutOfRangeException(float bias)
    {
      // Arrange: Options with a negative or non-finite POI bias.
      var options = ProcNodeRelSizeOpt.Default with { BiasPois = bias };

      // Act & Assert: The POI bias is rejected as out of range.
      var exception = Assert.Throws<ArgumentOutOfRangeException>(
        () => new ProcNodeRelSize(CreateChainGraph(4), options, Seed).Execute());
      Assert.Equal("BiasPois", exception.ParamName);
    }

    [Fact]
    public void Distribute_WithSingleStepAndRelationshipBias_ThrowsArgumentException()
    {
      // Arrange: One step value forbids variation while the bias demands it.
      var options = ProcNodeRelSizeOpt.Default with
      {
        Steps = 1,
        BiasRelationships = 0.5f,
      };

      // Act & Assert: The contradiction is reported as an argument error.
      var exception = Assert.Throws<ArgumentException>(
        () => new ProcNodeRelSize(CreateChainGraph(4), options, Seed).Execute());
      Assert.Equal("Steps", exception.ParamName);
    }

    [Fact]
    public void Distribute_WithSingleStepAndPoiBias_ThrowsArgumentException()
    {
      // Arrange: One step value forbids variation while the POI bias demands it.
      var options = ProcNodeRelSizeOpt.Default with
      {
        Steps = 1,
        BiasRelationships = 0f,
        BiasPois = 0.5f,
      };

      // Act & Assert: The contradiction is reported as an argument error.
      var exception = Assert.Throws<ArgumentException>(
        () => new ProcNodeRelSize(CreateChainGraph(4), options, Seed).Execute());
      Assert.Equal("Steps", exception.ParamName);
    }

    [Fact]
    public void Distribute_WithUndefinedCurve_ThrowsArgumentOutOfRangeException()
    {
      // Arrange: Options with a curve value outside the enum.
      var options = ProcNodeRelSizeOpt.Default with { Curve = (EnRelSizeCurve)42 };

      // Act & Assert: The unknown curve is rejected.
      var exception = Assert.Throws<ArgumentOutOfRangeException>(
        () => new ProcNodeRelSize(CreateChainGraph(4), options, Seed).Execute());
      Assert.Equal("Curve", exception.ParamName);
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(-1f)]
    public void Distribute_WithLinearCurveAndNonIncreasingGrowth_ThrowsArgumentException(float growth)
    {
      // Arrange: Linear options whose growth cannot separate the steps (0 keeps them
      // equal, negative reverses them).
      var options = ProcNodeRelSizeOpt.Default with
      {
        Curve = EnRelSizeCurve.Linear,
        Growth = growth,
      };

      // Act & Assert: The collapsed step values are rejected as a contradiction.
      var exception = Assert.Throws<ArgumentException>(
        () => new ProcNodeRelSize(CreateChainGraph(4), options, Seed).Execute());
      Assert.Equal("Growth", exception.ParamName);
    }

    [Theory]
    [InlineData(1f)]
    [InlineData(0.5f)]
    public void Distribute_WithGeometricCurveAndNonIncreasingGrowth_ThrowsArgumentException(float growth)
    {
      // Arrange: Geometric options whose growth cannot separate the steps (1 keeps them
      // equal, below 1 shrinks them).
      var options = ProcNodeRelSizeOpt.Default with { Growth = growth };

      // Act & Assert: The collapsed step values are rejected as a contradiction.
      var exception = Assert.Throws<ArgumentException>(
        () => new ProcNodeRelSize(CreateChainGraph(4), options, Seed).Execute());
      Assert.Equal("Growth", exception.ParamName);
    }

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(float.MaxValue)]
    public void Distribute_WithNonFiniteOrOverflowingGrowth_ThrowsArgumentOutOfRangeException(float growth)
    {
      // Arrange: Growth that either is not finite or pushes step values out of Int32 range.
      var options = ProcNodeRelSizeOpt.Default with { Growth = growth };

      // Act & Assert: Growth is rejected as out of range.
      var exception = Assert.Throws<ArgumentOutOfRangeException>(
        () => new ProcNodeRelSize(CreateChainGraph(4), options, Seed).Execute());
      Assert.Equal("Growth", exception.ParamName);
    }

    [Fact]
    public void Distribute_WithTooManySteps_ThrowsArgumentOutOfRangeException()
    {
      // Arrange: Sixty geometric doublings reach 2^59, far beyond Int32.
      var options = ProcNodeRelSizeOpt.Default with { Steps = 60 };

      // Act & Assert: The overflowing step values are rejected.
      var exception = Assert.Throws<ArgumentOutOfRangeException>(
        () => new ProcNodeRelSize(CreateChainGraph(4), options, Seed).Execute());
      Assert.Equal("Growth", exception.ParamName);
    }

    [Fact]
    public void Distribute_WithInvalidOptionsAndEmptyGraph_StillThrows()
    {
      // Arrange: Empty graph, so only validation can produce an error here.
      var graph = new LilGraph();
      var options = ProcNodeRelSizeOpt.Default with { Steps = 0 };

      // Act & Assert: Options are validated before the early exit for empty graphs.
      Assert.Throws<ArgumentOutOfRangeException>(
        () => new ProcNodeRelSize(graph, options, Seed).Execute());
    }

    [Fact]
    public void Constructor_WithNullGraph_ThrowsArgumentNullException()
    {
      // Act & Assert: The pass refuses to be built around a missing graph.
      var exception = Assert.Throws<ArgumentNullException>(
        () => new ProcNodeRelSize(null!, ProcNodeRelSizeOpt.Default, Seed));
      Assert.Equal("graph", exception.ParamName);
    }

    [Fact]
    public void Constructor_WithNullOptions_ThrowsArgumentNullException()
    {
      // Act & Assert: The pass refuses missing options.
      var exception = Assert.Throws<ArgumentNullException>(
        () => new ProcNodeRelSize(CreateChainGraph(4), null!, Seed));
      Assert.Equal("options", exception.ParamName);
    }

    [Fact]
    public void Constructor_WithInvalidOptions_ThrowsBeforeExecute()
    {
      // Arrange: Options rejected by validation.
      var options = ProcNodeRelSizeOpt.Default with { Steps = 0 };

      // Act & Assert: Construction alone rejects the options, before Execute can run.
      Assert.Throws<ArgumentOutOfRangeException>(
        () => new ProcNodeRelSize(CreateChainGraph(4), options, Seed));
    }
  }

  /// <summary>Tests for which values end up on the nodes.</summary>
  public sealed class DistributionTests
  {
    [Fact]
    public void BuildStepValues_WithDefaults_ReturnsGeometricSteps()
    {
      // Act: Build the step values of the default options.
      var values = ProcNodeRelSizeOpt.Default.Prepare();

      // Assert: Three doublings from 1: 1, 2, 4.
      Assert.Equal(new[] { 1, 2, 4 }, values);
    }

    [Fact]
    public void BuildStepValues_WithLinearCurve_AppearsAdditively()
    {
      // Arrange: Four linear steps from 2 with an increment of 3.
      var options = ProcNodeRelSizeOpt.Default with
      {
        Steps = 4,
        Curve = EnRelSizeCurve.Linear,
        MinSize = 2,
        Growth = 3f,
      };

      // Act: Build the step values.
      var values = options.Prepare();

      // Assert: Each step adds the increment once.
      Assert.Equal(new[] { 2, 5, 8, 11 }, values);
    }

    [Fact]
    public void Distribute_WithSingleStep_AssignsMinSizeToEveryNode()
    {
      // Arrange: One allowed step value - the only configuration without variation.
      var options = new ProcNodeRelSizeOpt { Steps = 1, MinSize = 5 };
      var graph = CreateChainGraph(10);

      // Act: Distribute with a bias-free single-step setup.
      new ProcNodeRelSize(graph, options, Seed).Execute();

      // Assert: Every node uses the single step value, whatever the seed rolled.
      Assert.All(graph.Nodes, node => Assert.Equal(5, node.RelSize));
    }

    [Fact]
    public void Distribute_WithFullSkewAndNoBias_AssignsOneSharedValue()
    {
      // Arrange: All probability mass sits on the dominant step and no bias tilts away.
      var options = ProcNodeRelSizeOpt.Default with
      {
        Skew = 1f,
        BiasRelationships = 0f,
        BiasPois = 0f,
      };

      // Act & Assert: Whatever step each seed rolled, all of its nodes landed on it.
      for (var seed = 0; seed < 10; seed++)
      {
        var graph = CreateStarGraph(15);
        new ProcNodeRelSize(graph, options, seed).Execute();
        Assert.Single(graph.Nodes.Select(node => node.RelSize).Distinct());
      }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(10)]
    [InlineData(100)]
    public void Distribute_WithDefaults_UsesOnlyStepValues(int nodeCount)
    {
      // Arrange: The default options define the set of allowed values.
      var expected = ProcNodeRelSizeOpt.Default.Prepare();

      // Act & Assert: Across many seeds only step values are ever assigned.
      for (var seed = 0; seed < 20; seed++)
      {
        var graph = CreateChainGraph(nodeCount);
        new ProcNodeRelSize(graph, ProcNodeRelSizeOpt.Default, seed).Execute();
        Assert.All(graph.Nodes, node => Assert.Contains(node.RelSize, expected));
      }
    }

    [Fact]
    public void Distribute_WithZeroSkew_OverSeeds_ObservesEveryStepValue()
    {
      // Arrange: Flat base distribution without any bias to starve the extremes.
      var options = ProcNodeRelSizeOpt.Default with
      {
        Skew = 0f,
        BiasRelationships = 0f,
        BiasPois = 0f,
      };
      var expected = options.Prepare();
      var seen = new HashSet<int>();

      // Act: Distribute many moderately sized graphs and collect every value used.
      for (var seed = 0; seed < 20; seed++)
      {
        var graph = CreateChainGraph(30);
        new ProcNodeRelSize(graph, options, seed).Execute();
        foreach (var node in graph.Nodes)
        {
          seen.Add(node.RelSize);
        }
      }

      // Assert: Every step value appeared at least once - none of them is starved out.
      Assert.All(expected, value => Assert.Contains(value, seen));
    }

    [Fact]
    public void Distribute_WithDefaults_AcrossSeeds_ProducesVaryingSizes()
    {
      // Act: Distribute the default options over several seeds.
      var varyingGraphs = 0;
      for (var seed = 0; seed < 10; seed++)
      {
        var graph = CreateStarGraph(20);
        new ProcNodeRelSize(graph, ProcNodeRelSizeOpt.Default, seed).Execute();
        if (graph.Nodes.Select(node => node.RelSize).Distinct().Count() > 1) varyingGraphs++;
      }

      // Assert: The defaults vary sizes in almost every graph - a single dominant value
      // everywhere would defeat the purpose of the pass.
      Assert.True(varyingGraphs >= 8, $"Only {varyingGraphs} of 10 graphs used more than one size.");
    }

    [Fact]
    public void Distribute_WithEmptyGraph_DoesNothing()
    {
      // Arrange: A graph without nodes or edges.
      var graph = new LilGraph();

      // Act: Distribute over the empty graph.
      new ProcNodeRelSize(graph, ProcNodeRelSizeOpt.Default, Seed).Execute();

      // Assert: Nothing appeared out of nowhere.
      Assert.Empty(graph.Nodes);
      Assert.Empty(graph.Edges);
    }
  }

  /// <summary>
  /// Tests that relationship and POI counts shift the likelihood toward larger sizes.
  /// </summary>
  public sealed class BiasTests
  {
    [Fact]
    public void Distribute_WithRelationshipBias_MakesHubLargerThanLeavesOnAverage()
    {
      // Arrange: Star graph - one hub with the maximum degree, many leaves, no POIs, so
      // only the relationship bias can act.
      var options = ProcNodeRelSizeOpt.Default with
      {
        Skew = 0f,
        BiasRelationships = 1f,
        BiasPois = 0f,
      };
      var hubTotal = 0;
      var leafTotal = 0;
      var leafSamples = 0;

      // Act: Average the assigned size of hub and leaves over many seeds.
      for (var seed = 0; seed < 100; seed++)
      {
        var graph = CreateStarGraph(12);
        new ProcNodeRelSize(graph, options, seed).Execute();
        hubTotal += graph.Nodes[0].RelSize;
        for (var i = 1; i < graph.Nodes.Count; i++)
        {
          leafTotal += graph.Nodes[i].RelSize;
          leafSamples++;
        }
      }

      // Assert: The hub averages clearly larger than the leaves.
      var hubAverage = hubTotal / 100.0;
      var leafAverage = leafTotal / (double)leafSamples;
      Assert.True(hubAverage > leafAverage, $"Hub average {hubAverage} is not above leaf average {leafAverage}.");
    }

    [Fact]
    public void Distribute_WithPoiBias_MakesPoiNodeLargerThanOthersOnAverage()
    {
      // Arrange: Star graph with a single POI on the hub; relationship bias off, so
      // only the POI bias can act.
      var options = ProcNodeRelSizeOpt.Default with
      {
        Skew = 0f,
        BiasRelationships = 0f,
        BiasPois = 1f,
      };
      var poiTotal = 0;
      var otherTotal = 0;
      var otherSamples = 0;

      // Act: Average the assigned size of the POI node and the rest over many seeds.
      for (var seed = 0; seed < 100; seed++)
      {
        var graph = CreateStarGraph(12, poiNodeIndex: 0, poiCount: 1);
        new ProcNodeRelSize(graph, options, seed).Execute();
        poiTotal += graph.Nodes[0].RelSize;
        for (var i = 1; i < graph.Nodes.Count; i++)
        {
          otherTotal += graph.Nodes[i].RelSize;
          otherSamples++;
        }
      }

      // Assert: The POI node averages clearly larger than the others.
      var poiAverage = poiTotal / 100.0;
      var otherAverage = otherTotal / (double)otherSamples;
      Assert.True(poiAverage > otherAverage, $"POI average {poiAverage} is not above other average {otherAverage}.");
    }

    [Fact]
    public void Distribute_WithSeveralPois_PullsHarderThanSinglePoi()
    {
      // Arrange: Two POIs on the first node, one on the second, none elsewhere; only two
      // steps, so the comparison is a simple bigger/smaller likelihood.
      var options = ProcNodeRelSizeOpt.Default with
      {
        Steps = 2,
        Skew = 0f,
        BiasRelationships = 0f,
        BiasPois = 1f,
      };
      var doublePoiTotal = 0;
      var singlePoiTotal = 0;

      // Act: Average the assigned size of both POI nodes over many seeds.
      for (var seed = 0; seed < 1000; seed++)
      {
        var graph = CreateChainGraph(10);
        graph.Nodes[0].Pois.Add(CreatePoi(0, 0));
        graph.Nodes[0].Pois.Add(CreatePoi(0, 1));
        graph.Nodes[1].Pois.Add(CreatePoi(1, 0));
        new ProcNodeRelSize(graph, options, seed).Execute();
        doublePoiTotal += graph.Nodes[0].RelSize;
        singlePoiTotal += graph.Nodes[1].RelSize;
      }

      // Assert: Two POIs (full normalization) pull harder than one (half normalization).
      var doubleAverage = doublePoiTotal / 1000.0;
      var singleAverage = singlePoiTotal / 1000.0;
      Assert.True(doubleAverage > singleAverage, $"Two-POI average {doubleAverage} is not above single-POI average {singleAverage}.");
    }

    [Fact]
    public void Distribute_WithBias_StillAllowsAnyNodeAnySize()
    {
      // Arrange: Strong POI bias on one node of the graph.
      var options = ProcNodeRelSizeOpt.Default with
      {
        Skew = 0f,
        BiasRelationships = 0f,
        BiasPois = 1f,
      };
      var stepValues = options.Prepare();
      var sawSmallPoiNode = false;
      var sawLargePlainNode = false;

      // Act: Watch the extremes across many seeds.
      for (var seed = 0; seed < 50; seed++)
      {
        var graph = CreateStarGraph(10, poiNodeIndex: 0, poiCount: 1);
        new ProcNodeRelSize(graph, options, seed).Execute();
        if (graph.Nodes[0].RelSize == stepValues[0]) sawSmallPoiNode = true;
        for (var i = 1; i < graph.Nodes.Count; i++)
        {
          if (graph.Nodes[i].RelSize == stepValues[stepValues.Length - 1]) sawLargePlainNode = true;
        }
      }

      // Assert: Bias only shifts likelihood - both extremes stay reachable.
      Assert.True(sawSmallPoiNode, "The POI node never landed on the smallest step.");
      Assert.True(sawLargePlainNode, "No plain node ever landed on the largest step.");
    }

    [Fact]
    public void Distribute_WithEqualPoiCounts_BiasChangesNothing()
    {
      // Arrange: No POIs anywhere, so the POI normalization has nothing to distinguish.
      var biased = ProcNodeRelSizeOpt.Default with
      {
        Skew = 0.4f,
        BiasRelationships = 0f,
        BiasPois = 1f,
      };
      var plain = biased with { BiasPois = 0f };

      // Act & Assert: The biased run matches the unbiased run for the same seed.
      for (var seed = 0; seed < 5; seed++)
      {
        var biasedGraph = CreateChainGraph(10);
        var plainGraph = CreateChainGraph(10);
        new ProcNodeRelSize(biasedGraph, biased, seed).Execute();
        new ProcNodeRelSize(plainGraph, plain, seed).Execute();
        Assert.Equal(GetAssignments(biasedGraph), GetAssignments(plainGraph));
      }
    }

    [Fact]
    public void Distribute_WithEqualDegrees_BiasChangesNothing()
    {
      // Arrange: Cycle graph - every node has the same degree, so relationship
      // normalization has nothing to distinguish.
      var biased = ProcNodeRelSizeOpt.Default with
      {
        Skew = 0.4f,
        BiasRelationships = 1f,
        BiasPois = 0f,
      };
      var plain = biased with { BiasRelationships = 0f };

      // Act & Assert: The biased run matches the unbiased run for the same seed.
      for (var seed = 0; seed < 5; seed++)
      {
        var biasedGraph = CreateCycleGraph(10);
        var plainGraph = CreateCycleGraph(10);
        new ProcNodeRelSize(biasedGraph, biased, seed).Execute();
        new ProcNodeRelSize(plainGraph, plain, seed).Execute();
        Assert.Equal(GetAssignments(biasedGraph), GetAssignments(plainGraph));
      }
    }
  }

  /// <summary>Tests for reproducibility and isolation of the assignment.</summary>
  public sealed class DeterminismTests
  {
    [Fact]
    public void Distribute_WithSameSeed_ProducesIdenticalAssignments()
    {
      // Arrange: Two structurally identical graphs and the same options.
      var first = CreateStarGraph(12, poiNodeIndex: 3, poiCount: 2);
      var second = CreateStarGraph(12, poiNodeIndex: 3, poiCount: 2);

      // Act: Distribute both with the same seed.
      new ProcNodeRelSize(first, ProcNodeRelSizeOpt.Default, Seed).Execute();
      new ProcNodeRelSize(second, ProcNodeRelSizeOpt.Default, Seed).Execute();

      // Assert: Same identifiers with the same sizes, in any comparison order.
      GraphTestSupport.AssertEqualGraphs(first, second);
      Assert.Equal(GetAssignments(first), GetAssignments(second));
    }

    [Fact]
    public void Distribute_WithDifferentSeed_ProducesDifferentAssignments()
    {
      // Arrange: Same structure, different seeds.
      var first = CreateStarGraph(30);
      var second = CreateStarGraph(30);

      // Act: Distribute both graphs.
      new ProcNodeRelSize(first, ProcNodeRelSizeOpt.Default, seedGraph: 1).Execute();
      new ProcNodeRelSize(second, ProcNodeRelSizeOpt.Default, seedGraph: 2).Execute();

      // Assert: The size sequences differ - the dominant step and draws are seed-driven.
      var firstSizes = first.Nodes.Select(node => node.RelSize).ToList();
      var secondSizes = second.Nodes.Select(node => node.RelSize).ToList();
      Assert.NotEqual(firstSizes, secondSizes);
    }

    [Fact]
    public void Distribute_IsIndependentOfNodeInsertionOrder()
    {
      // Arrange: Same graph twice, but the node and edge lists of the second one reversed.
      var original = CreateStarGraph(12, poiNodeIndex: 5, poiCount: 1);
      var reversed = CreateStarGraph(12, poiNodeIndex: 5, poiCount: 1);
      reversed.Nodes.Reverse();
      reversed.Edges.Reverse();

      // Act: Distribute both with the same seed.
      new ProcNodeRelSize(original, ProcNodeRelSizeOpt.Default, Seed).Execute();
      new ProcNodeRelSize(reversed, ProcNodeRelSizeOpt.Default, Seed).Execute();

      // Assert: Every identifier keeps the size it got regardless of list order.
      Assert.Equal(GetAssignments(original), GetAssignments(reversed));
    }

    [Fact]
    public void Distribute_ModifiesOnlyRelSize()
    {
      // Arrange: Graph with known identifiers, edges and POIs, plus a size snapshot.
      var graph = CreateStarGraph(8, poiNodeIndex: 2, poiCount: 2);
      var nodeIdsBefore = graph.Nodes.Select(node => node.Id).ToList();
      var edgesBefore = graph.Edges.Select(edge => (edge.Id, edge.From, edge.To)).ToList();
      var poisBefore = graph.Nodes
        .SelectMany(node => node.Pois)
        .Select(poi => (poi.Id, poi.Type))
        .ToList();

      // Act: Distribute.
      new ProcNodeRelSize(graph, ProcNodeRelSizeOpt.Default, Seed).Execute();

      // Assert: Node order, edges and POIs are untouched; only sizes were rewritten.
      Assert.Equal(nodeIdsBefore, graph.Nodes.Select(node => node.Id).ToList());
      Assert.Equal(edgesBefore, graph.Edges.Select(edge => (edge.Id, edge.From, edge.To)).ToList());
      Assert.Equal(poisBefore, graph.Nodes.SelectMany(node => node.Pois).Select(poi => (poi.Id, poi.Type)).ToList());
    }

    [Fact]
    public void Distribute_Twice_ProducesTheSameAssignment()
    {
      // Arrange: One graph distributed once.
      var graph = CreateStarGraph(12, poiNodeIndex: 4, poiCount: 1);
      new ProcNodeRelSize(graph, ProcNodeRelSizeOpt.Default, Seed).Execute();
      var firstRun = GetAssignments(graph);

      // Act: Distribute the very same graph again with the same inputs.
      new ProcNodeRelSize(graph, ProcNodeRelSizeOpt.Default, Seed).Execute();

      // Assert: Sizes do not feed back into the draws, so the second run is a no-op.
      Assert.Equal(firstRun, GetAssignments(graph));
    }
  }

  //
  // Graph fixtures
  //

  /// <summary>
  /// Creates a chain graph: node 0 - node 1 - ... with deterministic identifiers.
  /// </summary>
  /// <param name="nodeCount">Amount of nodes; at least 1.</param>
  /// <param name="poiNodeIndex">Node that receives POIs, or -1 for none.</param>
  /// <param name="poiCount">Amount of POIs to place on <paramref name="poiNodeIndex"/>.</param>
  /// <returns>The constructed graph with <c>nodeCount - 1</c> edges.</returns>
  private static LilGraph CreateChainGraph(int nodeCount, int poiNodeIndex = -1, int poiCount = 0)
  {
    var graph = CreateNodes(nodeCount, poiNodeIndex, poiCount);
    for (var i = 0; i + 1 < nodeCount; i++)
    {
      graph.Edges.Add(CreateEdge(i, i + 1));
    }

    return graph;
  }

  /// <summary>
  /// Creates a star graph: node 0 (the hub) connected to every other node.
  /// </summary>
  /// <param name="nodeCount">Amount of nodes; at least 2.</param>
  /// <param name="poiNodeIndex">Node that receives POIs, or -1 for none.</param>
  /// <param name="poiCount">Amount of POIs to place on <paramref name="poiNodeIndex"/>.</param>
  /// <returns>The constructed graph with <c>nodeCount - 1</c> edges.</returns>
  private static LilGraph CreateStarGraph(int nodeCount, int poiNodeIndex = -1, int poiCount = 0)
  {
    var graph = CreateNodes(nodeCount, poiNodeIndex, poiCount);
    for (var i = 1; i < nodeCount; i++)
    {
      graph.Edges.Add(CreateEdge(0, i));
    }

    return graph;
  }

  /// <summary>
  /// Creates a cycle graph: every node has exactly the same degree of two.
  /// </summary>
  /// <param name="nodeCount">Amount of nodes; at least 3.</param>
  /// <returns>The constructed graph with <c>nodeCount</c> edges.</returns>
  private static LilGraph CreateCycleGraph(int nodeCount)
  {
    var graph = CreateNodes(nodeCount, poiNodeIndex: -1, poiCount: 0);
    for (var i = 0; i < nodeCount; i++)
    {
      graph.Edges.Add(CreateEdge(i, (i + 1) % nodeCount));
    }

    return graph;
  }

  /// <summary>
  /// Creates the node list with deterministic identifiers and initial sizes of 1.
  /// </summary>
  /// <param name="nodeCount">Amount of nodes.</param>
  /// <param name="poiNodeIndex">Node that receives POIs, or -1 for none.</param>
  /// <param name="poiCount">Amount of POIs to place on that node.</param>
  /// <returns>A graph holding the nodes; no edges.</returns>
  private static LilGraph CreateNodes(int nodeCount, int poiNodeIndex, int poiCount)
  {
    var graph = new LilGraph();
    for (var i = 0; i < nodeCount; i++)
    {
      var node = new LilNode { Id = Id(i), RelSize = 1 };
      if (i == poiNodeIndex)
      {
        for (var poi = 0; poi < poiCount; poi++)
        {
          node.Pois.Add(CreatePoi(i, poi));
        }
      }

      graph.Nodes.Add(node);
    }

    return graph;
  }

  /// <summary>
  /// Creates an edge between two node indices with a deterministic identifier.
  /// </summary>
  /// <param name="fromIndex">Source node index.</param>
  /// <param name="toIndex">Target node index; must differ.</param>
  /// <returns>The constructed edge, bidirectional and unconditional.</returns>
  private static LilEdge CreateEdge(int fromIndex, int toIndex)
  {
    // Low/high ordering keeps identifiers unique per unordered pair.
    var low = Math.Min(fromIndex, toIndex);
    var high = Math.Max(fromIndex, toIndex);
    return new LilEdge
    {
      Id = Guid.Parse($"00000000-0000-0000-0001-{low:D6}{high:D6}"),
      From = Id(fromIndex),
      To = Id(toIndex),
    };
  }

  /// <summary>
  /// Creates a deterministic POI identifier unique per node and slot.
  /// </summary>
  /// <param name="nodeIndex">Index of the owning node.</param>
  /// <param name="slot">POI slot on that node.</param>
  /// <returns>A distinct, non-empty identifier.</returns>
  private static LilPoi CreatePoi(int nodeIndex, int slot)
  {
    return new LilPoi
    {
      Id = Guid.Parse($"00000000-0000-0000-0002-{nodeIndex:D6}{slot:D6}"),
      Type = EnPoiType.Start,
    };
  }
}
