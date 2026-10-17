using LostInLimbo.LevelGen.Data.Config.Graph;
using LostInLimbo.LevelGen.Data.Result.Graph;
using LostInLimbo.LevelGen.Internal.Generator.Graph.Builders;
using LostInLimbo.LevelGen.Internal.Generator.Graph.Distributor;
using LostInLimbo.LevelGen.Internal.Generator.Graph.Validator;

namespace LostInLimbo.LevelGen.Tests.Generator.Graph.Builders;

/// <summary>
/// Tests <see cref="LinearGraphBuilder"/>: chain shape, start/exit placement, the
/// extra-relationships pass (caps, skip window, weighting), soft-constraint behavior,
/// determinism and beatability.
/// </summary>
/// <remarks>
/// The builder must succeed for any validated config: size, connectivity and complexity are
/// soft constraints, so most tests assert shape outcomes rather than failure modes. Parallel
/// passages (skeleton plus one extra between an adjacent pair) are legal here, so edge
/// assertions pass <c>allowDuplicates: true</c> and apply pair-count rules of their own.
/// </remarks>
public sealed class LinearGraphBuilderTests
{
  /// <summary>Mirrors <c>LinearGraphBuilder.MaxExtraDegree</c>: cap of the extra-relationships phase per direction.</summary>
  private const int MaxExtraDegree = LinearGraphBuilder.MaxExtraDegree;

  /// <summary>Mirrors <c>LinearGraphBuilder.MaxSkipDistance</c>: nodes an extra relationship may skip.</summary>
  private const int MaxSkipDistance = LinearGraphBuilder.MaxSkipDistance;

  /// <summary>Tests for the skeleton shape: node count, minimality and exact chain form.</summary>
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
      var (success, graph) = GraphTestSupport.Build(EnGraphBuilder.Linear, new CnfGraph { Size = size });

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
    public void Build_WithZeroConnectivity_ProducesExactChain(int size)
    {
      // Act: Build a graph without any extra relationships.
      var (success, graph) = GraphTestSupport.Build(EnGraphBuilder.Linear, new CnfGraph { Size = size });

      // Assert: The skeleton is exactly the chain over all nodes - size minus one
      // relationships, each consecutive pair once - and it is connected.
      Assert.True(success);
      Assert.Equal(Math.Max(0, size - 1), graph.Edges.Count);
      GraphAssertions.AssertExactChain(graph, size);
      GraphTestSupport.AssertBeatable(graph, "Chain graph must be connected.");
    }

    [Theory]
    [InlineData(10, 3)]
    [InlineData(5, 4)]
    [InlineData(20, 8)]
    [InlineData(100, 50)]
    public void Build_WithConnectivityBelowCapacity_ConsumesAllConnectivity(int size, int connectivity)
    {
      for (var seed = 0; seed < 5; seed++)
      {
        // Act: Build a graph whose requested extras fit well under the capacity.
        var (success, graph) = GraphTestSupport.Build(EnGraphBuilder.Linear, new CnfGraph
        {
          Size = size,
          Connectivity = connectivity,
        }, seed);

        // Assert: Every requested extra relationship exists on top of the chain, and the
        // graph stays connected.
        Assert.True(success, $"Build failed for size {size}, seed {seed}.");
        Assert.Equal(size - 1 + connectivity, graph.Edges.Count);
        GraphTestSupport.AssertBeatable(graph, $"Graph must stay connected (size {size}, seed {seed}).");
      }
    }
  }

  /// <summary>Tests for start and exit placement at the chain ends.</summary>
  public sealed class PoiTests
  {
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(5)]
    [InlineData(10)]
    public void Build_AlwaysPlacesStartAndExitAtChainEnds(int size)
    {
      // Act: Build a graph of the given size.
      var (_, graph) = GraphTestSupport.Build(EnGraphBuilder.Linear, new CnfGraph { Size = size });

      // Assert: Exactly two POIs exist across the graph, one of each required type.
      var pois = graph.Nodes.SelectMany(node => node.Pois).ToList();
      Assert.Equal(2, pois.Count);
      Assert.Single(pois.Where(poi => poi.Type == EnPoiType.Start));
      Assert.Single(pois.Where(poi => poi.Type == EnPoiType.Exit));

      // Assert: The start lives in the first chain node, the exit in the last one.
      Assert.Single(graph.Nodes[0].Pois.Where(poi => poi.Type == EnPoiType.Start));
      Assert.Single(graph.Nodes[^1].Pois.Where(poi => poi.Type == EnPoiType.Exit));

      if (size > 1)
      {
        // Assert: The end nodes hold no foreign POI (a single-node graph shares one node).
        Assert.DoesNotContain(graph.Nodes[0].Pois, poi => poi.Type == EnPoiType.Exit);
        Assert.DoesNotContain(graph.Nodes[^1].Pois, poi => poi.Type == EnPoiType.Start);
      }
    }
  }

  /// <summary>Tests for the extra-relationships pass: capacity, caps, skip window and weighting.</summary>
  public sealed class ExtraRelationshipTests
  {
    [Theory]
    [InlineData(0)]
    [InlineData(10)]
    public void Build_WithSizeOne_WithAnyConnectivity_ProducesSingleNodeWithoutEdges(int connectivity)
    {
      // Act: Build the degenerate one-node graph with and without extra connectivity.
      var (success, graph) = GraphTestSupport.Build(EnGraphBuilder.Linear, new CnfGraph
      {
        Size = 1,
        Connectivity = connectivity,
      });

      // Assert: No relationship is possible, yet both POIs live on the single node.
      Assert.True(success);
      var node = Assert.Single(graph.Nodes);
      Assert.Empty(graph.Edges);
      Assert.Single(node.Pois.Where(poi => poi.Type == EnPoiType.Start));
      Assert.Single(node.Pois.Where(poi => poi.Type == EnPoiType.Exit));
    }

    [Fact]
    public void Build_WithSizeTwo_WithConnectivity_AddsOneParallelPassage()
    {
      // Act: Two nodes can only link to each other; one extra relationship is the capacity.
      var (success, graph) = GraphTestSupport.Build(EnGraphBuilder.Linear, new CnfGraph
      {
        Size = 2,
        Connectivity = 1,
      });

      // Assert: The skeleton passage and its parallel extra passage both exist, between
      // the same two nodes but with distinct identifiers.
      Assert.True(success);
      Assert.Equal(2, graph.Edges.Count);
      Assert.Equal(graph.Edges[0].From, graph.Edges[1].From);
      Assert.Equal(graph.Edges[0].To, graph.Edges[1].To);
      Assert.NotEqual(graph.Edges[0].Id, graph.Edges[1].Id);
    }

    [Theory]
    [InlineData(3, 10000)]
    [InlineData(5, 10000)]
    [InlineData(10, 10000)]
    [InlineData(25, 100000)]
    public void Build_WithExcessiveConnectivity_ObeysExtraRelationshipCaps(int size, int connectivity)
    {
      for (var seed = 0; seed < 20; seed++)
      {
        // Act: Ask for far more extra relationships than the graph can hold.
        var (success, graph) = GraphTestSupport.Build(EnGraphBuilder.Linear, new CnfGraph
        {
          Size = size,
          Connectivity = connectivity,
        }, seed);

        // Assert: The build succeeds, keeps at least the skeleton and every constraint of
        // the extra-relationships pass holds: chain order, skip window, per-direction
        // degree caps and the one-extra-per-pair rule.
        Assert.True(success, $"Build failed for size {size}, seed {seed}.");
        Assert.True(graph.Edges.Count >= size - 1, $"Skeleton missing (size {size}, seed {seed}).");
        GraphAssertions.AssertForwardAndSkipWindow(graph);
        GraphAssertions.AssertExtraDegreeCaps(graph);
        GraphAssertions.AssertPairPassageCounts(graph);
        GraphTestSupport.AssertValidEdges(graph, allowDuplicates: true);
        GraphTestSupport.AssertOpenPassages(graph);
        GraphTestSupport.AssertBeatable(graph, $"Graph must stay connected (size {size}, seed {seed}).");
      }
    }

    [Theory]
    [InlineData(5, 4)]
    [InlineData(12, 20)]
    [InlineData(40, 100)]
    public void Build_WithExtraRelationships_StayForwardAndWithinSkipWindow(int size, int connectivity)
    {
      for (var seed = 0; seed < 10; seed++)
      {
        // Act: Build a graph with extra relationships across several seeds.
        var (_, graph) = GraphTestSupport.Build(EnGraphBuilder.Linear, new CnfGraph
        {
          Size = size,
          Connectivity = connectivity,
        }, seed);

        // Assert: Every relationship runs from the earlier node to the later node and
        // skips no more than MaxSkipDistance nodes in between.
        GraphAssertions.AssertForwardAndSkipWindow(graph);
      }
    }

    [Fact]
    public void Build_WithSizeThree_ExcessiveConnectivity_SaturatesEveryPair()
    {
      // Act: Three nodes offer exactly three extra-relationship slots under all caps.
      var (success, graph) = GraphTestSupport.Build(EnGraphBuilder.Linear, new CnfGraph
      {
        Size = 3,
        Connectivity = 10000,
      });

      // Assert: Every node pair carries its full complement of passages - two for the
      // adjacent pairs (skeleton plus extra), one for the skipping pair - five edges in
      // total, and the degree caps still hold.
      Assert.True(success);
      Assert.Equal(5, graph.Edges.Count);
      GraphAssertions.AssertPairPassageCounts(graph);
      GraphAssertions.AssertExtraDegreeCaps(graph);
    }

    [Fact]
    public void Build_WithExtraRelationships_FavorsAdjacentTargetsOverDistantOnes()
    {
      // Act: Collect every extra relationship by chain distance over many seeds; chain
      // skeleton edges are excluded (an adjacent pair contributes only its second passage).
      var extrasByDistance = new int[MaxSkipDistance + 2];
      for (var seed = 0; seed < 100; seed++)
      {
        var (_, graph) = GraphTestSupport.Build(EnGraphBuilder.Linear, new CnfGraph
        {
          Size = 30,
          Connectivity = 15,
        }, seed);
        foreach (var (pair, passageCount) in GraphAssertions.GetPassagesByPair(graph))
          extrasByDistance[pair.Hi - pair.Lo] += passageCount - (pair.Hi - pair.Lo == 1 ? 1 : 0);
      }

      // Assert: Connectivity was spent in full, and the linear weighting (3:2:1 over
      // adjacent, skip-one, skip-two) makes every distance strictly more likely than the
      // next one behind it.
      Assert.Equal(15 * 100, extrasByDistance.Sum());
      Assert.True(extrasByDistance[1] > extrasByDistance[2],
        $"Adjacent {extrasByDistance[1]} must beat skip-one {extrasByDistance[2]}.");
      Assert.True(extrasByDistance[2] > extrasByDistance[3],
        $"Skip-one {extrasByDistance[2]} must beat skip-two {extrasByDistance[3]}.");
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
        var (_, graph) = GraphTestSupport.Build(EnGraphBuilder.Linear, new CnfGraph
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
      var first = GraphTestSupport.Build(EnGraphBuilder.Linear, config);
      var second = GraphTestSupport.Build(EnGraphBuilder.Linear, config);

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
      var first = GraphTestSupport.Build(EnGraphBuilder.Linear, config, seedGraph: 1);
      var second = GraphTestSupport.Build(EnGraphBuilder.Linear, config, seedGraph: 2);

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
      var reference = GraphTestSupport.Build(EnGraphBuilder.Linear, new CnfGraph { Size = 8, Connectivity = 4 });
      var varied = GraphTestSupport.Build(EnGraphBuilder.Linear, new CnfGraph
      {
        Size = 8,
        Connectivity = 4,
        Complexity = complexity,
      });

      // Assert: The obstacle pass is a placeholder - complexity is ignored for now.
      GraphTestSupport.AssertEqualGraphs(reference.Graph, varied.Graph);
    }

    [Fact]
    public void Build_ProducesDistinctIdentifiers()
    {
      // Act: Build a graph with many nodes and relationships.
      var (_, graph) = GraphTestSupport.Build(EnGraphBuilder.Linear, new CnfGraph { Size = 50, Connectivity = 20 });

      // Assert: All identifiers are non-empty, unique per collection and POIs do not
      // collide with nodes - parallel passages carry distinct identifiers too.
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

  /// <summary>Tests for beatability of the produced graphs.</summary>
  public sealed class ValidationTests
  {
    [Theory]
    [InlineData(1, 0)]
    [InlineData(1, 25)]
    [InlineData(2, 0)]
    [InlineData(2, 5)]
    [InlineData(3, 3)]
    [InlineData(7, 10)]
    [InlineData(15, 0)]
    [InlineData(15, 40)]
    [InlineData(50, 100)]
    public void Build_WithVariousConfigs_ProducesBeatableGraph(int size, int connectivity)
    {
      for (var seed = 0; seed < 5; seed++)
      {
        // Act: Build a graph across sizes, connectivity levels and seeds.
        var (success, graph) = GraphTestSupport.Build(EnGraphBuilder.Linear, new CnfGraph
        {
          Size = size,
          Connectivity = connectivity,
        }, seed);

        // Assert: The build succeeds and the beatability simulation passes: structure is
        // valid, every passage is open for now, and start reaches exit with all nodes
        // reachable.
        Assert.True(success, $"Build failed for size {size}, seed {seed}.");
        GraphTestSupport.AssertValidEdges(graph, allowDuplicates: true);
        GraphTestSupport.AssertOpenPassages(graph);
        GraphTestSupport.AssertBeatable(graph, $"Graph must stay connected (size {size}, seed {seed}).");
        GraphTestSupport.AssertBeatable(graph);
      }
    }
  }

  //
  // Shared assertions specific to LinearGraphBuilder.
  //

  /// <summary>
  /// Builder-specific structural assertions of <see cref="LinearGraphBuilder"/>: kept out of
  /// the builder-neutral <see cref="GraphTestSupport"/> because they know the chain shape.
  /// </summary>
  private static class GraphAssertions
  {
    /// <summary>
    /// Maps node identifiers to chain positions; the builder appends nodes in chain order.
    /// </summary>
    /// <param name="graph">Graph to map.</param>
    /// <returns>Node identifier to chain position lookup.</returns>
    public static Dictionary<Guid, int> GetPositions(LilGraph graph)
    {
      return graph.Nodes
        .Select((node, index) => (node.Id, index))
        .ToDictionary(pair => pair.Id, pair => pair.index);
    }

    /// <summary>
    /// Asserts that the graph's edges are exactly the consecutive chain pairs, each present
    /// once, and in earlier-to-later orientation.
    /// </summary>
    /// <param name="graph">Graph to check.</param>
    /// <param name="size">Expected amount of nodes.</param>
    public static void AssertExactChain(LilGraph graph, int size)
    {
      var positions = GetPositions(graph);
      var actual = graph.Edges
        .Select(edge => (
          Lo: Math.Min(positions[edge.From], positions[edge.To]),
          Hi: Math.Max(positions[edge.From], positions[edge.To])))
        .OrderBy(pair => pair.Lo)
        .ThenBy(pair => pair.Hi)
        .ToList();
      var expected = Enumerable.Range(0, Math.Max(0, size - 1)).Select(i => (Lo: i, Hi: i + 1)).ToList();

      // Assert: The edge set equals the chain pairs, no more and no less.
      Assert.Equal(expected, actual);
    }

    /// <summary>
    /// Asserts that every relationship runs from the earlier node to the later node of the
    /// chain and skips no more than <see cref="MaxSkipDistance"/> nodes.
    /// </summary>
    /// <param name="graph">Graph to check.</param>
    public static void AssertForwardAndSkipWindow(LilGraph graph)
    {
      var positions = GetPositions(graph);
      foreach (var edge in graph.Edges)
      {
        var from = positions[edge.From];
        var to = positions[edge.To];

        // Assert: The relationship runs forward along the chain.
        Assert.True(from < to, $"Edge runs backward: position {from} -> {to}.");

        // Assert: The relationship skips no more nodes than the skip window allows.
        Assert.True(
          to - from <= MaxSkipDistance + 1,
          $"Edge skips {to - from - 1} nodes, limit is {MaxSkipDistance}.");
      }
    }

    /// <summary>
    /// Asserts that no node gained more than <see cref="MaxExtraDegree"/> extra relationships
    /// as source and as target; the skeleton's own single in and single out relationship are
    /// subtracted from the raw counts.
    /// </summary>
    /// <param name="graph">Graph to check.</param>
    public static void AssertExtraDegreeCaps(LilGraph graph)
    {
      var positions = GetPositions(graph);
      var size = graph.Nodes.Count;
      var outCounts = new int[size];
      var inCounts = new int[size];
      foreach (var edge in graph.Edges)
      {
        outCounts[positions[edge.From]]++;
        inCounts[positions[edge.To]]++;
      }

      for (var i = 0; i < size; i++)
      {
        // Assert: The skeleton contributes exactly one outgoing relationship to every node
        // but the last, and exactly one incoming relationship to every node but the first;
        // anything on top is an extra that must stay under the cap in both directions.
        var extraOut = outCounts[i] - (i < size - 1 ? 1 : 0);
        var extraIn = inCounts[i] - (i > 0 ? 1 : 0);
        Assert.InRange(extraOut, 0, MaxExtraDegree);
        Assert.InRange(extraIn, 0, MaxExtraDegree);
      }
    }

    /// <summary>
    /// Asserts the one-extra-per-pair rule: an adjacent pair carries the skeleton plus at
    /// most one extra passage, a skipping pair at most its single extra passage.
    /// </summary>
    /// <param name="graph">Graph to check.</param>
    public static void AssertPairPassageCounts(LilGraph graph)
    {
      foreach (var (pair, passageCount) in GetPassagesByPair(graph))
      {
        var allowed = pair.Hi - pair.Lo == 1 ? 2 : 1;

        // Assert: The pair holds no more passages than the rule allows.
        Assert.True(
          passageCount <= allowed,
          $"Node pair {pair.Lo}-{pair.Hi} carries {passageCount} passages, allowed {allowed}.");
      }
    }

    /// <summary>
    /// Counts the passages per unordered node pair, keyed by chain positions.
    /// </summary>
    /// <param name="graph">Graph to inspect.</param>
    /// <returns>Passage count per earlier-to-later node pair.</returns>
    public static Dictionary<(int Lo, int Hi), int> GetPassagesByPair(LilGraph graph)
    {
      var positions = GetPositions(graph);
      var counts = new Dictionary<(int Lo, int Hi), int>();
      foreach (var edge in graph.Edges)
      {
        var lo = Math.Min(positions[edge.From], positions[edge.To]);
        var hi = Math.Max(positions[edge.From], positions[edge.To]);
        counts[(lo, hi)] = counts.GetValueOrDefault((lo, hi)) + 1;
      }

      return counts;
    }
  }
}
