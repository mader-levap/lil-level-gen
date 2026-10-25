using LostInLimbo.LevelGen.Data.Config.Graph.Proc;
using LostInLimbo.LevelGen.Data.Result.Graph;
using LostInLimbo.LevelGen.Internal.Generator.Graph.Processors;
using static LostInLimbo.LevelGen.Tests.Generator.Graph.GraphTestCreate;

namespace LostInLimbo.LevelGen.Tests.Generator.Graph.Processors;

/// <summary>
/// Tests <see cref="ProcRelSecrets"/>: option validation, secret-node shape, host picking,
/// secret-in-secret nesting, the two-passage layout, determinism and the hand-off to the
/// later finalization passes.
/// </summary>
/// <remarks>
/// The pass works on a finished <see cref="LilGraph"/>, so most tests construct graphs
/// directly instead of going through a builder - that keeps the host pool exactly
/// controlled per test. Nesting expectations with intermediate chances are statistical:
/// they average over many seeds, because the exact outcome depends on the random stream.
/// The two-passage tests force the layout rolls to 0 or 1 wherever the structure alone is
/// under test, so only the weight-distribution and conditional-chance tests stay
/// statistical.
/// </remarks>
public sealed class ProcRelSecretsTests
{
  /// <summary>Graph sub-seed used by all tests unless a test varies the seed itself.</summary>
  private const long Seed = GraphTestSupport.DefaultSeedGraph;

  /// <summary>Number of seeds the statistical tests average over.</summary>
  private const int StatisticalSeedCount = 20;

  /// <summary>Tests for the hard constraints: nulls and out-of-range values.</summary>
  public sealed class ValidationTests
  {
    [Fact]
    public void Constructor_WithNullGraph_ThrowsArgumentNullException()
    {
      // Act & Assert: A missing graph is rejected.
      Assert.Throws<ArgumentNullException>(() => new ProcRelSecrets(null!, CnfProcRelSecretsOpt.Default, Seed));
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
      var options = CnfProcRelSecretsOpt.Default with { Amount = -1 };

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
      var options = CnfProcRelSecretsOpt.Default with { Amount = 0 };

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
      new ProcRelSecrets(graph, CnfProcRelSecretsOpt.Default, Seed).Execute();

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
      new ProcRelSecrets(graph, CnfProcRelSecretsOpt.Default, Seed).Execute();

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
      var options = CnfProcRelSecretsOpt.Default with { Amount = 3, ChanceSecretInSecret = 0f };

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
        var options = CnfProcRelSecretsOpt.Default with { Amount = 2 };

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
        var options = CnfProcRelSecretsOpt.Default with { Amount = 5, ChanceSecretInSecret = 0f };

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
      var options = CnfProcRelSecretsOpt.Default with { Amount = 3, ChanceSecretInSecret = 1f };

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
        var options = CnfProcRelSecretsOpt.Default with { Amount = 4, ChanceSecretInSecret = 1f };

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
        var options = CnfProcRelSecretsOpt.Default with { Amount = 5 };

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
    public void Execute_WithConfiguredMaxEdges_AssignsCapToCreatedSecrets()
    {
      // Arrange: A chain graph whose ordinary nodes stay unlimited; the configured cap
      // differs from the option default, so only the configured value can satisfy it.
      var graph = CreateStartExitChainGraph(4);
      var options = CnfProcRelSecretsOpt.Default with
      {
        Amount = 3,
        ChanceSecretInSecret = 0.5f,
        MaxEdges = 5,
      };

      // Act: Add three secrets; the nesting chance may make a secret host a later one,
      // so the configured cap is consulted for secret hosts as well.
      new ProcRelSecrets(graph, options, Seed).Execute();

      // Assert: Every created secret carries the configured cap, while the nodes that
      // already existed keep their own - the pass only stamps what it appends.
      var secrets = graph.Nodes.Where(node => node.Secret).ToList();
      Assert.Equal(3, secrets.Count);
      Assert.All(secrets, secret => Assert.Equal(5, secret.MaxEdges));
      Assert.All(graph.Nodes.Where(node => !node.Secret), node => Assert.Equal(DefaultNodeMaxEdges, node.MaxEdges));
      GraphTestAssert.AssertMaxEdges(graph);
    }

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
      new ProcRelSecrets(graph, CnfProcRelSecretsOpt.Default, Seed).Execute();

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
      new ProcRelSecrets(graph, CnfProcRelSecretsOpt.Default with { Amount = 3 }, Seed).Execute();

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
      var options = CnfProcRelSecretsOpt.Default with { Amount = 2, ChanceSecretInSecret = 0f };

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
      var options = CnfProcRelSecretsOpt.Default with { Amount = 4, ChanceSecretInSecret = 1f };

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
        var options = CnfProcRelSecretsOpt.Default with { Amount = 6 };

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
      var options = CnfProcRelSecretsOpt.Default with { Amount = 5, ChanceSecretInSecret = 0.5f };

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
      var options = CnfProcRelSecretsOpt.Default with { Amount = 3, ChanceSecretInSecret = 0.5f };

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
        var options = CnfProcRelSecretsOpt.Default with { Amount = 3, ChanceSecretInSecret = 0.5f };

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
  public sealed class FinalizationTests
  {
    [Fact]
    public void Execute_BeforeRelSizeAndStyle_AllSecretsStillGetSizeAndGroup()
    {
      // Arrange: A chain graph; the pass runs first, exactly like in the builders.
      var graph = CreateStartExitChainGraph(4);
      var options = CnfProcRelSecretsOpt.Default with { Amount = 2, ChanceSecretInSecret = 0.5f };

      // Act: Run the finalization passes in builder order.
      new ProcRelSecrets(graph, options, Seed).Execute();
      new ProcNodeRelSize(graph, CnfProcNodeRelSizeOpt.Default, Seed).Execute();
      new ProcNodeStyle(graph, CnfProcNodeStyleOpt.Default, Seed).Execute();

      // Assert: The secret nodes took part in both later passes - a configured relative
      // size and an assigned group each.
      var stepValues = CnfProcNodeRelSizeOpt.Default.Prepare();
      foreach (var secret in graph.Nodes.Where(node => node.Secret))
      {
        Assert.Contains(secret.RelSize, stepValues);
        Assert.NotNull(secret.Style.Group);
      }
      GraphTestAssert.AssertBeatable(graph, "Finalised graph must stay connected.");
    }
  }

  /// <summary>Tests for the rare two-passage layout: return detours, nearby exits, fallback.</summary>
  public sealed class TwoPassageTests
  {
    [Fact]
    public void Execute_WithChanceZero_NeverCreatesASecondPassage()
    {
      // Arrange: A chain graph with the two-passage feature explicitly off.
      var graph = CreateStartExitChainGraph(5);
      var options = CnfProcRelSecretsOpt.Default with
      {
        Amount = 4,
        ChanceSecretInSecret = 0f,
        ChanceTwoPassages = 0f,
      };

      // Act: Add four secrets.
      new ProcRelSecrets(graph, options, Seed).Execute();

      // Assert: Every secret is touched by exactly one secret relationship - no exit
      // passage ever appears.
      var secrets = graph.Nodes.Where(node => node.Secret).ToList();
      Assert.Equal(4, secrets.Count);
      var secretEdges = graph.Edges.Where(edge => edge.Secret).ToList();
      Assert.Equal(4, secretEdges.Count);
      foreach (var secret in secrets)
      {
        Assert.Single(secretEdges.Where(edge => edge.From == secret.Id || edge.To == secret.Id));
      }
    }

    [Fact]
    public void Execute_WithChanceOneAndReturnOne_ProducesOneWayReturnPassages()
    {
      // Arrange: An uncapped chain; every secret is forced into the A -> S -> A layout.
      var graph = CreateStartExitChainGraph(6);
      var originalNodeIds = graph.Nodes.Select(node => node.Id).ToHashSet();
      var options = CnfProcRelSecretsOpt.Default with
      {
        Amount = 3,
        ChanceSecretInSecret = 0f,
        ChanceTwoPassages = 1f,
        ChanceReturnToHost = 1f,
      };

      // Act: Add three secrets.
      new ProcRelSecrets(graph, options, Seed).Execute();

      // Assert: Two secret passages per secret, all identifiers unique - the entry and
      // exit edges derive from separate salts.
      var secrets = graph.Nodes.Where(node => node.Secret).ToList();
      Assert.Equal(3, secrets.Count);
      var secretEdges = graph.Edges.Where(edge => edge.Secret).ToList();
      Assert.Equal(6, secretEdges.Count);
      Assert.Equal(6, secretEdges.Select(edge => edge.Id).Distinct().Count());

      // Assert: Entry A -> S and exit S -> A, both one way, both open secret chokepoints
      // hanging off the same ordinary host.
      foreach (var secret in secrets)
      {
        var entry = Assert.Single(secretEdges.Where(edge => edge.To == secret.Id));
        var exit = Assert.Single(secretEdges.Where(edge => edge.From == secret.Id));
        Assert.Equal(EnPassageDirection.OneWay, entry.Direction);
        Assert.Equal(EnPassageDirection.OneWay, exit.Direction);
        Assert.Equal(entry.From, exit.To);
        Assert.Contains(entry.From, originalNodeIds);
        Assert.True(entry.Secret);
        Assert.True(exit.Secret);
        Assert.True(entry.Chokepoint);
        Assert.True(exit.Chokepoint);
        Assert.Empty(entry.Conditions);
        Assert.Empty(exit.Conditions);
      }

      // Assert: Two passages per host stay within every cap and the level stays winnable.
      GraphTestAssert.AssertMaxEdges(graph);
      GraphTestAssert.AssertBeatable(graph, "Return-passage graph must stay beatable.");
    }

    [Fact]
    public void Execute_WithChanceOneAndReturnZero_ExitsToNearbyOrdinaryNode()
    {
      // Arrange: An uncapped chain; every secret must exit to a different nearby node.
      var graph = CreateStartExitChainGraph(8);
      var originalNodeIds = graph.Nodes.Select(node => node.Id).ToHashSet();
      var options = CnfProcRelSecretsOpt.Default with
      {
        Amount = 5,
        ChanceSecretInSecret = 0f,
        ChanceTwoPassages = 1f,
        ChanceReturnToHost = 0f,
      };

      // Act: Add five secrets.
      new ProcRelSecrets(graph, options, Seed).Execute();

      // Assert: Every secret got both passages: an open entry from a host and an open
      // exit to a different ordinary node within three hops of that host.
      var secrets = graph.Nodes.Where(node => node.Secret).ToList();
      Assert.Equal(5, secrets.Count);
      var secretEdges = graph.Edges.Where(edge => edge.Secret).ToList();
      Assert.Equal(10, secretEdges.Count);
      foreach (var secret in secrets)
      {
        var entry = Assert.Single(secretEdges.Where(edge => edge.To == secret.Id));
        var exit = Assert.Single(secretEdges.Where(edge => edge.From == secret.Id));
        Assert.Equal(EnPassageDirection.Bidirectional, entry.Direction);
        Assert.Equal(EnPassageDirection.Bidirectional, exit.Direction);
        Assert.NotEqual(entry.From, exit.To);
        Assert.Contains(exit.To, originalNodeIds);
        Assert.InRange(UndirectedDistance(graph, entry.From, exit.To), 1, 3);
      }

      // Assert: The added passages stay within every cap and the level stays winnable.
      GraphTestAssert.AssertMaxEdges(graph);
      GraphTestAssert.AssertBeatable(graph, "Exit-passage graph must stay beatable.");
    }

    [Fact]
    public void Execute_WithMaxExitDistanceOne_ExitsOnlyToAdjacentNodes()
    {
      // Arrange: The exit bound shrinks to a single hop.
      var graph = CreateStartExitChainGraph(8);
      var options = CnfProcRelSecretsOpt.Default with
      {
        Amount = 5,
        ChanceSecretInSecret = 0f,
        ChanceTwoPassages = 1f,
        ChanceReturnToHost = 0f,
        MaxExitDistance = 1,
      };

      // Act: Add five secrets.
      new ProcRelSecrets(graph, options, Seed).Execute();

      // Assert: Every exit lands on a direct neighbor of its host.
      var secretEdges = graph.Edges.Where(edge => edge.Secret).ToList();
      Assert.Equal(10, secretEdges.Count);
      foreach (var secret in graph.Nodes.Where(node => node.Secret))
      {
        var entry = Assert.Single(secretEdges.Where(edge => edge.To == secret.Id));
        var exit = Assert.Single(secretEdges.Where(edge => edge.From == secret.Id));
        Assert.Equal(1, UndirectedDistance(graph, entry.From, exit.To));
      }

      // Assert: The tighter bound still respects every cap.
      GraphTestAssert.AssertMaxEdges(graph);
    }

    [Fact]
    public void Execute_WithReturnRollButOnlyOneFreeSlot_FallsBackToSinglePassage()
    {
      // Arrange: Only node 1 may host, and it has room for exactly one more relationship
      // - not enough for the two slots the return layout needs.
      var graph = CreateCappedChainGraph(
        [
          (maxEdges: 2, start: true, exit: false),
          (maxEdges: 1, start: false, exit: true),
        ]);
      var options = CnfProcRelSecretsOpt.Default with
      {
        Amount = 1,
        ChanceSecretInSecret = 0f,
        ChanceTwoPassages = 1f,
        ChanceReturnToHost = 1f,
      };

      // Act: Ask for a two-passage secret anyway.
      new ProcRelSecrets(graph, options, Seed).Execute();

      // Assert: The return layout does not fit and no other node has a free slot for an
      // exit, so the pass falls back to the plain single-passage secret.
      var secretEdge = Assert.Single(graph.Edges.Where(edge => edge.Secret));
      Assert.Equal(Id(1), secretEdge.From);
      Assert.Equal(EnPassageDirection.Bidirectional, secretEdge.Direction);
      GraphTestAssert.AssertMaxEdges(graph);
    }

    [Fact]
    public void Execute_WithReturnRollAndTwoFreeSlots_ProducesReturnPassage()
    {
      // Arrange: Only node 1 may host, and it has room for exactly two more
      // relationships - precisely what the return layout consumes.
      var graph = CreateCappedChainGraph(
        [
          (maxEdges: 3, start: true, exit: false),
          (maxEdges: 1, start: false, exit: true),
        ]);
      var options = CnfProcRelSecretsOpt.Default with
      {
        Amount = 1,
        ChanceSecretInSecret = 0f,
        ChanceTwoPassages = 1f,
        ChanceReturnToHost = 1f,
      };

      // Act: Add the secret.
      new ProcRelSecrets(graph, options, Seed).Execute();

      // Assert: Both passages fit exactly into the host's remaining slots - entry out of
      // node 1 and exit back into node 1, one way each.
      var secret = Assert.Single(graph.Nodes.Where(node => node.Secret));
      var secretEdges = graph.Edges.Where(edge => edge.Secret).ToList();
      Assert.Equal(2, secretEdges.Count);
      var entry = Assert.Single(secretEdges.Where(edge => edge.To == secret.Id));
      var exit = Assert.Single(secretEdges.Where(edge => edge.From == secret.Id));
      Assert.Equal(Id(1), entry.From);
      Assert.Equal(Id(1), exit.To);
      Assert.Equal(EnPassageDirection.OneWay, entry.Direction);
      Assert.Equal(EnPassageDirection.OneWay, exit.Direction);
      GraphTestAssert.AssertMaxEdges(graph);
    }

    [Fact]
    public void Execute_WithForcedNestingAndExits_NeverTargetsSecretAsExit()
    {
      for (var seed = 0; seed < StatisticalSeedCount; seed++)
      {
        // Arrange: A chain where every later secret nests behind the previous one and
        // every secret must look for an exit among the ordinary nodes.
        var graph = CreateStartExitChainGraph(6);
        var options = CnfProcRelSecretsOpt.Default with
        {
          Amount = 4,
          ChanceSecretInSecret = 1f,
          ChanceTwoPassages = 1f,
          ChanceReturnToHost = 0f,
        };

        // Act: Add four nested secrets.
        new ProcRelSecrets(graph, options, seed).Execute();

        // Assert: Four entries plus four exits - every secret found its exit.
        var secretEdges = graph.Edges.Where(edge => edge.Secret).ToList();
        Assert.Equal(8, secretEdges.Count);

        // Assert: Exactly one passage arrives at every secret - its entry. An exit that
        // targeted a secret would show up as a second incoming passage.
        foreach (var secret in graph.Nodes.Where(node => node.Secret))
        {
          Assert.Equal(1, graph.Edges.Count(edge => edge.To == secret.Id));
        }

        // Assert: Entry, exit and nesting never push a node over its cap.
        GraphTestAssert.AssertMaxEdges(graph);
      }
    }

    [Fact]
    public void Execute_WithForcedExit_WeightsNearerNodesHigher()
    {
      // Arrange: Many seeds, each with one exit-passage secret on a long chain, so the
      // observed distance distribution reflects the linear weights.
      var nearCount = 0;
      var farCount = 0;
      const int seedCount = 200;
      for (var seed = 0; seed < seedCount; seed++)
      {
        var graph = CreateStartExitChainGraph(10);
        var options = CnfProcRelSecretsOpt.Default with
        {
          Amount = 1,
          ChanceSecretInSecret = 0f,
          ChanceTwoPassages = 1f,
          ChanceReturnToHost = 0f,
        };

        // Act: Add one exit-passage secret.
        new ProcRelSecrets(graph, options, seed).Execute();

        // Measure: Distance between the host and the chosen exit node.
        var secret = Assert.Single(graph.Nodes.Where(node => node.Secret));
        var secretEdges = graph.Edges.Where(edge => edge.Secret).ToList();
        var entry = Assert.Single(secretEdges.Where(edge => edge.To == secret.Id));
        var exit = Assert.Single(secretEdges.Where(edge => edge.From == secret.Id));
        var distance = UndirectedDistance(graph, entry.From, exit.To);
        Assert.InRange(distance, 1, 3);
        if (distance == 1) nearCount++;
        else if (distance == 3) farCount++;
      }

      // Assert: The nearest distance carries the highest weight, so over many picks it
      // must clearly outnumber the farthest one.
      Assert.True(nearCount > farCount,
        $"Distance 1 picked {nearCount} times vs distance 3 picked {farCount} times over {seedCount} seeds.");
    }

    [Fact]
    public void Execute_WithChanceOneOverManySeeds_KeepsGraphsWithinCapsAndBeatable()
    {
      for (var seed = 0; seed < StatisticalSeedCount; seed++)
      {
        // Arrange: A tightly capped chain; the layout rolls stay at their defaults, so
        // each secret independently lands on one of the three layouts.
        var graph = CreateCappedChainGraph(
        [
          (maxEdges: 2, start: true, exit: false),
          (maxEdges: 3, start: false, exit: false),
          (maxEdges: 2, start: false, exit: false),
          (maxEdges: 3, start: false, exit: true),
        ]);
        var options = CnfProcRelSecretsOpt.Default with
        {
          Amount = 6,
          ChanceTwoPassages = 1f,
        };

        // Act: Ask for more secrets than the ends can comfortably host.
        new ProcRelSecrets(graph, options, seed).Execute();

        // Assert: Every layout observes the caps and keeps the level winnable.
        GraphTestAssert.AssertMaxEdges(graph);
        GraphTestAssert.AssertBeatable(graph, $"Two-passage capped graph must stay beatable (seed {seed}).");
      }
    }

    [Fact]
    public void Execute_WithSameSeedAndTwoPassages_ProducesIdenticalGraphs()
    {
      // Arrange: Two identical chain graphs and the same two-passage inputs.
      var first = CreateStartExitChainGraph(6);
      var second = CreateStartExitChainGraph(6);
      var options = CnfProcRelSecretsOpt.Default with
      {
        Amount = 5,
        ChanceSecretInSecret = 0.5f,
        ChanceTwoPassages = 1f,
      };

      // Act: Run the pass on both graphs.
      new ProcRelSecrets(first, options, Seed).Execute();
      new ProcRelSecrets(second, options, Seed).Execute();

      // Assert: Layout rolls, exit search and identifier derivation all stay reproducible.
      GraphTestAssert.AssertEqualGraphs(first, second);
    }
  }

  /// <summary>Tests for the per-secret conditional roll on the exit search.</summary>
  public sealed class TwoPassageConditionalTests
  {
    [Fact]
    public void Execute_WithConditionalsBlocked_FallsBackToSinglePassage()
    {
      // Arrange: Two nodes joined only by a conditional passage, both with one free slot.
      // When conditional passages are off, the only neighbor is unreachable for the exit
      // search, so no candidate exists.
      var graph = CreateConditionalPairGraph();
      var originalNodeIds = graph.Nodes.Select(node => node.Id).ToHashSet();
      var options = CnfProcRelSecretsOpt.Default with
      {
        Amount = 1,
        ChanceSecretInSecret = 0f,
        ChanceTwoPassages = 1f,
        ChanceReturnToHost = 0f,
        ChanceIgnoreConditionals = 0f,
      };

      // Act: Add one secret.
      new ProcRelSecrets(graph, options, Seed).Execute();

      // Assert: The exit search finds nothing without the conditional passage, so the
      // secret falls back to the single open entry passage.
      var secret = Assert.Single(graph.Nodes.Where(node => node.Secret));
      var secretEdge = Assert.Single(graph.Edges.Where(edge => edge.Secret));
      Assert.Equal(secret.Id, secretEdge.To);
      Assert.Contains(secretEdge.From, originalNodeIds);
      Assert.Equal(EnPassageDirection.Bidirectional, secretEdge.Direction);
      GraphTestAssert.AssertMaxEdges(graph);
      GraphTestAssert.AssertBeatable(graph, "Blocked-conditional graph must stay beatable.");
    }

    [Fact]
    public void Execute_WithConditionalsAllowed_CrossesConditionalForExit()
    {
      // Arrange: The same pair, but this secret may cross conditional passages.
      var graph = CreateConditionalPairGraph();
      var originalNodeIds = graph.Nodes.Select(node => node.Id).ToHashSet();
      var options = CnfProcRelSecretsOpt.Default with
      {
        Amount = 1,
        ChanceSecretInSecret = 0f,
        ChanceTwoPassages = 1f,
        ChanceReturnToHost = 0f,
        ChanceIgnoreConditionals = 1f,
      };

      // Act: Add one secret.
      new ProcRelSecrets(graph, options, Seed).Execute();

      // Assert: The neighbor behind the conditional passage becomes a valid exit, so the
      // secret gets both passages: entry from the host, exit to the other node.
      var secret = Assert.Single(graph.Nodes.Where(node => node.Secret));
      var secretEdges = graph.Edges.Where(edge => edge.Secret).ToList();
      Assert.Equal(2, secretEdges.Count);
      var entry = Assert.Single(secretEdges.Where(edge => edge.To == secret.Id));
      var exit = Assert.Single(secretEdges.Where(edge => edge.From == secret.Id));
      Assert.Contains(entry.From, originalNodeIds);
      Assert.Contains(exit.To, originalNodeIds);
      Assert.NotEqual(entry.From, exit.To);
      GraphTestAssert.AssertMaxEdges(graph);
      GraphTestAssert.AssertBeatable(graph, "Allowed-conditional graph must stay beatable.");
    }

    [Fact]
    public void Execute_WithDefaultConditionalChance_ProducesMostlyBlockedExits()
    {
      // Arrange: The default ten percent allowance - most secrets must stay blocked.
      var blockedCount = 0;
      var allowedCount = 0;
      const int seedCount = 150;
      for (var seed = 0; seed < seedCount; seed++)
      {
        var graph = CreateConditionalPairGraph();
        var options = CnfProcRelSecretsOpt.Default with
        {
          Amount = 1,
          ChanceSecretInSecret = 0f,
          ChanceTwoPassages = 1f,
          ChanceReturnToHost = 0f,
        };

        // Act: Add one secret and read back whether the exit search won.
        new ProcRelSecrets(graph, options, seed).Execute();
        var secretEdges = graph.Edges.Where(edge => edge.Secret).ToList();
        if (secretEdges.Count == 1) blockedCount++;
        else allowedCount++;
      }

      // Assert: Both rolls happen in practice, but blocking is by far the common case.
      Assert.True(blockedCount > 0, $"The blocked roll never hit over {seedCount} seeds.");
      Assert.True(allowedCount > 0, $"The allowed roll never hit over {seedCount} seeds.");
      Assert.True(blockedCount > allowedCount,
        $"Blocked {blockedCount} vs allowed {allowedCount} over {seedCount} seeds.");
    }
  }

  //
  // Graph fixtures
  //

  /// <summary>
  /// Builds a two-node graph whose only passage is conditional: node 1 holds the Start
  /// and the referenced Solution POI, node 2 the Exit. Both nodes have exactly one free
  /// relationship slot.
  /// </summary>
  /// <returns>The constructed graph with one conditional edge.</returns>
  private static LilGraph CreateConditionalPairGraph()
  {
    var graph = new LilGraph();
    graph.Nodes.Add(Node(1, maxEdges: 2, Poi(1001, EnPoiType.Start), Poi(1002, EnPoiType.Solution)));
    graph.Nodes.Add(Node(2, maxEdges: 2, Poi(2001, EnPoiType.Exit)));
    graph.Edges.Add(Edge(1, 1, 2, conditions: [Condition(1002)]));
    return graph;
  }

  /// <summary>
  /// Measures the undirected hop distance between two nodes, ignoring passage direction -
  /// the same view the two-passage exit search uses.
  /// </summary>
  /// <param name="graph">Graph to measure in.</param>
  /// <param name="from">Source node identifier.</param>
  /// <param name="to">Target node identifier.</param>
  /// <returns>Number of hops; -1 when the target is unreachable.</returns>
  private static int UndirectedDistance(LilGraph graph, Guid from, Guid to)
  {
    var adjacency = new Dictionary<Guid, List<Guid>>();
    foreach (var node in graph.Nodes)
    {
      adjacency[node.Id] = [];
    }
    foreach (var edge in graph.Edges)
    {
      adjacency[edge.From].Add(edge.To);
      adjacency[edge.To].Add(edge.From);
    }

    var distances = new Dictionary<Guid, int> { [from] = 0 };
    var queue = new Queue<Guid>();
    queue.Enqueue(from);
    while (queue.Count > 0)
    {
      var current = queue.Dequeue();
      foreach (var next in adjacency[current])
      {
        if (distances.ContainsKey(next)) continue;
        distances[next] = distances[current] + 1;
        queue.Enqueue(next);
      }
    }

    return distances.TryGetValue(to, out var distance) ? distance : -1;
  }

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
