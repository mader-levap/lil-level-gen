using LostInLimbo.LevelGen.Internal.Generator.Graph.Processors.Obstacler;

namespace LostInLimbo.LevelGen.Tests.Generator.Graph.Processors.Obstacler;

/// <summary>
/// Tests <see cref="MinCutFinder"/>: cut weight, both extreme cuts, parallel edges, contracted
/// terminals, the flow limit and argument validation.
/// </summary>
public sealed class MinCutFinderTests
{
  /// <summary>Flow limit far above every capacity used in these tests.</summary>
  private const long NoLimit = 1_000_000;

  /// <summary>Creates node flags with the given nodes set.</summary>
  /// <param name="nodeCount">Number of nodes.</param>
  /// <param name="nodes">Nodes to flag.</param>
  /// <returns>Flags per node.</returns>
  private static bool[] Flags(int nodeCount, params int[] nodes)
  {
    var flags = new bool[nodeCount];
    foreach (var node in nodes) flags[node] = true;
    return flags;
  }

  /// <summary>Creates the finder of a chain <c>0 - 1 - ... - (nodeCount - 1)</c>.</summary>
  /// <param name="nodeCount">Number of nodes; edge k joins nodes k and k + 1.</param>
  /// <returns>The finder.</returns>
  private static MinCutFinder CreateChain(int nodeCount)
  {
    var from = new int[nodeCount - 1];
    var to = new int[nodeCount - 1];
    for (var edge = 0; edge < from.Length; edge++)
    {
      from[edge] = edge;
      to[edge] = edge + 1;
    }

    return new MinCutFinder(nodeCount, from, to);
  }

  /// <summary>Tests the cut weight and the two extreme minimum cuts.</summary>
  public sealed class CutTests
  {
    [Fact]
    public void Find_OnChain_FlowEqualsSmallestCapacity()
    {
      // Arrange: Chain 0 - 1 - 2 - 3 with a weak middle edge.
      var finder = CreateChain(4);
      long[] capacities = [5, 2, 7];

      // Act: Cut node 0 from node 3.
      var result = finder.Find(capacities, Flags(4, 0), Flags(4, 3), NoLimit);

      // Assert: The weak edge is the cut, seen from both sides.
      Assert.False(result.Limited);
      Assert.Equal(2, result.Flow);
      Assert.Equal(Flags(4, 0, 1), result.SourceSide);
      Assert.Equal(Flags(4, 2, 3), result.SinkSide);
      Assert.Equal([1], finder.CrossingEdges(capacities, result.SourceSide));
      Assert.Equal([1], finder.CrossingEdges(capacities, result.SinkSide));
    }

    [Fact]
    public void Find_WithTwoEqualMinimumCuts_ReportsNearestToEachTerminal()
    {
      // Arrange: Chain 0 - 1 - 2 - 3 whose outer edges are equally weak.
      var finder = CreateChain(4);
      long[] capacities = [2, 5, 2];

      // Act: Cut node 0 from node 3.
      var result = finder.Find(capacities, Flags(4, 0), Flags(4, 3), NoLimit);

      // Assert: The source side stops at the first weak edge, the sink side at the last.
      Assert.Equal(2, result.Flow);
      Assert.Equal([0], finder.CrossingEdges(capacities, result.SourceSide));
      Assert.Equal([2], finder.CrossingEdges(capacities, result.SinkSide));
    }

    [Fact]
    public void Find_OnCycle_CutsBothWays()
    {
      // Arrange: Cycle 0 - 1 - 2 - 3 - 0 with unit capacities.
      var finder = new MinCutFinder(4, [0, 1, 2, 3], [1, 2, 3, 0]);
      long[] capacities = [1, 1, 1, 1];

      // Act: Cut node 0 from the opposite node 2.
      var result = finder.Find(capacities, Flags(4, 0), Flags(4, 2), NoLimit);

      // Assert: Both ways around the cycle must be cut.
      Assert.Equal(2, result.Flow);
      Assert.Equal(2, finder.CrossingEdges(capacities, result.SinkSide).Count);
    }

    [Fact]
    public void Find_WithParallelEdges_CountsEveryEdgeSeparately()
    {
      // Arrange: Three parallel edges between two nodes, one of them stored reversed.
      var finder = new MinCutFinder(2, [0, 0, 1], [1, 1, 0]);
      long[] capacities = [1, 2, 3];

      // Act: Cut the two nodes apart.
      var result = finder.Find(capacities, Flags(2, 0), Flags(2, 1), NoLimit);

      // Assert: Capacities add up and all three edges form the cut.
      Assert.Equal(6, result.Flow);
      Assert.Equal([0, 1, 2], finder.CrossingEdges(capacities, result.SourceSide));
    }

    [Fact]
    public void Find_WhenFlowMustBeRerouted_FindsMaximum()
    {
      // Arrange: Diamond 0 - {1, 2} - 3 with a cross edge 1 - 2; a greedy path over the cross
      // edge has to be undone to reach the maximum.
      var finder = new MinCutFinder(4, [0, 0, 1, 1, 2], [1, 2, 2, 3, 3]);
      long[] capacities = [1, 1, 1, 1, 1];

      // Act: Cut node 0 from node 3.
      var result = finder.Find(capacities, Flags(4, 0), Flags(4, 3), NoLimit);

      // Assert: Two disjoint paths exist.
      Assert.Equal(2, result.Flow);
    }

    [Fact]
    public void Find_WithZeroCapacityEdge_TreatsItAsMissing()
    {
      // Arrange: Two nodes joined by an edge without capacity.
      var finder = new MinCutFinder(2, [0], [1]);
      long[] capacities = [0];

      // Act: Cut the two nodes apart.
      var result = finder.Find(capacities, Flags(2, 0), Flags(2, 1), NoLimit);

      // Assert: Nothing flows, each side holds only its terminal and no edge is reported.
      Assert.Equal(0, result.Flow);
      Assert.Equal(Flags(2, 0), result.SourceSide);
      Assert.Equal(Flags(2, 1), result.SinkSide);
      Assert.Empty(finder.CrossingEdges(capacities, result.SourceSide));
    }

    [Fact]
    public void Find_WithSelfLoop_IgnoresIt()
    {
      // Arrange: A self-loop on the source next to a real edge.
      var finder = new MinCutFinder(2, [0, 0], [0, 1]);
      long[] capacities = [9, 3];

      // Act: Cut the two nodes apart.
      var result = finder.Find(capacities, Flags(2, 0), Flags(2, 1), NoLimit);

      // Assert: Only the real edge counts.
      Assert.Equal(3, result.Flow);
      Assert.Equal([1], finder.CrossingEdges(capacities, result.SourceSide));
    }

    [Fact]
    public void Find_CalledTwice_ReturnsSameResult()
    {
      // Arrange: Cycle with mixed capacities.
      var finder = new MinCutFinder(4, [0, 1, 2, 3], [1, 2, 3, 0]);
      long[] capacities = [3, 1, 2, 4];

      // Act: Run the same query twice.
      var first = finder.Find(capacities, Flags(4, 0), Flags(4, 2), NoLimit);
      var second = finder.Find(capacities, Flags(4, 0), Flags(4, 2), NoLimit);

      // Assert: The finder keeps no state between calls.
      Assert.Equal(first.Flow, second.Flow);
      Assert.Equal(first.SourceSide, second.SourceSide);
      Assert.Equal(first.SinkSide, second.SinkSide);
    }
  }

  /// <summary>Tests several sources or sinks acting as one contracted terminal.</summary>
  public sealed class ContractionTests
  {
    [Fact]
    public void Find_WithSeveralSinks_CutsInFrontOfAllOfThem()
    {
      // Arrange: Chain 0 - 1 - 2 - 3 with a weak middle edge.
      var finder = CreateChain(4);
      long[] capacities = [3, 1, 3];

      // Act: Once with the weak edge in front of the sinks, once with it swallowed by them.
      var behindWeak = finder.Find(capacities, Flags(4, 0), Flags(4, 2, 3), NoLimit);
      var swallowed = finder.Find(capacities, Flags(4, 0), Flags(4, 1, 2, 3), NoLimit);

      // Assert: A swallowed edge can no longer be the cut.
      Assert.Equal(1, behindWeak.Flow);
      Assert.Equal(3, swallowed.Flow);
      Assert.Equal([0], finder.CrossingEdges(capacities, swallowed.SinkSide));
    }

    [Fact]
    public void Find_WithSeveralSources_CutsBehindAllOfThem()
    {
      // Arrange: Chain 0 - 1 - 2 - 3 with a weak first edge.
      var finder = CreateChain(4);
      long[] capacities = [1, 3, 4];

      // Act: Nodes 0 and 1 are both sources.
      var result = finder.Find(capacities, Flags(4, 0, 1), Flags(4, 3), NoLimit);

      // Assert: The weak edge lies inside the source set, so the next weakest is cut.
      Assert.Equal(3, result.Flow);
      Assert.Equal([1], finder.CrossingEdges(capacities, result.SourceSide));
    }
  }

  /// <summary>Tests the flow limit that gives up on uncuttable connections.</summary>
  public sealed class LimitTests
  {
    [Fact]
    public void Find_WhenFlowReachesLimit_ReportsLimited()
    {
      // Arrange: Chain whose every edge is stronger than the limit.
      var finder = CreateChain(3);
      long[] capacities = [100, 100];

      // Act: Search with a limit below the real cut weight.
      var result = finder.Find(capacities, Flags(3, 0), Flags(3, 2), flowLimit: 50);

      // Assert: The search stops and reports no usable cut.
      Assert.True(result.Limited);
      Assert.True(result.Flow >= 50);
    }

    [Fact]
    public void Find_WhenFlowStaysBelowLimit_IsNotLimited()
    {
      // Arrange: Chain with a cut weight just below the limit.
      var finder = CreateChain(3);
      long[] capacities = [100, 49];

      // Act: Search with a limit above the real cut weight.
      var result = finder.Find(capacities, Flags(3, 0), Flags(3, 2), flowLimit: 50);

      // Assert: The cut is reported normally.
      Assert.False(result.Limited);
      Assert.Equal(49, result.Flow);
    }
  }

  /// <summary>Tests argument validation.</summary>
  public sealed class ValidationTests
  {
    [Fact]
    public void Constructor_WithNullEndpoints_ThrowsArgumentNullException()
    {
      // Act/Assert: Each missing array is rejected naming the parameter.
      Assert.Equal("edgeFrom", Assert.Throws<ArgumentNullException>(() => new MinCutFinder(1, null!, [])).ParamName);
      Assert.Equal("edgeTo", Assert.Throws<ArgumentNullException>(() => new MinCutFinder(1, [], null!)).ParamName);
    }

    [Fact]
    public void Constructor_WithNegativeNodeCount_ThrowsArgumentOutOfRangeException()
    {
      // Act/Assert: A negative node count is rejected.
      var exception = Assert.Throws<ArgumentOutOfRangeException>(() => new MinCutFinder(-1, [], []));
      Assert.Equal("nodeCount", exception.ParamName);
    }

    [Fact]
    public void Constructor_WithMismatchedEndpointArrays_ThrowsArgumentException()
    {
      // Act/Assert: Every edge needs both endpoints.
      Assert.Throws<ArgumentException>(() => new MinCutFinder(2, [0, 1], [1]));
    }

    [Theory]
    [InlineData(2, 0)]
    [InlineData(0, -1)]
    public void Constructor_WithUnknownEndpoint_ThrowsArgumentOutOfRangeException(int from, int to)
    {
      // Act/Assert: An endpoint outside the node range is rejected.
      Assert.Throws<ArgumentOutOfRangeException>(() => new MinCutFinder(2, [from], [to]));
    }

    [Fact]
    public void Find_WithNullArguments_ThrowsArgumentNullException()
    {
      // Arrange: Smallest valid graph.
      var finder = CreateChain(2);

      // Act/Assert: Each missing array is rejected naming the parameter.
      Assert.Equal("capacities", Assert.Throws<ArgumentNullException>(
        () => finder.Find(null!, Flags(2, 0), Flags(2, 1), NoLimit)).ParamName);
      Assert.Equal("sources", Assert.Throws<ArgumentNullException>(
        () => finder.Find([1], null!, Flags(2, 1), NoLimit)).ParamName);
      Assert.Equal("sinks", Assert.Throws<ArgumentNullException>(
        () => finder.Find([1], Flags(2, 0), null!, NoLimit)).ParamName);
    }

    [Fact]
    public void Find_WithWrongArrayLengths_ThrowsArgumentException()
    {
      // Arrange: Smallest valid graph.
      var finder = CreateChain(2);

      // Act/Assert: Capacities are per edge, terminal flags per node.
      Assert.Throws<ArgumentException>(() => finder.Find([1, 1], Flags(2, 0), Flags(2, 1), NoLimit));
      Assert.Throws<ArgumentException>(() => finder.Find([1], Flags(3, 0), Flags(2, 1), NoLimit));
      Assert.Throws<ArgumentException>(() => finder.Find([1], Flags(2, 0), Flags(3, 1), NoLimit));
    }

    [Fact]
    public void Find_WithNegativeCapacity_ThrowsArgumentException()
    {
      // Arrange: Smallest valid graph.
      var finder = CreateChain(2);

      // Act/Assert: A negative capacity is rejected.
      var exception = Assert.Throws<ArgumentException>(() => finder.Find([-1], Flags(2, 0), Flags(2, 1), NoLimit));
      Assert.Equal("capacities", exception.ParamName);
    }

    [Fact]
    public void Find_WithoutSourceOrSink_ThrowsArgumentException()
    {
      // Arrange: Smallest valid graph.
      var finder = CreateChain(2);

      // Act/Assert: Both terminal sets must be non-empty.
      Assert.Equal("sources", Assert.Throws<ArgumentException>(
        () => finder.Find([1], Flags(2), Flags(2, 1), NoLimit)).ParamName);
      Assert.Equal("sinks", Assert.Throws<ArgumentException>(
        () => finder.Find([1], Flags(2, 0), Flags(2), NoLimit)).ParamName);
    }

    [Fact]
    public void Find_WithNodeBothSourceAndSink_ThrowsArgumentException()
    {
      // Arrange: Smallest valid graph.
      var finder = CreateChain(2);

      // Act/Assert: The terminal sets must not overlap.
      Assert.Throws<ArgumentException>(() => finder.Find([1], Flags(2, 0, 1), Flags(2, 1), NoLimit));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Find_WithNonPositiveLimit_ThrowsArgumentOutOfRangeException(long limit)
    {
      // Arrange: Smallest valid graph.
      var finder = CreateChain(2);

      // Act/Assert: The limit must be positive.
      var exception = Assert.Throws<ArgumentOutOfRangeException>(() => finder.Find([1], Flags(2, 0), Flags(2, 1), limit));
      Assert.Equal("flowLimit", exception.ParamName);
    }
  }
}
