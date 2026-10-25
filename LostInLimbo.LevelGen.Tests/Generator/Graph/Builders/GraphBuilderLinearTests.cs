using LostInLimbo.LevelGen.Data.Config.Graph;
using LostInLimbo.LevelGen.Data.Config.Graph.Builder;
using LostInLimbo.LevelGen.Data.Result.Graph;
using LostInLimbo.LevelGen.Internal.Generator.Graph.Builders;

namespace LostInLimbo.LevelGen.Tests.Generator.Graph.Builders;

/// <summary>
/// Tests <see cref="GraphBuilderLinear"/>: chain shape, start/exit placement, the
/// extra-relationships pass (caps, skip window, weighting), the obstacle pass (conditions,
/// solution placement, saturation), soft-constraint behavior, determinism and beatability.
/// </summary>
/// <remarks>
/// The builder must succeed for any validated config: size, connectivity and complexity are
/// soft constraints, so most tests assert shape outcomes rather than failure modes. Parallel
/// passages (skeleton plus one extra between an adjacent pair) are legal here, so edge
/// assertions pass <c>allowDuplicates: true</c> and apply pair-count rules of their own.
/// </remarks>
public sealed class GraphBuilderLinearTests
{
  /// <summary>Cap of the extra-relationships phase per direction.</summary>
  private static readonly int MaxExtraDegree = CnfGraphBuilderLinear.Default.MaxExtraDegree;

  /// <summary>Hard cap of relationships per node, set on every node by the builder.</summary>
  private static readonly int MaxEdges = CnfGraphBuilderLinear.Default.MaxEdges;

  /// <summary>Nodes an extra relationship may skip.</summary>
  private static readonly int MaxSkipDistance = CnfGraphBuilderLinear.Default.MaxSkipDistance;

  /// <summary>Conditions one relationship can carry.</summary>
  private static readonly int MaxConditionPerRel = CnfGraphBuilderLinear.Default.MaxConditionPerRel;

  /// <summary>Nodes a solution may sit away from its guarded passage.</summary>
  private static readonly int MaxConditionDistance = CnfGraphBuilderLinear.Default.MaxConditionDistance;

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
      // Act: Build a graph of the given size, without the default secret node.
      var (result, graph) = GraphTestSupport.Build(EnGraphBuilder.Linear, new CnfGraph
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
    public void Build_WithZeroConnectivity_ProducesExactChain(int size)
    {
      // Act: Build a graph without any extra relationships, without the default secret
      // node.
      var (result, graph) = GraphTestSupport.Build(EnGraphBuilder.Linear, new CnfGraph
      {
        Size = size,
        ProcOpts = GraphTestSupport.NoSecrets,
      });

      // Assert: The skeleton is exactly the chain over all nodes - size minus one
      // relationships, each consecutive pair once - and it is connected.
      Assert.True(result.Success);
      Assert.Equal(Math.Max(0, size - 1), graph.Edges.Count);
      GraphAssertions.AssertExactChain(graph, size);
      GraphTestAssert.AssertBeatable(graph, "Chain graph must be connected.");
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
        // Act: Build a graph whose requested extras fit well under the capacity, without
        // the default secret node.
        var (result, graph) = GraphTestSupport.Build(EnGraphBuilder.Linear, new CnfGraph
        {
          Size = size,
          Connectivity = connectivity,
          ProcOpts = GraphTestSupport.NoSecrets,
        }, seed);

        // Assert: Every requested extra relationship exists on top of the chain and the
        // graph stays connected.
        Assert.True(result.Success, $"Build failed for size {size}, seed {seed}.");
        Assert.Equal(size - 1 + connectivity, graph.Edges.Count);
        GraphTestAssert.AssertBeatable(graph, $"Graph must stay connected (size {size}, seed {seed}).");
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

      // Assert: Exactly one POI of each type exists across the graph - Start, Exit and
      // the two objectives placed by the finalization pass.
      var pois = graph.Nodes.SelectMany(node => node.Pois).ToList();
      Assert.Equal(4, pois.Count);
      Assert.Single(pois.Where(poi => poi.Type == EnPoiType.Start));
      Assert.Single(pois.Where(poi => poi.Type == EnPoiType.Exit));
      Assert.Single(pois.Where(poi => poi.Type == EnPoiType.Goal));
      Assert.Single(pois.Where(poi => poi.Type == EnPoiType.OptionalGoal));

      // Assert: The start lives in the first chain node, the exit in the last one; the
      // secret node appended by finalization never carries Start, Exit or a mandatory
      // goal - only an optional goal may land there.
      var chain = graph.Nodes.Where(node => !node.Secret).ToList();
      Assert.Single(chain[0].Pois.Where(poi => poi.Type == EnPoiType.Start));
      Assert.Single(chain[^1].Pois.Where(poi => poi.Type == EnPoiType.Exit));
      var secret = Assert.Single(graph.Nodes.Where(node => node.Secret));
      Assert.DoesNotContain(secret.Pois, poi => poi.Type is EnPoiType.Start or EnPoiType.Exit or EnPoiType.Goal);

      if (size > 1)
      {
        // Assert: The end nodes hold no foreign POI (a single-node graph shares one node).
        Assert.DoesNotContain(chain[0].Pois, poi => poi.Type == EnPoiType.Exit);
        Assert.DoesNotContain(chain[^1].Pois, poi => poi.Type == EnPoiType.Start);
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
      var (result, graph) = GraphTestSupport.Build(EnGraphBuilder.Linear, new CnfGraph
      {
        Size = 1,
        Connectivity = connectivity,
      });

      // Assert: No relationship is possible, yet both POIs and the mandatory goal live on
      // the single ordinary node; the finalization pass appends one secret node hanging
      // off it, which hosts the optional goal (allowed in secret nodes).
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
    public void Build_WithSizeTwo_WithConnectivity_AddsOneParallelPassage()
    {
      // Act: Two nodes can only link to each other; one extra relationship is the
      // capacity. Secrets are disabled so only builder passages remain.
      var (result, graph) = GraphTestSupport.Build(EnGraphBuilder.Linear, new CnfGraph
      {
        Size = 2,
        Connectivity = 1,
        ProcOpts = GraphTestSupport.NoSecrets,
      });

      // Assert: The skeleton passage and its parallel extra passage both exist, between
      // the same two nodes but with distinct identifiers.
      Assert.True(result.Success);
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
        // Act: Ask for far more extra relationships than the graph can hold, without the
        // default secret node.
        var (result, graph) = GraphTestSupport.Build(EnGraphBuilder.Linear, new CnfGraph
        {
          Size = size,
          Connectivity = connectivity,
          ProcOpts = GraphTestSupport.NoSecrets,
        }, seed);

        // Assert: The build succeeds, keeps at least the skeleton and every constraint of
        // the extra-relationships pass holds: chain order, skip window, per-direction
        // degree caps, the one-extra-per-pair rule and the node MaxEdges hard cap.
        Assert.True(result.Success, $"Build failed for size {size}, seed {seed}.");
        Assert.True(graph.Edges.Count >= size - 1, $"Skeleton missing (size {size}, seed {seed}).");
        GraphAssertions.AssertForwardAndSkipWindow(graph);
        GraphAssertions.AssertExtraDegreeCaps(graph);
        GraphAssertions.AssertPairPassageCounts(graph);
        GraphTestAssert.AssertMaxEdges(graph);
        Assert.All(graph.Nodes, node => Assert.Equal(MaxEdges, node.MaxEdges));
        GraphTestAssert.AssertValidEdges(graph, allowDuplicates: true);
        GraphTestAssert.AssertOpenPassages(graph);
        GraphTestAssert.AssertBeatable(graph, $"Graph must stay connected (size {size}, seed {seed}).");
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
        // Act: Build a graph with extra relationships across several seeds, without the
        // default secret node.
        var (_, graph) = GraphTestSupport.Build(EnGraphBuilder.Linear, new CnfGraph
        {
          Size = size,
          Connectivity = connectivity,
          ProcOpts = GraphTestSupport.NoSecrets,
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
      // Secrets are disabled so only builder passages remain.
      var (result, graph) = GraphTestSupport.Build(EnGraphBuilder.Linear, new CnfGraph
      {
        Size = 3,
        Connectivity = 10000,
        ProcOpts = GraphTestSupport.NoSecrets,
      });

      // Assert: Every node pair carries its full complement of passages - two for the
      // adjacent pairs (skeleton plus extra), one for the skipping pair - five pair
      // passages in total - and the degree caps still hold.
      Assert.True(result.Success);
      Assert.Equal(5, graph.Edges.Count);
      GraphAssertions.AssertPairPassageCounts(graph);
      GraphAssertions.AssertExtraDegreeCaps(graph);
    }

    [Fact]
    public void Build_WithExtraRelationships_FavorsAdjacentTargetsOverDistantOnes()
    {
      // Act: Collect every extra relationship by chain distance over many seeds, without
      // the default secret node; chain skeleton edges are excluded (an adjacent pair
      // contributes only its second passage).
      var extrasByDistance = new int[MaxSkipDistance + 2];
      for (var seed = 0; seed < 100; seed++)
      {
        var (_, graph) = GraphTestSupport.Build(EnGraphBuilder.Linear, new CnfGraph
        {
          Size = 30,
          Connectivity = 15,
          ProcOpts = GraphTestSupport.NoSecrets,
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

  /// <summary>Tests for the obstacle pass: conditions, solution placement and saturation.</summary>
  public sealed class ObstacleTests
  {
    [Fact]
    public void Build_WithZeroComplexity_KeepsPassagesOpen()
    {
      // Act: Build without requesting obstacles.
      var (result, graph) = GraphTestSupport.Build(EnGraphBuilder.Linear, new CnfGraph
      {
        Size = 12,
        Connectivity = 5,
        Complexity = 0,
      });

      // Assert: No solution POI exists and every passage stays open.
      Assert.True(result.Success);
      Assert.Empty(GraphTestSupport.GetPois(graph, EnPoiType.Solution));
      GraphTestAssert.AssertOpenPassages(graph);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(8)]
    public void Build_WithComplexity_AddsOneSolutionPerObstacle(int complexity)
    {
      for (var seed = 0; seed < 5; seed++)
      {
        // Act: Plenty of nodes, so no relationship saturates and every requested
        // obstacle fits.
        var (result, graph) = GraphTestSupport.Build(EnGraphBuilder.Linear, new CnfGraph
        {
          Size = 20,
          Connectivity = 4,
          Complexity = complexity,
        }, seed);

        // Assert: One solution POI per obstacle, and every condition is a solution
        // requirement pointing at one of those POIs.
        Assert.True(result.Success, $"Build failed for complexity {complexity}, seed {seed}.");
        Assert.Equal(complexity, GraphTestSupport.GetPois(graph, EnPoiType.Solution).Count);

        var solutionIds = GraphTestSupport.GetPois(graph, EnPoiType.Solution).Select(poi => poi.Id).ToHashSet();
        foreach (var edge in graph.Edges)
        {
          foreach (var condition in edge.Conditions)
          {
            // Assert: The condition demands an existing solution POI.
            Assert.Equal(EnConditionType.RequiresSolution, condition.Type);
            Assert.Contains(condition.RequiresPoi, solutionIds);
          }
        }
      }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(10)]
    public void Build_WithSizeOneAndAnyComplexity_ProducesNoObstacles(int complexity)
    {
      // Act: A single node has no incoming relationship to guard; secrets are disabled
      // so the node is the whole graph.
      var (result, graph) = GraphTestSupport.Build(EnGraphBuilder.Linear, new CnfGraph
      {
        Size = 1,
        Complexity = complexity,
        ProcOpts = GraphTestSupport.NoSecrets,
      });

      // Assert: Build succeeds, the node holds both POIs and the mandatory goal, and no
      // solution or condition appears.
      Assert.True(result.Success);
      var node = Assert.Single(graph.Nodes);
      Assert.Single(node.Pois.Where(poi => poi.Type == EnPoiType.Start));
      Assert.Single(node.Pois.Where(poi => poi.Type == EnPoiType.Exit));
      Assert.Single(node.Pois.Where(poi => poi.Type == EnPoiType.Goal));
      Assert.DoesNotContain(node.Pois, poi => poi.Type == EnPoiType.Solution);
      GraphTestAssert.AssertOpenPassages(graph);
    }

    [Fact]
    public void Build_WithExcessiveComplexity_SaturatesEveryRelationship()
    {
      // Act: Four nodes without extras offer three guardable relationships with room for
      // MaxConditionPerRel conditions each - nine obstacles - yet far more is requested.
      // Secrets are disabled so only builder relationships remain.
      var (result, graph) = GraphTestSupport.Build(EnGraphBuilder.Linear, new CnfGraph
      {
        Size = 4,
        Connectivity = 0,
        Complexity = 10000,
        ProcOpts = GraphTestSupport.NoSecrets,
      });

      // Assert: The pass ends silently once everything is saturated: nine solutions and
      // exactly MaxConditionPerRel conditions on every relationship, never more.
      Assert.True(result.Success);
      Assert.Equal((4 - 1) * MaxConditionPerRel, GraphTestSupport.GetPois(graph, EnPoiType.Solution).Count);
      foreach (var edge in graph.Edges)
      {
        // Assert: The per-relationship condition cap holds exactly at saturation.
        Assert.Equal(MaxConditionPerRel, edge.Conditions.Count);
      }
      GraphTestAssert.AssertBeatable(graph, "Saturated graph must stay beatable.");
    }

    [Theory]
    [InlineData(6, 8, 12)]
    [InlineData(10, 20, 50)]
    public void Build_WithObstacles_GuardsAllPassagesIntoNodeWithSameSolution(int size, int connectivity, int complexity)
    {
      for (var seed = 0; seed < 5; seed++)
      {
        // Act: Build with extras so nodes carry parallel and shortcut in-passages.
        var (_, graph) = GraphTestSupport.Build(EnGraphBuilder.Linear, new CnfGraph
        {
          Size = size,
          Connectivity = connectivity,
          Complexity = complexity,
        }, seed);
        var positions = GraphAssertions.GetPositions(graph);

        // Assert: All relationships into one node carry an identical condition list -
        // the obstacle pass conditions them together, so they never diverge.
        var conditionsByTarget = new Dictionary<int, List<(Guid Poi, EnConditionType Type)>>();
        foreach (var edge in graph.Edges)
        {
          var target = positions[edge.To];
          var actual = edge.Conditions.Select(condition => (condition.RequiresPoi, condition.Type)).ToList();
          if (!conditionsByTarget.TryGetValue(target, out var expected))
            conditionsByTarget[target] = actual;
          else Assert.Equal(expected, actual);
        }
      }
    }

    [Fact]
    public void Build_WithObstacles_KeepsSolutionsWithinMaxConditionDistance()
    {
      // Act: Several graphs with enough complexity to guard every node multiple times.
      for (var seed = 0; seed < 10; seed++)
      {
        var (_, graph) = GraphTestSupport.Build(EnGraphBuilder.Linear, new CnfGraph
        {
          Size = 15,
          Connectivity = 6,
          Complexity = 25,
        }, seed);
        var positions = GraphAssertions.GetPositions(graph);

        // Map every solution POI to the node its passages guard.
        var guardedNodeByPoi = new Dictionary<Guid, int>();
        foreach (var edge in graph.Edges)
        {
          foreach (var condition in edge.Conditions)
          {
            if (!guardedNodeByPoi.TryAdd(condition.RequiresPoi, positions[edge.To]))
            {
              // Assert: One solution guards exactly one node - all in-passages of a node
              // share the same obstacle.
              Assert.Equal(positions[edge.To], guardedNodeByPoi[condition.RequiresPoi]);
            }
          }
        }

        for (var position = 0; position < graph.Nodes.Count; position++)
        {
          foreach (var poi in graph.Nodes[position].Pois.Where(poi => poi.Type == EnPoiType.Solution))
          {
            // Assert: The solution guards some node and sits at or before that node's
            // chain predecessor, never further away than MaxConditionDistance.
            Assert.True(guardedNodeByPoi.ContainsKey(poi.Id), $"Solution {poi.Id} guards nothing (seed {seed}).");
            var distance = guardedNodeByPoi[poi.Id] - 1 - position;
            Assert.InRange(distance, 0, MaxConditionDistance);
          }
        }
      }
    }

    [Fact]
    public void Build_WithObstacles_FavorsSolutionPlacementNearGuardedPassage()
    {
      // Act: Collect every solution's distance to its guarded passage over many seeds.
      var distances = new int[MaxConditionDistance + 1];
      for (var seed = 0; seed < 100; seed++)
      {
        var (_, graph) = GraphTestSupport.Build(EnGraphBuilder.Linear, new CnfGraph
        {
          Size = 30,
          Connectivity = 5,
          Complexity = 15,
        }, seed);
        var positions = GraphAssertions.GetPositions(graph);

        var guardedNodeByPoi = new Dictionary<Guid, int>();
        foreach (var edge in graph.Edges)
        foreach (var condition in edge.Conditions)
          guardedNodeByPoi[condition.RequiresPoi] = positions[edge.To];

        for (var position = 0; position < graph.Nodes.Count; position++)
        {
          foreach (var poi in graph.Nodes[position].Pois.Where(poi => poi.Type == EnPoiType.Solution))
            distances[guardedNodeByPoi[poi.Id] - 1 - position]++;
        }
      }

      // Assert: The 4:3:2:1 weighting makes every distance strictly more likely than the
      // next one behind it.
      Assert.True(distances[0] > distances[1],
        $"Distance 0 ({distances[0]}) must beat distance 1 ({distances[1]}).");
      Assert.True(distances[1] > distances[2],
        $"Distance 1 ({distances[1]}) must beat distance 2 ({distances[2]}).");
      Assert.True(distances[2] > distances[3],
        $"Distance 2 ({distances[2]}) must beat distance 3 ({distances[3]}).");
    }

    [Theory]
    [InlineData(1, 0, 0)]
    [InlineData(2, 1, 3)]
    [InlineData(5, 0, 10)]
    [InlineData(8, 6, 20)]
    [InlineData(15, 10, 60)]
    [InlineData(30, 20, 200)]
    [InlineData(50, 30, 500)]
    public void Build_WithObstacles_ProducesBeatableGraph(int size, int connectivity, int complexity)
    {
      for (var seed = 0; seed < 5; seed++)
      {
        // Act: Build across sizes, connectivity levels, complexity levels and seeds.
        var (result, graph) = GraphTestSupport.Build(EnGraphBuilder.Linear, new CnfGraph
        {
          Size = size,
          Connectivity = connectivity,
          Complexity = complexity,
        }, seed);

        // Assert: The build succeeds and the beatability simulation passes with
        // conditions active - solutions are always reachable before they are demanded.
        Assert.True(result.Success, $"Build failed for size {size}, seed {seed}.");
        GraphTestAssert.AssertValidEdges(graph, allowDuplicates: true);
        GraphTestAssert.AssertBeatable(graph, $"Conditioned graph must stay beatable (size {size}, seed {seed}).");
      }
    }
  }

  /// <summary>Tests for the secret-node pass run first in the finalization.</summary>
  public sealed class SecretTests
  {
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(5)]
    [InlineData(50)]
    public void Build_AlwaysAddsExactlyOneSecretHangingOffChainNode(int size)
    {
      // Act: Build a graph of the given size.
      var (result, graph) = GraphTestSupport.Build(EnGraphBuilder.Linear, new CnfGraph { Size = size });

      // Assert: One secret node exists, reached only over its own secret chokepoint
      // whose source is an ordinary chain node.
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
        var (_, graph) = GraphTestSupport.Build(EnGraphBuilder.Linear, new CnfGraph { Size = 8, Connectivity = 3 }, seed);

        // Assert: The hidden node stays reachable - the secret passage is open - so the
        // beatability simulation still passes.
        GraphTestAssert.AssertBeatable(graph, $"Secret graph must stay connected (seed {seed}).");
      }
    }
  }

  /// <summary>Tests for reproducibility of the generated graph.</summary>
  public sealed class DeterminismTests
  {
    [Fact]
    public void Build_WithSameSeed_ProducesIdenticalGraphs()
    {
      // Arrange: Same config and same seed for two builds.
      var config = new CnfGraph { Size = 12, Connectivity = 6, Complexity = 5 };

      // Act: Build both graphs.
      var first = GraphTestSupport.Build(EnGraphBuilder.Linear, config);
      var second = GraphTestSupport.Build(EnGraphBuilder.Linear, config);

      // Assert: Nodes, edges (including conditions) and POIs match one by one.
      Assert.Equal(first.Result.Success, second.Result.Success);
      GraphTestAssert.AssertEqualGraphs(first.Graph, second.Graph);
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

    [Fact]
    public void Build_WithDifferentComplexity_ProducesDifferentGraphs()
    {
      // Arrange: Reference build without obstacles and a build with the complexity knob
      // turned up.
      var reference = GraphTestSupport.Build(EnGraphBuilder.Linear, new CnfGraph { Size = 8, Connectivity = 4 });
      var varied = GraphTestSupport.Build(EnGraphBuilder.Linear, new CnfGraph
      {
        Size = 8,
        Connectivity = 4,
        Complexity = 5,
      });

      // Assert: The obstacle pass reacts to complexity - the varied graph gains solution
      // POIs and conditions while the reference stays open.
      Assert.Empty(GraphTestSupport.GetPois(reference.Graph, EnPoiType.Solution));
      Assert.Equal(5, GraphTestSupport.GetPois(varied.Graph, EnPoiType.Solution).Count);
      GraphTestAssert.AssertOpenPassages(reference.Graph);
      Assert.NotEqual(reference.Graph, varied.Graph);
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
        var (result, graph) = GraphTestSupport.Build(EnGraphBuilder.Linear, new CnfGraph
        {
          Size = size,
          Connectivity = connectivity,
        }, seed);

        // Assert: The build succeeds and the beatability simulation passes: structure is
        // valid, every passage is open for now, and start reaches exit with all nodes
        // reachable.
        Assert.True(result.Success, $"Build failed for size {size}, seed {seed}.");
        GraphTestAssert.AssertValidEdges(graph, allowDuplicates: true);
        GraphTestAssert.AssertOpenPassages(graph);
        GraphTestAssert.AssertBeatable(graph, $"Graph must stay connected (size {size}, seed {seed}).");
        GraphTestAssert.AssertBeatable(graph);
      }
    }
  }

  //
  // Shared assertions specific to GraphBuilderLinear.
  //

  /// <summary>
  /// Builder-specific structural assertions of <see cref="GraphBuilderLinear"/>: kept out of
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
    /// Asserts that the graph's passages are exactly the consecutive chain pairs, each
    /// present once, and in earlier-to-later orientation.
    /// </summary>
    /// <param name="graph">Graph to check.</param>
    /// <param name="size">Expected number of chain nodes.</param>
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
    /// Asserts that no chain node gained more than <see cref="MaxExtraDegree"/> extra
    /// relationships as source and as target; the skeleton's own single in and single out
    /// relationships are subtracted from the raw counts.
    /// </summary>
    /// <param name="graph">Graph to check.</param>
    public static void AssertExtraDegreeCaps(LilGraph graph)
    {
      var positions = GetPositions(graph);
      var outCounts = new int[graph.Nodes.Count];
      var inCounts = new int[graph.Nodes.Count];
      foreach (var edge in graph.Edges)
      {
        outCounts[positions[edge.From]]++;
        inCounts[positions[edge.To]]++;
      }

      for (var i = 0; i < graph.Nodes.Count; i++)
      {
        // Assert: The skeleton contributes exactly one outgoing relationship to every chain
        // node but the last, and exactly one incoming relationship to every chain node but
        // the first; anything on top is an extra that must stay under the cap in both directions.
        var extraOut = outCounts[i] - (i < graph.Nodes.Count - 1 ? 1 : 0);
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

  /// <summary>Tests for config validation.</summary>
  public sealed class ConfigValidationTests
  {
    [Fact]
    public void Validate_Default_DoesNotThrow()
    {
      // Arrange: The default instance should be valid.
      var config = CnfGraphBuilderLinear.Default;

      // Assert: No exception is thrown.
      config.Validate();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_ZeroOrNegativeMaxEdges_ThrowsArgumentOutOfRange(int maxEdges)
    {
      // Arrange: MaxEdges must be at least 1.
      var config = CnfGraphBuilderLinear.Default with { MaxEdges = maxEdges };

      // Assert: Validation rejects the value.
      var ex = Assert.Throws<ArgumentOutOfRangeException>(() => config.Validate());
      Assert.Equal(nameof(CnfGraphBuilderLinear.MaxEdges), ex.ParamName);
    }

    [Theory]
    [InlineData(-1)]
    public void Validate_NegativeMaxExtraDegree_ThrowsArgumentOutOfRange(int maxExtraDegree)
    {
      // Arrange: A negative extra-degree cap is invalid.
      var config = CnfGraphBuilderLinear.Default with { MaxExtraDegree = maxExtraDegree };

      // Assert: Validation rejects the value.
      var ex = Assert.Throws<ArgumentOutOfRangeException>(() => config.Validate());
      Assert.Equal(nameof(CnfGraphBuilderLinear.MaxExtraDegree), ex.ParamName);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_ZeroOrNegativeMaxConditionPerRel_ThrowsArgumentOutOfRange(int maxConditionPerRel)
    {
      // Arrange: MaxConditionPerRel must be at least 1.
      var config = CnfGraphBuilderLinear.Default with { MaxConditionPerRel = maxConditionPerRel };

      // Assert: Validation rejects the value.
      var ex = Assert.Throws<ArgumentOutOfRangeException>(() => config.Validate());
      Assert.Equal(nameof(CnfGraphBuilderLinear.MaxConditionPerRel), ex.ParamName);
    }

    [Theory]
    [InlineData(-1)]
    public void Validate_NegativeMaxConditionDistance_ThrowsArgumentOutOfRange(int maxConditionDistance)
    {
      // Arrange: A negative condition distance is invalid.
      var config = CnfGraphBuilderLinear.Default with { MaxConditionDistance = maxConditionDistance };

      // Assert: Validation rejects the value.
      var ex = Assert.Throws<ArgumentOutOfRangeException>(() => config.Validate());
      Assert.Equal(nameof(CnfGraphBuilderLinear.MaxConditionDistance), ex.ParamName);
    }

    [Theory]
    [InlineData(-1)]
    public void Validate_NegativeMaxSkipDistance_ThrowsArgumentOutOfRange(int maxSkipDistance)
    {
      // Arrange: A negative skip distance is invalid.
      var config = CnfGraphBuilderLinear.Default with { MaxSkipDistance = maxSkipDistance };

      // Assert: Validation rejects the value.
      var ex = Assert.Throws<ArgumentOutOfRangeException>(() => config.Validate());
      Assert.Equal(nameof(CnfGraphBuilderLinear.MaxSkipDistance), ex.ParamName);
    }
  }
}
