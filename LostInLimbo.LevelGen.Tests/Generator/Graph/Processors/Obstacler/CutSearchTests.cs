using LostInLimbo.LevelGen.Data.Config.Graph.Proc;
using LostInLimbo.LevelGen.Internal.Generator.Graph.Processors.Obstacler;
using static LostInLimbo.LevelGen.Tests.Generator.Graph.Processors.Obstacler.ObstaclerTestSupport;

namespace LostInLimbo.LevelGen.Tests.Generator.Graph.Processors.Obstacler;

/// <summary>
/// Tests <see cref="CutSearch"/> on hand-made graphs: which cuts are offered, in which order,
/// and which are refused.
/// </summary>
public sealed class CutSearchTests
{
  /// <summary>Default width cap, from the default options.</summary>
  private static readonly int DefaultMaxWidth = CnfProcObstaclePlacerOpt.Default.MaxEdgesPerObstacle;

  /// <summary>Runs a main-pass search with the default width cap.</summary>
  /// <param name="work">Graph to search.</param>
  /// <param name="source">Work index of the source node.</param>
  /// <param name="target">Work index of the target node.</param>
  /// <returns>The candidates, cheapest first.</returns>
  private static List<CutCandidate> Search(ObstacleWorkGraph work, int source, int target)
  {
    return new CutSearch(work, DefaultMaxWidth).FindCandidates(source, target, work.BuildMainCutCosts());
  }

  /// <summary>Lists the lock sets of candidates, one array per candidate.</summary>
  /// <param name="candidates">Candidates to list.</param>
  /// <returns>Lock edges per candidate, in candidate order.</returns>
  private static int[][] LockSets(List<CutCandidate> candidates)
  {
    var sets = new int[candidates.Count][];
    for (var index = 0; index < sets.Length; index++) sets[index] = [.. candidates[index].LockEdges];
    return sets;
  }

  /// <summary>Tests which cuts are found on basic graph shapes.</summary>
  public sealed class ShapeTests
  {
    [Fact]
    public void FindCandidates_OnChain_OffersEveryPassageOnce()
    {
      // Arrange: Chain 1 - 2 - 3 - 4.
      var work = CreateWorkGraph(4, Chain(4));

      // Act: Keep the last node away from the first.
      var candidates = Search(work, 0, 3);

      // Assert: Each passage is a width-1 cut; equal costs keep discovery order, which starts
      // at the target and moves towards the source.
      Assert.Equal([[2], [1], [0]], LockSets(candidates));
      Assert.All(candidates, candidate => Assert.Equal(1, candidate.OrdinaryWidth));
      Assert.Equal([3], candidates[0].FarNodes);
      Assert.Equal([1, 2, 3], candidates[2].FarNodes);
    }

    [Fact]
    public void FindCandidates_OnCycle_OffersTwoPassageCuts()
    {
      // Arrange: Cycle 1 - 2 - 3 - 4 - 1.
      var work = CreateWorkGraph(4, new Passage(1, 2), new Passage(2, 3), new Passage(3, 4), new Passage(4, 1));

      // Act: Keep node 3 away from the opposite node 1.
      var candidates = Search(work, 0, 2);

      // Assert: Both ways round must be locked; one cut hugs the target, one hugs the source.
      Assert.Equal([[1, 2], [0, 3]], LockSets(candidates));
      Assert.All(candidates, candidate => Assert.Equal(2, candidate.OrdinaryWidth));
    }

    [Fact]
    public void FindCandidates_OnStar_CutsOnlyTheTargetSpoke()
    {
      // Arrange: Center 1 with leaves 2, 3 and 4.
      var work = CreateWorkGraph(4, new Passage(1, 2), new Passage(1, 3), new Passage(1, 4));

      // Act: Keep leaf 4 away from leaf 2 ...
      var leafToLeaf = Search(work, 1, 3);
      // ... and away from the center.
      var centerToLeaf = Search(work, 0, 3);

      // Assert: Between leaves both spokes on the way are bottlenecks; from the center only one.
      Assert.Equal([[2], [0]], LockSets(leafToLeaf));
      Assert.Equal([[2]], LockSets(centerToLeaf));
    }

    [Fact]
    public void FindCandidates_WithAdjacentSourceAndTarget_AlwaysLocksTheDirectPassage()
    {
      // Arrange: Triangle 1 - 2, 2 - 3, 1 - 3.
      var work = CreateWorkGraph(3, new Passage(1, 2), new Passage(2, 3), new Passage(1, 3));

      // Act: Keep node 2 away from its neighbour node 1.
      var candidates = Search(work, 0, 1);

      // Assert: Every cut contains the direct passage plus one passage of the detour.
      Assert.Equal([[0, 1], [0, 2]], LockSets(candidates));
    }

    [Fact]
    public void FindCandidates_WithDirectPassageOnly_OffersThatPassage()
    {
      // Arrange: Two nodes, one passage.
      var work = CreateWorkGraph(2, Chain(2));

      // Act: Cut them apart.
      var candidates = Search(work, 0, 1);

      // Assert: A single candidate.
      Assert.Equal([[0]], LockSets(candidates));
    }

    [Fact]
    public void FindCandidates_WithSameSourceAndTarget_ReturnsEmpty()
    {
      // Arrange: Any graph.
      var work = CreateWorkGraph(3, Chain(3));

      // Act/Assert: A node cannot be cut from itself.
      Assert.Empty(Search(work, 1, 1));
    }

    [Fact]
    public void FindCandidates_WithUnconnectedTarget_ReturnsEmpty()
    {
      // Arrange: Node 3 has no passage.
      var work = CreateWorkGraph(3, new Passage(1, 2));

      // Act/Assert: Nothing to lock.
      Assert.Empty(Search(work, 0, 2));
    }

    [Fact]
    public void FindCandidates_CalledTwice_ReturnsSameCandidates()
    {
      // Arrange: Cycle with a tail, mixed passage kinds.
      var work = CreateWorkGraph(
        5,
        new Passage(1, 2, Chokepoint: true),
        new Passage(2, 3),
        new Passage(3, 4, Chokepoint: true),
        new Passage(4, 1),
        new Passage(3, 5));
      var search = new CutSearch(work, DefaultMaxWidth);

      // Act: Same query twice.
      var first = search.FindCandidates(0, 4, work.BuildMainCutCosts());
      var second = search.FindCandidates(0, 4, work.BuildMainCutCosts());

      // Assert: Identical candidates in identical order.
      Assert.Equal(LockSets(first), LockSets(second));
      Assert.NotEmpty(first);
    }
  }

  /// <summary>Tests the ordering by cost: chokepoints, stacking, width.</summary>
  public sealed class OrderTests
  {
    [Fact]
    public void FindCandidates_WithChokepointOnTheWay_OffersItFirst()
    {
      // Arrange: Chain 1 - 2 - 3 - 4 whose first passage is the only chokepoint.
      var work = CreateWorkGraph(4, new Passage(1, 2, Chokepoint: true), new Passage(2, 3), new Passage(3, 4));

      // Act: Keep the last node away from the first.
      var candidates = Search(work, 0, 3);

      // Assert: The chokepoint leads. It is the only minimum cut, so the whole target series
      // lands on it; the dearer passages only show up once the grown source swallows it.
      Assert.Equal([[0], [1], [2]], LockSets(candidates));
      Assert.Equal(ObstacleWorkGraph.CostChokepoint, candidates[0].Cost);
    }

    [Fact]
    public void FindCandidates_WithStackedPassage_OffersFreshOnesFirst()
    {
      // Arrange: Chain 1 - 2 - 3 - 4 whose last passage already carries a condition.
      var work = CreateWorkGraph(4, new Passage(1, 2), new Passage(2, 3), new Passage(3, 4, Conditions: 1));

      // Act: Keep the last node away from the first.
      var candidates = Search(work, 0, 3);

      // Assert: The stacked passage comes last and is marked as stacking.
      Assert.Equal([[1], [0], [2]], LockSets(candidates));
      Assert.Equal(0, candidates[0].MaxConditions);
      Assert.Equal(1, candidates[2].MaxConditions);
    }

    [Fact]
    public void FindCandidates_WithParallelPassages_LocksThemTogetherAndRanksThemWider()
    {
      // Arrange: 1 - 2 by one passage, 2 - 3 by two parallel passages (one a chokepoint).
      var work = CreateWorkGraph(3, new Passage(1, 2), new Passage(2, 3, Chokepoint: true), new Passage(2, 3));

      // Act: Keep node 3 away from node 1.
      var candidates = Search(work, 0, 2);

      // Assert: The single passage wins; the parallel pair is one cut of width two and is
      // never split, chokepoint or not.
      Assert.Equal([[0], [1, 2]], LockSets(candidates));
      Assert.Equal(2, candidates[1].OrdinaryWidth);
      Assert.Equal(ObstacleWorkGraph.CostChokepoint + ObstacleWorkGraph.CostOpen, candidates[1].Cost);
    }
  }

  /// <summary>Tests the cuts that must be refused.</summary>
  public sealed class RefusalTests
  {
    [Fact]
    public void FindCandidates_WithWidthCapBelowCut_RefusesIt()
    {
      // Arrange: Cycle, where every cut needs two passages.
      var work = CreateWorkGraph(4, new Passage(1, 2), new Passage(2, 3), new Passage(3, 4), new Passage(4, 1));
      var search = new CutSearch(work, maxOrdinaryWidth: 1);

      // Act/Assert: No cut fits a cap of one passage.
      Assert.Empty(search.FindCandidates(0, 2, work.BuildMainCutCosts()));
    }

    [Fact]
    public void FindCandidates_WithWidthCap_KeepsNarrowCutsOnly()
    {
      // Arrange: Single passage followed by a parallel pair.
      var work = CreateWorkGraph(3, new Passage(1, 2), new Passage(2, 3), new Passage(2, 3));
      var search = new CutSearch(work, maxOrdinaryWidth: 1);

      // Act: Keep node 3 away from node 1.
      var candidates = search.FindCandidates(0, 2, work.BuildMainCutCosts());

      // Assert: The pair is too wide; the single passage remains.
      Assert.Equal([[0]], LockSets(candidates));
    }

    [Fact]
    public void FindCandidates_WithSaturatedPassage_NeverCutsIt()
    {
      // Arrange: Chain 1 - 2 - 3 whose second passage is at the condition cap.
      var work = CreateWorkGraph(3, new Passage(1, 2), new Passage(2, 3, Conditions: 3));

      // Act: Keep node 3 away from node 1.
      var candidates = Search(work, 0, 2);

      // Assert: Only the other passage is offered.
      Assert.Equal([[0]], LockSets(candidates));
    }

    [Fact]
    public void FindCandidates_WhenOnlyPassageIsSaturated_ReturnsEmpty()
    {
      // Arrange: Two nodes joined by one full passage.
      var work = CreateWorkGraph(2, new Passage(1, 2, Conditions: 3));

      // Act/Assert: Nothing can be locked any more.
      Assert.Empty(Search(work, 0, 1));
    }

    [Fact]
    public void FindCandidates_WithOneSaturatedParallel_RefusesTheWholePair()
    {
      // Arrange: 1 - 2 single, 2 - 3 by a fresh and a full parallel passage.
      var work = CreateWorkGraph(3, new Passage(1, 2), new Passage(2, 3), new Passage(2, 3, Conditions: 3));

      // Act: Keep node 3 away from node 1.
      var candidates = Search(work, 0, 2);

      // Assert: Locking only the fresh parallel would leak, so the pair is uncuttable.
      Assert.Equal([[0]], LockSets(candidates));
    }

    [Fact]
    public void FindCandidates_WithOneWayPassage_NeverCutsIt()
    {
      // Arrange: 1 - 2 two-way, 2 -> 3 one-way.
      var work = CreateWorkGraph(3, new Passage(1, 2), new Passage(2, 3, OneWay: true));

      // Act: Keep node 3 away from node 1.
      var candidates = Search(work, 0, 2);

      // Assert: Both ends of the one-way passage stay on the same side.
      Assert.Equal([[0]], LockSets(candidates));
      Assert.Equal([1, 2], candidates[0].FarNodes);
    }
  }

  /// <summary>Tests how secret passages take part in cuts.</summary>
  public sealed class SecretTests
  {
    [Fact]
    public void FindCandidates_WithSecretRoute_LocksItForFree()
    {
      // Arrange: Chain 1 - 2 - 3 plus secret node 4 bridging 1 and 3.
      var work = CreateWorkGraph(
        4,
        new Passage(1, 2),
        new Passage(2, 3),
        new Passage(1, 4, Secret: true),
        new Passage(4, 3, Secret: true));

      // Act: Keep node 3 away from node 1.
      var candidates = Search(work, 0, 2);

      // Assert: Both bottlenecks stay width-1 cuts; each also locks the secret passage that
      // leads from the start side into the far side.
      Assert.Equal([[1, 3], [0, 3]], LockSets(candidates));
      Assert.All(candidates, candidate =>
      {
        Assert.Equal(1, candidate.OrdinaryWidth);
        Assert.Equal(ObstacleWorkGraph.CostOpen, candidate.Cost);
      });
    }

    [Fact]
    public void FindCandidates_WithSecretRouteOnly_OffersASecretOnlyCut()
    {
      // Arrange: Nodes 1 and 3 joined only through secret node 2.
      var work = CreateWorkGraph(3, new Passage(1, 2, Secret: true), new Passage(2, 3, Secret: true));

      // Act: Keep node 3 away from node 1.
      var candidates = Search(work, 0, 2);

      // Assert: The secret node joins the start side; the passage behind it is locked at no
      // cost and with no ordinary width.
      Assert.Equal([[1]], LockSets(candidates));
      Assert.Equal(0, candidates[0].OrdinaryWidth);
      Assert.Equal(0, candidates[0].Cost);
    }

    [Fact]
    public void FindCandidates_WithSecretLeaves_NeverLocksThem()
    {
      // Arrange: Chain 1 - 2 - 3 with a secret leaf on every node.
      var work = CreateWorkGraph(
        6,
        new Passage(1, 2),
        new Passage(2, 3),
        new Passage(1, 4, Secret: true),
        new Passage(2, 5, Secret: true),
        new Passage(3, 6, Secret: true));

      // Act: Keep node 3 away from node 1.
      var candidates = Search(work, 0, 2);

      // Assert: Only the chain passages are locked; leaves follow their hosts.
      Assert.Equal([[1], [0]], LockSets(candidates));
      Assert.Equal([2, 5], candidates[0].FarNodes);
      Assert.Equal([1, 2, 4, 5], candidates[1].FarNodes);
    }
  }

  /// <summary>Tests argument validation.</summary>
  public sealed class ValidationTests
  {
    [Fact]
    public void Constructor_WithNullGraph_ThrowsArgumentNullException()
    {
      // Act/Assert: A missing graph is rejected naming the parameter.
      var exception = Assert.Throws<ArgumentNullException>(() => new CutSearch(null!, 1));
      Assert.Equal("graph", exception.ParamName);
    }

    [Fact]
    public void Constructor_WithWidthBelowOne_ThrowsArgumentOutOfRangeException()
    {
      // Arrange: Any graph.
      var work = CreateWorkGraph(2, Chain(2));

      // Act/Assert: At least one ordinary passage must be allowed.
      var exception = Assert.Throws<ArgumentOutOfRangeException>(() => new CutSearch(work, 0));
      Assert.Equal("maxOrdinaryWidth", exception.ParamName);
    }

    [Fact]
    public void FindCandidates_WithBadArguments_Throws()
    {
      // Arrange: Any graph.
      var work = CreateWorkGraph(2, Chain(2));
      var search = new CutSearch(work, DefaultMaxWidth);
      var costs = work.BuildMainCutCosts();

      // Act/Assert: Missing costs, mis-sized costs and unknown nodes are rejected.
      Assert.Throws<ArgumentNullException>(() => search.FindCandidates(0, 1, null!));
      Assert.Throws<ArgumentException>(() => search.FindCandidates(0, 1, [1, 1]));
      Assert.Equal("source", Assert.Throws<ArgumentOutOfRangeException>(() => search.FindCandidates(-1, 1, costs)).ParamName);
      Assert.Equal("target", Assert.Throws<ArgumentOutOfRangeException>(() => search.FindCandidates(0, 2, costs)).ParamName);
    }
  }
}
