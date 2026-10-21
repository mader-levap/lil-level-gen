using LostInLimbo.LevelGen.Data.Config.Graph;
using LostInLimbo.LevelGen.Data.Result.Graph;
using LostInLimbo.LevelGen.Internal.Generator.Graph.Processors;
using static LostInLimbo.LevelGen.Tests.Generator.Graph.GraphTestCreate;
using static LostInLimbo.LevelGen.Tests.Generator.Graph.GraphTestSupport;

namespace LostInLimbo.LevelGen.Tests.Generator.Graph.Processors;

/// <summary>
/// Tests <see cref="ProcRelSecrets"/>: option validation, secret-node shape, host picking,
/// secret-in-secret nesting, determinism and the hand-off to the later finalisation passes.
/// </summary>
/// <remarks>
/// The pass works on a finished <see cref="LilGraph"/>, so most tests construct graphs
/// directly instead of going through a builder - that keeps the host pool exactly
/// controlled per test. Nesting expectations with intermediate chances are statistical:
/// they average over many seeds, because the exact outcome depends on the random stream.
/// </remarks>
public sealed class ProcRelSecretsTests
{
  /// <summary>Graph sub-seed used by all tests unless a test varies the seed itself.</summary>
  private const long Seed = GraphTestSupport.DefaultSeedGraph;

  /// <summary>Amount of seeds the statistical tests average over.</summary>
  private const int StatisticalSeedCount = 20;

  /// <summary>Tests for the hard constraints: nulls and out-of-range values.</summary>
  public sealed class ValidationTests
  {
    [Fact]
    public void Constructor_WithNullGraph_ThrowsArgumentNullException()
    {
      // Act & Assert: A missing graph is rejected.
      Assert.Throws<ArgumentNullException>(() => new ProcRelSecrets(null!, CnfProcRelSecretsOpts.Default, Seed));
    }

    [Fact]
    public void Constructor_WithNullOptions_ThrowsArgumentNullException()
    {
      // Act & Assert: Missing options are rejected.
      Assert.Throws<ArgumentNullException>(() => new ProcRelSecrets(CreateStartExitChainGraph(4), null!, Seed));
    }

    [Fact]
    public void Constructor_WithInvalidOptions_ThrowsBeforeExecute()
    {
      // Arrange: Options rejected by validation; the rules themselves are pinned by
      // CnfProcOptsTests.
      var options = CnfProcRelSecretsOpts.Default with { Amount = -1 };

      // Act & Assert: Construction alone rejects the options, before Execute can run.
      Assert.Throws<ArgumentOutOfRangeException>(
        () => new ProcRelSecrets(CreateStartExitChainGraph(4), options, Seed));
    }
  }

  /// <summary>Tests for the appended shape: counts, secret flags, edge properties.</summary>
  public sealed class BehaviorTests
  {
    [Fact]
    public void Execute_WithAmountZero_LeavesGraphUntouched()
    {
      // Arrange: A chain graph and options that disable the pass.
      var graph = CreateStartExitChainGraph(4);
      var options = CnfProcRelSecretsOpts.Default with { Amount = 0 };

      // Act: Run the pass.
      new ProcRelSecrets(graph, options, Seed).Execute();

      // Assert: Nothing was appended and nothing became secret.
      Assert.Equal(4, graph.Nodes.Count);
      Assert.Equal(3, graph.Edges.Count);
      Assert.DoesNotContain(graph.Nodes, node => node.Secret);
      Assert.DoesNotContain(graph.Edges, edge => edge.Secret);
    }

    [Fact]
    public void Execute_OnEmptyGraph_DoesNothing()
    {
      // Arrange: Empty graph; there is nothing to attach a secret to.
      var graph = new LilGraph();

      // Act: Run the pass with the default amount.
      new ProcRelSecrets(graph, CnfProcRelSecretsOpts.Default, Seed).Execute();

      // Assert: The graph stays empty instead of failing.
      Assert.Empty(graph.Nodes);
      Assert.Empty(graph.Edges);
    }

    [Fact]
    public void Execute_WithAmountOne_AddsOneSecretNodeWithSecretEdge()
    {
      // Arrange: A chain graph; remember the original identifiers.
      var graph = CreateStartExitChainGraph(4);
      var originalNodeIds = graph.Nodes.Select(node => node.Id).ToHashSet();
      var originalEdgeIds = graph.Edges.Select(edge => edge.Id).ToHashSet();

      // Act: Add a single secret.
      new ProcRelSecrets(graph, CnfProcRelSecretsOpts.Default, Seed).Execute();

      // Assert: One secret node was appended - no POIs of its own, not colliding with
      // the original identifiers.
      Assert.Equal(5, graph.Nodes.Count);
      var secret = Assert.Single(graph.Nodes.Where(node => node.Secret));
      Assert.Empty(secret.Pois);
      Assert.DoesNotContain(secret.Id, originalNodeIds);

      // Assert: Exactly one secret relationship reaches it - an open bidirectional
      // chokepoint from an ordinary node, with a fresh identifier.
      Assert.Equal(4, graph.Edges.Count);
      var edge = Assert.Single(graph.Edges.Where(edge => edge.Secret));
      Assert.True(edge.Secret);
      Assert.Equal(EnPassageDirection.Bidirectional, edge.Direction);
      Assert.True(edge.Chokepoint);
      Assert.Empty(edge.Conditions);
      Assert.Equal(secret.Id, edge.To);
      Assert.Contains(edge.From, originalNodeIds);
      Assert.DoesNotContain(edge.Id, originalEdgeIds);

      // Assert: The original nodes and edges are still exactly the original ones.
      Assert.Equal(4, graph.Nodes.Count(node => !node.Secret));
      Assert.Equal(3, graph.Edges.Count(edge => !edge.Secret));
    }

    [Fact]
    public void Execute_WithAmountThree_WithoutNesting_AttachesEverySecretToOrdinaryNode()
    {
      // Arrange: A chain graph and options that forbid nesting.
      var graph = CreateStartExitChainGraph(4);
      var originalNodeIds = graph.Nodes.Select(node => node.Id).ToHashSet();
      var options = CnfProcRelSecretsOpts.Default with { Amount = 3, ChanceSecretInSecret = 0f };

      // Act: Add three secrets.
      new ProcRelSecrets(graph, options, Seed).Execute();

      // Assert: Three secret nodes and three secret relationships exist, all identifiers
      // unique.
      Assert.Equal(7, graph.Nodes.Count);
      Assert.Equal(6, graph.Edges.Count);
      var secrets = graph.Nodes.Where(node => node.Secret).ToList();
      Assert.Equal(3, secrets.Count);
      Assert.Equal(3, secrets.Select(secret => secret.Id).Distinct().Count());
      var secretEdges = graph.Edges.Where(edge => edge.Secret).ToList();
      Assert.Equal(3, secretEdges.Count);
      Assert.Equal(3, secretEdges.Select(edge => edge.Id).Distinct().Count());

      // Assert: Every secret hangs off an ordinary node, and every secret node is the
      // target of exactly one secret relationship - a star of independent leaves.
      foreach (var edge in secretEdges)
      {
        Assert.Contains(edge.From, originalNodeIds);
      }
      foreach (var secret in secrets)
      {
        Assert.Single(secretEdges.Where(edge => edge.To == secret.Id));
      }
    }

    [Fact]
    public void Execute_OverManySeeds_KeepsSecretGraphsBeatable()
    {
      for (var seed = 0; seed < StatisticalSeedCount; seed++)
      {
        // Arrange: A chain graph with two secrets.
        var graph = CreateStartExitChainGraph(5);
        var options = CnfProcRelSecretsOpts.Default with { Amount = 2 };

        // Act: Add the secrets.
        new ProcRelSecrets(graph, options, seed).Execute();

        // Assert: Every secret stays reachable over its open passage, so the
        // beatability simulation passes.
        GraphTestAssert.AssertBeatable(graph, $"Secret graph must stay connected (seed {seed}).");
      }
    }
  }

  /// <summary>Tests for the secret-in-secret roll: never, always and in between.</summary>
  public sealed class NestingTests
  {
    [Fact]
    public void Execute_WithChanceZero_NeverNestsSecrets()
    {
      for (var seed = 0; seed < StatisticalSeedCount; seed++)
      {
        // Arrange: A chain graph and options that forbid nesting.
        var graph = CreateStartExitChainGraph(4);
        var originalNodeIds = graph.Nodes.Select(node => node.Id).ToHashSet();
        var options = CnfProcRelSecretsOpts.Default with { Amount = 5, ChanceSecretInSecret = 0f };

        // Act: Add five secrets.
        new ProcRelSecrets(graph, options, seed).Execute();

        // Assert: Every secret hangs off an ordinary node - no secret-in-secret.
        foreach (var edge in graph.Edges.Where(edge => edge.Secret))
        {
          Assert.Contains(edge.From, originalNodeIds);
        }
      }
    }

    [Fact]
    public void Execute_WithChanceOne_NestsEveryLaterSecretBehindASecret()
    {
      // Arrange: A chain graph and options that force nesting.
      var graph = CreateStartExitChainGraph(4);
      var options = CnfProcRelSecretsOpts.Default with { Amount = 3, ChanceSecretInSecret = 1f };

      // Act: Add three secrets; the processor appends one edge per secret, in order.
      new ProcRelSecrets(graph, options, Seed).Execute();

      // Assert: The first secret hangs off an ordinary node - it has nothing to nest
      // behind - and both later secrets hang off an earlier secret, so the secrets nest
      // behind secrets.
      var secretEdges = graph.Edges.Where(edge => edge.Secret).ToList();
      Assert.Equal(3, secretEdges.Count);
      Assert.DoesNotContain(secretEdges[0].From, graph.Nodes.Where(node => node.Secret).Select(node => node.Id));
      Assert.Contains(secretEdges[1].From, graph.Nodes.Where(node => node.Secret && node.Id != secretEdges[1].To).Select(node => node.Id));
      Assert.Contains(secretEdges[2].From, graph.Nodes.Where(node => node.Secret && node.Id != secretEdges[2].To).Select(node => node.Id));
    }

    [Fact]
    public void Execute_WithChanceOneOverManySeeds_AlwaysNestsAfterTheFirstSecret()
    {
      for (var seed = 0; seed < StatisticalSeedCount; seed++)
      {
        // Arrange: A chain graph and options that force nesting.
        var graph = CreateStartExitChainGraph(4);
        var originalNodeIds = graph.Nodes.Select(node => node.Id).ToHashSet();
        var options = CnfProcRelSecretsOpts.Default with { Amount = 4, ChanceSecretInSecret = 1f };

        // Act: Add four secrets.
        new ProcRelSecrets(graph, options, seed).Execute();

        // Assert: Exactly one secret relationship starts at an ordinary node - the
        // first one - and the other three start at a secret node.
        var secretEdges = graph.Edges.Where(edge => edge.Secret).ToList();
        Assert.Equal(4, secretEdges.Count);
        Assert.Single(secretEdges.Where(edge => originalNodeIds.Contains(edge.From)));
        Assert.Equal(3, secretEdges.Count(edge => !originalNodeIds.Contains(edge.From)));
      }
    }

    [Fact]
    public void Execute_WithIntermediateChance_ProducesMixOfFlatAndNestedSecrets()
    {
      // Arrange: With the default ten percent nesting chance, five secrets over many
      // seeds almost surely produce at least one fully flat and at least one nested run.
      var flatRuns = 0;
      var nestedRuns = 0;
      for (var seed = 0; seed < 100; seed++)
      {
        // Arrange: A chain graph and default-chance options.
        var graph = CreateStartExitChainGraph(4);
        var originalNodeIds = graph.Nodes.Select(node => node.Id).ToHashSet();
        var options = CnfProcRelSecretsOpts.Default with { Amount = 5 };

        // Act: Add five secrets.
        new ProcRelSecrets(graph, options, seed).Execute();

        // Classify: A run is nested once any secret hangs off another secret.
        if (graph.Edges.Where(edge => edge.Secret).All(edge => originalNodeIds.Contains(edge.From)))
          flatRuns++;
        else
          nestedRuns++;
      }

      // Assert: Both outcomes occur in practice - the chance is neither dead nor certain.
      Assert.True(flatRuns > 0, "The nesting chance never produced a flat set of secrets.");
      Assert.True(nestedRuns > 0, "The nesting chance never produced a nested secret.");
    }
  }

  /// <summary>Tests for the MaxEdges hard cap: host filtering, graceful stop, nesting under pressure.</summary>
  public sealed class MaxEdgesTests
  {
    [Fact]
    public void Execute_WithHostAtCapacity_SkipsThatHost()
    {
      // Arrange: Chain of two; the first node is capped at its single skeleton
      // relationship, the second one has no limit - only the second can host.
      var graph = CreateCappedChainGraph(
        [
          (maxEdges: 1, start: true, exit: false),
          (maxEdges: -1, start: false, exit: true),
        ]);

      // Act: Add a single secret.
      new ProcRelSecrets(graph, CnfProcRelSecretsOpts.Default, Seed).Execute();

      // Assert: The full node keeps its single relationship; the secret hangs off the
      // node that still has capacity.
      var edge = Assert.Single(graph.Edges.Where(edge => edge.Secret));
      Assert.Equal(Id(2), edge.From);
      Assert.Equal(1, GraphTestSupport.GetDegrees(graph)[Id(1)]);
    }

    [Fact]
    public void Execute_WithAllHostsAtCapacity_AddsNoSecrets()
    {
      // Arrange: Both nodes sit exactly at their cap of one relationship.
      var graph = CreateCappedChainGraph(
        [
          (maxEdges: 1, start: true, exit: false),
          (maxEdges: 1, start: false, exit: true),
        ]);
      var originalNodeCount = graph.Nodes.Count;
      var originalEdgeCount = graph.Edges.Count;

      // Act: Ask for three secrets anyway.
      new ProcRelSecrets(graph, CnfProcRelSecretsOpts.Default with { Amount = 3 }, Seed).Execute();

      // Assert: The pass stops gracefully instead of exceeding any cap or failing.
      Assert.Equal(originalNodeCount, graph.Nodes.Count);
      Assert.Equal(originalEdgeCount, graph.Edges.Count);
      Assert.DoesNotContain(graph.Nodes, node => node.Secret);
      GraphTestAssert.AssertMaxEdges(graph);
    }

    [Fact]
    public void Execute_WithNegativeMaxEdges_TreatsHostAsUnlimited()
    {
      // Arrange: Only the first node has capacity - unlimited by negative cap; the other
      // two sit at their positive caps (node 2 is full on skeleton, node 3 on its single
      // skeleton relationship).
      var graph = CreateCappedChainGraph(
        [
          (maxEdges: -1, start: true, exit: false),
          (maxEdges: 2, start: false, exit: false),
          (maxEdges: 1, start: false, exit: true),
        ]);
      var options = CnfProcRelSecretsOpts.Default with { Amount = 2, ChanceSecretInSecret = 0f };

      // Act: Add two secrets; the nesting chance is off, so both roll ordinary hosts.
      new ProcRelSecrets(graph, options, Seed).Execute();

      // Assert: Both secrets hang off the unlimited node - negative cap means no limit.
      var secretEdges = graph.Edges.Where(edge => edge.Secret).ToList();
      Assert.Equal(2, secretEdges.Count);
      Assert.All(secretEdges, edge => Assert.Equal(Id(1), edge.From));
      GraphTestAssert.AssertMaxEdges(graph);
    }

    [Fact]
    public void Execute_WithForcedNesting_KeepsNestingWhileOrdinaryHostsAreFull()
    {
      // Arrange: Two chain nodes with one free slot each; after the first secret one
      // ordinary host may already be full, so nesting carries the remaining secrets.
      var graph = CreateCappedChainGraph(
        [
          (maxEdges: 2, start: true, exit: false),
          (maxEdges: 2, start: false, exit: true),
        ]);
      var originalNodeIds = graph.Nodes.Select(node => node.Id).ToHashSet();
      var options = CnfProcRelSecretsOpts.Default with { Amount = 4, ChanceSecretInSecret = 1f };

      // Act: Add four secrets with nesting forced.
      new ProcRelSecrets(graph, options, Seed).Execute();

      // Assert: Exactly one secret relationship starts at an ordinary node - the first
      // one - and the other three nest behind secrets despite the tight host caps.
      var secretEdges = graph.Edges.Where(edge => edge.Secret).ToList();
      Assert.Equal(4, secretEdges.Count);
      Assert.Single(secretEdges.Where(edge => originalNodeIds.Contains(edge.From)));
      GraphTestAssert.AssertMaxEdges(graph);
      GraphTestAssert.AssertBeatable(graph, "Nested capped graph must stay connected.");
    }

    [Fact]
    public void Execute_OverManySeeds_NeverExceedsMaxEdges()
    {
      for (var seed = 0; seed < StatisticalSeedCount; seed++)
      {
        // Arrange: Chain with tight caps; ends hold one relationship already, middles
        // two - only the ends can ever host another relationship.
        var graph = CreateCappedChainGraph(
        [
          (maxEdges: 2, start: true, exit: false),
          (maxEdges: 2, start: false, exit: false),
          (maxEdges: 2, start: false, exit: false),
          (maxEdges: 2, start: false, exit: true),
        ]);
        var options = CnfProcRelSecretsOpts.Default with { Amount = 6 };

        // Act: Add six secrets - more than the two end slots can host.
        new ProcRelSecrets(graph, options, seed).Execute();

        // Assert: No node ever exceeds its hard cap and the graph stays beatable; the
        // pass simply places fewer secrets than requested once capacity runs out.
        GraphTestAssert.AssertMaxEdges(graph);
        GraphTestAssert.AssertBeatable(graph, $"Capped graph must stay connected (seed {seed}).");
      }
    }

    [Fact]
    public void Execute_WithSameSeedAndCaps_ProducesIdenticalGraphs()
    {
      // Arrange: Two identical capped chain graphs and the same inputs.
      var first = CreateCappedChainGraph(
      [
        (maxEdges: 2, start: true, exit: false),
        (maxEdges: 2, start: false, exit: false),
        (maxEdges: 2, start: false, exit: true),
      ]);
      var second = CreateCappedChainGraph(
      [
        (maxEdges: 2, start: true, exit: false),
        (maxEdges: 2, start: false, exit: false),
        (maxEdges: 2, start: false, exit: true),
      ]);
      var options = CnfProcRelSecretsOpts.Default with { Amount = 5, ChanceSecretInSecret = 0.5f };

      // Act: Run the pass on both graphs.
      new ProcRelSecrets(first, options, Seed).Execute();
      new ProcRelSecrets(second, options, Seed).Execute();

      // Assert: Capacity filtering stays part of the deterministic output.
      GraphTestAssert.AssertEqualGraphs(first, second);
    }
  }

  /// <summary>Tests for reproducibility of the additions.</summary>
  public sealed class DeterminismTests
  {
    [Fact]
    public void Execute_WithSameSeed_ProducesIdenticalGraphs()
    {
      // Arrange: Two identical chain graphs and the same inputs.
      var first = CreateStartExitChainGraph(4);
      var second = CreateStartExitChainGraph(4);
      var options = CnfProcRelSecretsOpts.Default with { Amount = 3, ChanceSecretInSecret = 0.5f };

      // Act: Run the pass on both graphs.
      new ProcRelSecrets(first, options, Seed).Execute();
      new ProcRelSecrets(second, options, Seed).Execute();

      // Assert: Nodes and edges match one by one - identifiers and host picks included.
      GraphTestAssert.AssertEqualGraphs(first, second);
    }

    [Fact]
    public void Execute_WithDifferentSeeds_ProducesDifferentHostPicks()
    {
      // Arrange: Collect the secret host sequences of many seeds.
      var sequences = new HashSet<string>();
      for (var seed = 0; seed < StatisticalSeedCount; seed++)
      {
        // Arrange: A chain graph with several secrets and a non-degenerate chance.
        var graph = CreateStartExitChainGraph(4);
        var options = CnfProcRelSecretsOpts.Default with { Amount = 3, ChanceSecretInSecret = 0.5f };

        // Act: Add the secrets and read back the host of every secret relationship.
        new ProcRelSecrets(graph, options, seed).Execute();
        var sequence = string.Join(",", graph.Edges.Where(edge => edge.Secret).Select(edge => edge.From));
        sequences.Add(sequence);
      }

      // Assert: The seed drives the host picks - more than one distinct sequence exists.
      Assert.True(sequences.Count > 1, $"All {StatisticalSeedCount} seeds produced the same secret hosts.");
    }
  }

  /// <summary>Tests for the hand-off to the passes that run after this one.</summary>
  public sealed class FinalisationTests
  {
    [Fact]
    public void Execute_BeforeRelSizeAndStyle_AllSecretsStillGetSizeAndGroup()
    {
      // Arrange: A chain graph; the pass runs first, exactly like in the builders.
      var graph = CreateStartExitChainGraph(4);
      var options = CnfProcRelSecretsOpts.Default with { Amount = 2, ChanceSecretInSecret = 0.5f };

      // Act: Run the finalisation passes in builder order.
      new ProcRelSecrets(graph, options, Seed).Execute();
      new ProcNodeRelSize(graph, CnfProcNodeRelSizeOpts.Default, Seed).Execute();
      new ProcNodeStyle(graph, CnfProcNodeStyleOpts.Default, Seed).Execute();

      // Assert: The secret nodes took part in both later passes - a configured relative
      // size and an assigned group each.
      var stepValues = CnfProcNodeRelSizeOpts.Default.Prepare();
      foreach (var secret in graph.Nodes.Where(node => node.Secret))
      {
        Assert.Contains(secret.RelSize, stepValues);
        Assert.NotNull(secret.Style.Group);
      }
      GraphTestAssert.AssertBeatable(graph, "Finalised graph must stay connected.");
    }
  }

  //
  // Graph fixtures
  //

  /// <summary>
  /// Builds a chain graph whose nodes carry explicit <see cref="LilNode.MaxEdges"/> caps,
  /// one tuple per node in chain order (node 1 first).
  /// </summary>
  /// <param name="nodes">Per-node cap and POI flags in chain order; at least one node.</param>
  /// <returns>The constructed graph with one open edge per consecutive node pair.</returns>
  private static LilGraph CreateCappedChainGraph(
    IReadOnlyList<(int maxEdges, bool start, bool exit)> nodes)
  {
    var graph = new LilGraph();
    for (var i = 0; i < nodes.Count; i++)
    {
      var (maxEdges, start, exit) = nodes[i];
      var pois = new List<LilPoi>();
      if (start) pois.Add(Poi(1000 + i + 1, EnPoiType.Start));
      if (exit) pois.Add(Poi(2000 + i + 1, EnPoiType.Exit));
      graph.Nodes.Add(Node(i + 1, maxEdges, [.. pois]));
    }

    for (var i = 0; i + 1 < nodes.Count; i++)
    {
      graph.Edges.Add(Edge(i + 1, i + 1, i + 2));
    }

    return graph;
  }
}
