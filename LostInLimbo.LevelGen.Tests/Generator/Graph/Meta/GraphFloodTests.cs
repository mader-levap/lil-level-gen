using LostInLimbo.LevelGen.Data.Result.Graph;
using LostInLimbo.LevelGen.Internal.Generator.Graph.Meta;

namespace LostInLimbo.LevelGen.Tests.Generator.Graph.Meta;

/// <summary>
/// Tests <see cref="GraphFlood"/> argument validation and its edge filter. Unfiltered flood
/// behavior is covered through <c>GraphMetadataTests</c> and <c>LevelGraphReachTests</c>.
/// </summary>
public sealed class GraphFloodTests
{
  /// <summary>Shared empty POI set: nothing initially held, nothing collectible.</summary>
  private static readonly HashSet<Guid> NoPois = [];

  /// <summary>Tests constructor argument validation.</summary>
  public sealed class ConstructorTests
  {
    [Fact]
    public void Constructor_WithNullGraph_ThrowsArgumentNullException()
    {
      // Act/Assert: A missing graph is rejected naming the parameter.
      var exception = Assert.Throws<ArgumentNullException>(() => new GraphFlood(null!));
      Assert.Equal("graph", exception.ParamName);
    }
  }

  /// <summary>Tests the optional edge filter that hides passages from the flood.</summary>
  public sealed class EdgeFilterTests
  {
    /// <summary>
    /// Creates the graph <c>1 - 2 - 3</c> plus a secret passage <c>1 - 4</c> and a one-way
    /// passage <c>3 -&gt; 5</c>; all passages are open.
    /// </summary>
    /// <returns>Graph with five nodes and four edges.</returns>
    private static LilGraph CreateMixedGraph()
    {
      var graph = new LilGraph();
      for (var id = 1; id <= 5; id++) graph.Nodes.Add(GraphTestCreate.Node(id));
      graph.Edges.Add(GraphTestCreate.Edge(101, 1, 2));
      graph.Edges.Add(GraphTestCreate.Edge(102, 2, 3));
      graph.Edges.Add(GraphTestCreate.Edge(103, 1, 4, secret: true));
      graph.Edges.Add(GraphTestCreate.Edge(104, 3, 5, direction: EnPassageDirection.OneWay));
      return graph;
    }

    /// <summary>Floods from node 1 holding and collecting nothing.</summary>
    /// <param name="flood">Flood to run.</param>
    /// <returns>Small integer identifiers of the reached nodes, ascending.</returns>
    private static List<int> ReachFromFirst(GraphFlood flood)
    {
      var (reached, _) = flood.Run(GraphTestCreate.Id(1), NoPois, NoPois, blockedPoi: null);
      var ids = new List<int>();
      for (var id = 1; id <= 5; id++)
      {
        if (reached.Contains(GraphTestCreate.Id(id))) ids.Add(id);
      }

      return ids;
    }

    [Fact]
    public void Run_WithoutFilter_ReachesOverEveryPassage()
    {
      // Arrange: Flood without a filter (the default).
      var flood = new GraphFlood(CreateMixedGraph());

      // Act/Assert: Secret and one-way passages are walked like ordinary ones.
      Assert.Equal([1, 2, 3, 4, 5], ReachFromFirst(flood));
    }

    [Fact]
    public void Run_WithNullFilter_BehavesLikeNoFilter()
    {
      // Arrange: Flood with an explicit null filter.
      var flood = new GraphFlood(CreateMixedGraph(), edgeFilter: null);

      // Act/Assert: Null keeps every passage.
      Assert.Equal([1, 2, 3, 4, 5], ReachFromFirst(flood));
    }

    [Fact]
    public void Run_WithFilterRejectingSecretPassages_DoesNotCrossThem()
    {
      // Arrange: Flood that ignores secret passages.
      var flood = new GraphFlood(CreateMixedGraph(), edge => !edge.Secret);

      // Act/Assert: The node behind the secret passage stays unreached.
      Assert.Equal([1, 2, 3, 5], ReachFromFirst(flood));
    }

    [Fact]
    public void Run_WithFilterRejectingOneWayPassages_DoesNotCrossThem()
    {
      // Arrange: Flood that ignores one-way passages, even in their forward direction.
      var flood = new GraphFlood(CreateMixedGraph(), edge => edge.Direction == EnPassageDirection.Bidirectional);

      // Act/Assert: The node behind the one-way passage stays unreached.
      Assert.Equal([1, 2, 3, 4], ReachFromFirst(flood));
    }

    [Fact]
    public void Run_WithFilterRejectingEverything_ReachesOnlyStart()
    {
      // Arrange: Flood without any usable passage.
      var flood = new GraphFlood(CreateMixedGraph(), _ => false);

      // Act/Assert: Only the start node is reached.
      Assert.Equal([1], ReachFromFirst(flood));
    }

    [Fact]
    public void Run_WithFilter_StillHonoursConditionsAndBlockedPoi()
    {
      // Arrange: Chain 1 - 2 - 3 where the passage 2 - 3 needs a solution lying in node 2,
      // plus a secret, unconditioned shortcut 1 - 3 that the filter hides.
      const int solutionId = 900;
      var graph = new LilGraph();
      graph.Nodes.Add(GraphTestCreate.Node(1));
      graph.Nodes.Add(GraphTestCreate.Node(2, GraphTestCreate.Poi(solutionId, EnPoiType.Solution)));
      graph.Nodes.Add(GraphTestCreate.Node(3));
      graph.Edges.Add(GraphTestCreate.Edge(101, 1, 2));
      graph.Edges.Add(GraphTestCreate.Edge(102, 2, 3, conditions: [GraphTestCreate.Condition(solutionId)]));
      graph.Edges.Add(GraphTestCreate.Edge(103, 1, 3, secret: true));
      var flood = new GraphFlood(graph, edge => !edge.Secret);
      var collectible = new HashSet<Guid> { GraphTestCreate.Id(solutionId) };

      // Act: Flood once normally and once with the solution blocked.
      var (reached, _) = flood.Run(GraphTestCreate.Id(1), NoPois, collectible, blockedPoi: null);
      var (reachedBlocked, _) = flood.Run(GraphTestCreate.Id(1), NoPois, collectible, GraphTestCreate.Id(solutionId));

      // Assert: With the solution the conditioned passage opens; blocked, node 3 is cut off
      // because the secret shortcut does not exist for this flood.
      Assert.Contains(GraphTestCreate.Id(3), reached);
      Assert.DoesNotContain(GraphTestCreate.Id(3), reachedBlocked);
      Assert.Contains(GraphTestCreate.Id(2), reachedBlocked);
    }

    [Fact]
    public void Constructor_WithFilter_EvaluatesFilterOncePerUsableEdge()
    {
      // Arrange: Counting filter over a graph with four usable passages.
      var calls = 0;

      // Act: Build the flood and run it twice.
      var flood = new GraphFlood(CreateMixedGraph(), _ =>
      {
        calls++;
        return true;
      });
      ReachFromFirst(flood);
      ReachFromFirst(flood);

      // Assert: The filter ran at construction only, once per passage.
      Assert.Equal(4, calls);
    }
  }
}
