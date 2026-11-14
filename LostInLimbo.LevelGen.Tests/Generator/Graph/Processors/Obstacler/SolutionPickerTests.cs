using LostInLimbo.LevelGen.Data.Config.Graph.Proc;
using LostInLimbo.LevelGen.Data.Result.Graph;
using LostInLimbo.LevelGen.Internal.Generator.Graph.Processors.Obstacler;
using LostInLimbo.LevelGen.Internal.Utils.Rng;
using static LostInLimbo.LevelGen.Tests.Generator.Graph.Processors.Obstacler.ObstaclerTestSupport;

namespace LostInLimbo.LevelGen.Tests.Generator.Graph.Processors.Obstacler;

/// <summary>
/// Tests <see cref="SolutionPicker"/>: which nodes may be picked, the weights and determinism.
/// </summary>
public sealed class SolutionPickerTests
{
  /// <summary>Number of picks used by the statistical tests.</summary>
  private const int Picks = 400;

  /// <summary>Creates the work graph of a level with default options.</summary>
  /// <param name="graph">Graph to index.</param>
  /// <returns>The work graph.</returns>
  private static ObstacleWorkGraph Index(LilGraph graph) => new(graph, CnfProcObstaclePlacerOpt.Default);

  /// <summary>Creates flags with every node set.</summary>
  /// <param name="work">Graph the flags are for.</param>
  /// <returns>All-true flags.</returns>
  private static bool[] All(ObstacleWorkGraph work)
  {
    var flags = new bool[work.NodeCount];
    Array.Fill(flags, true);
    return flags;
  }

  /// <summary>Tests which nodes are eligible.</summary>
  public sealed class EligibilityTests
  {
    [Fact]
    public void Pick_OnlyReturnsReachableNodes()
    {
      // Arrange: Chain 1 - 2 - 3 - 4 locked between 2 and 3; only the first two are reachable.
      var work = Index(CreateLevel(4, 1, 4, Chain(4)));
      var picker = new SolutionPicker(work, new SeededRandom(7));
      bool[] reachable = [true, true, false, false];

      // Act/Assert: Many picks never leave the reachable part.
      for (var pick = 0; pick < Picks; pick++) Assert.InRange(picker.Pick(reachable, [1], null), 0, 1);
    }

    [Fact]
    public void Pick_NeverReturnsExcludedNodes()
    {
      // Arrange: Everything reachable, node 2 excluded.
      var work = Index(CreateLevel(3, 1, 3, Chain(3)));
      var picker = new SolutionPicker(work, new SeededRandom(7));
      bool[] excluded = [false, true, false];

      // Act/Assert: The excluded node never comes up.
      for (var pick = 0; pick < Picks; pick++) Assert.NotEqual(1, picker.Pick(All(work), [1], excluded));
    }

    [Fact]
    public void Pick_NeverReturnsSecretNodes()
    {
      // Arrange: Node 2 is secret, yet flagged reachable.
      var graph = CreateLevel(3, 1, 3, Chain(3));
      MarkSecret(graph, 2);
      var work = Index(graph);
      var picker = new SolutionPicker(work, new SeededRandom(7));

      // Act/Assert: A solution never lands in a secret node.
      for (var pick = 0; pick < Picks; pick++) Assert.NotEqual(1, picker.Pick(All(work), [1], null));
    }

    [Fact]
    public void Pick_WithoutEligibleNode_ReturnsNoNodeAndDrawsNothing()
    {
      // Arrange: Two pickers on equal random streams; one is asked an impossible pick first.
      var work = Index(CreateLevel(3, 1, 3, Chain(3)));
      var asked = new SolutionPicker(work, new SeededRandom(7));
      var fresh = new SolutionPicker(work, new SeededRandom(7));
      bool[] everything = [true, true, true];

      // Act: Impossible pick, then a normal one on both.
      var none = asked.Pick(All(work), [0], everything);
      var afterNone = asked.Pick(All(work), [0], null);
      var reference = fresh.Pick(All(work), [0], null);

      // Assert: The failed pick consumed no random number.
      Assert.Equal(ObstacleWorkGraph.NoNode, none);
      Assert.Equal(reference, afterNone);
    }

    [Fact]
    public void Pick_WithSingleEligibleNode_ReturnsIt()
    {
      // Arrange: Only the start is reachable.
      var work = Index(CreateLevel(3, 1, 3, Chain(3)));
      var picker = new SolutionPicker(work, new SeededRandom(7));

      // Act/Assert: The start has a low weight, never an impossible one.
      Assert.Equal(0, picker.Pick([true, false, false], [0], null));
    }
  }

  /// <summary>Tests the weight rules.</summary>
  public sealed class WeightTests
  {
    [Fact]
    public void ComputeWeight_FollowsTheDocumentedRules()
    {
      // Arrange: Chain 1 - 2 - 3 - 4 with a leaf 5 on node 2; start in node 1. Node 4 holds a solution.
      var graph = CreateLevel(5, 1, 4, new Passage(1, 2), new Passage(2, 3), new Passage(3, 4), new Passage(2, 5));
      graph.Nodes[3].Pois.Add(GraphTestCreate.Poi(900, EnPoiType.Solution));
      var picker = new SolutionPicker(Index(graph), new SeededRandom(7));

      // Act/Assert: Middle node - base plus hops.
      Assert.Equal(SolutionPicker.WeightBase, picker.ComputeWeight(2, 0));
      Assert.Equal(SolutionPicker.WeightBase + 2 * SolutionPicker.WeightPerHop, picker.ComputeWeight(2, 2));

      // Hops stop counting at the cap, and a negative value counts as none.
      var capped = SolutionPicker.WeightBase + SolutionPicker.MaxHopBonus * SolutionPicker.WeightPerHop;
      Assert.Equal(capped, picker.ComputeWeight(2, 50));
      Assert.Equal(SolutionPicker.WeightBase, picker.ComputeWeight(2, -3));

      // Dead end - bonus on top.
      Assert.Equal(SolutionPicker.WeightBase + SolutionPicker.WeightDeadEnd, picker.ComputeWeight(4, 0));

      // Start - divided, although it is a dead end too.
      Assert.Equal(
        (SolutionPicker.WeightBase + SolutionPicker.WeightDeadEnd) / SolutionPicker.StartDivisor,
        picker.ComputeWeight(0, 0));

      // Node already holding one solution - halved.
      Assert.Equal((SolutionPicker.WeightBase + SolutionPicker.WeightDeadEnd) / 2, picker.ComputeWeight(3, 0));
    }

    [Fact]
    public void ComputeWeight_NeverDropsBelowOne()
    {
      // Arrange: Start node buried under solutions.
      var graph = CreateLevel(3, 2, 3, Chain(3));
      for (var id = 900; id < 940; id++) graph.Nodes[1].Pois.Add(GraphTestCreate.Poi(id, EnPoiType.Solution));
      var picker = new SolutionPicker(Index(graph), new SeededRandom(7));

      // Act/Assert: Still pickable.
      Assert.Equal(1, picker.ComputeWeight(1, 0));
    }

    [Fact]
    public void Pick_PrefersDeadEndsOverTheStartAndThroughNodes()
    {
      // Arrange: Start 1 - 2 - 3, leaf 4 on node 2; the passage 2 - 3 is locked.
      var work = Index(CreateLevel(4, 1, 3, new Passage(1, 2), new Passage(2, 3), new Passage(2, 4)));
      var picker = new SolutionPicker(work, new SeededRandom(11));
      bool[] reachable = [true, true, false, true];
      var counts = new int[4];

      // Act: Many picks.
      for (var pick = 0; pick < Picks; pick++) counts[picker.Pick(reachable, [1], null)]++;

      // Assert: Leaf beats the through node, which beats the start; all three do occur.
      Assert.True(counts[3] > counts[1], $"leaf {counts[3]}, through {counts[1]}");
      Assert.True(counts[1] > counts[0], $"through {counts[1]}, start {counts[0]}");
      Assert.True(counts[0] > 0);
      Assert.Equal(0, counts[2]);
    }
  }

  /// <summary>Tests determinism and argument validation.</summary>
  public sealed class ContractTests
  {
    [Fact]
    public void Pick_WithSameSeed_ProducesSameSequence()
    {
      // Arrange: Two pickers on equal random streams.
      var work = Index(CreateLevel(6, 1, 6, Chain(6)));
      var first = new SolutionPicker(work, new SeededRandom(5));
      var second = new SolutionPicker(work, new SeededRandom(5));

      // Act/Assert: Pick by pick the same node.
      for (var pick = 0; pick < 50; pick++)
      {
        Assert.Equal(first.Pick(All(work), [4], null), second.Pick(All(work), [4], null));
      }
    }

    [Fact]
    public void Constructor_WithNullArguments_ThrowsArgumentNullException()
    {
      // Arrange: Any graph.
      var work = Index(CreateLevel(2, 1, 2, Chain(2)));

      // Act/Assert: Each missing argument is rejected naming the parameter.
      Assert.Equal("graph", Assert.Throws<ArgumentNullException>(() => new SolutionPicker(null!, new SeededRandom(1))).ParamName);
      Assert.Equal("random", Assert.Throws<ArgumentNullException>(() => new SolutionPicker(work, null!)).ParamName);
    }

    [Fact]
    public void Pick_WithBadArguments_Throws()
    {
      // Arrange: Graph of two nodes.
      var work = Index(CreateLevel(2, 1, 2, Chain(2)));
      var picker = new SolutionPicker(work, new SeededRandom(1));

      // Act/Assert: Missing or mis-sized arguments are rejected.
      Assert.Throws<ArgumentNullException>(() => picker.Pick(null!, [0], null));
      Assert.Throws<ArgumentNullException>(() => picker.Pick([true, true], null!, null));
      Assert.Throws<ArgumentException>(() => picker.Pick([true], [0], null));
      Assert.Throws<ArgumentException>(() => picker.Pick([true, true], [0], [false]));
    }
  }
}
