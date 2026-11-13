using LostInLimbo.LevelGen.Data.Result.Graph;
using LostInLimbo.LevelGen.Internal.Generator.Graph.Meta;

namespace LostInLimbo.LevelGen.Tests.Generator.Graph.Meta;

/// <summary>
/// Tests <see cref="GraphIndex"/>: identifier-sorted visit orders, node indexing and strict
/// endpoint resolution.
/// </summary>
public sealed class GraphIndexTests
{
  /// <summary>Identifiers in ascending order: <c>Low &lt; Mid &lt; High</c>.</summary>
  private static readonly Guid Low = new("00000000-0000-0000-0000-000000000001");
  private static readonly Guid Mid = new("00000000-0000-0000-0000-000000000002");
  private static readonly Guid High = new("00000000-0000-0000-0000-000000000003");

  /// <summary>Creates a node with the given identifier and neutral required values.</summary>
  private static LilNode Node(Guid id) => new() { Id = id, RelSize = 1, MaxEdges = -1 };

  /// <summary>Creates a graph whose nodes are stored out of identifier order: High, Low, Mid.</summary>
  private static LilGraph CreateUnsortedGraph()
  {
    return new LilGraph
    {
      Nodes = [Node(High), Node(Low), Node(Mid)],
      Edges =
      [
        new LilEdge { Id = Mid, From = High, To = Low },
        new LilEdge { Id = High, From = Low, To = Mid },
        new LilEdge { Id = Low, From = Mid, To = High },
      ],
    };
  }

  /// <summary>Tests of the identifier-sorted visit orders.</summary>
  public sealed class VisitOrderTests
  {
    [Fact]
    public void NodeVisitOrder_WithUnsortedNodes_ReturnsIndicesInIdOrder()
    {
      // Act: Order the nodes stored as High, Low, Mid.
      var order = GraphIndex.NodeVisitOrder(CreateUnsortedGraph());

      // Assert: Indices of Low, Mid, High - independent of storage order.
      Assert.Equal([1, 2, 0], order);
    }

    [Fact]
    public void EdgeVisitOrder_WithUnsortedEdges_ReturnsIndicesInIdOrder()
    {
      // Act: Order the edges stored with identifiers Mid, High, Low.
      var order = GraphIndex.EdgeVisitOrder(CreateUnsortedGraph());

      // Assert: Indices of the Low, Mid, High edges.
      Assert.Equal([2, 0, 1], order);
    }

    [Fact]
    public void VisitOrders_WithEmptyGraph_ReturnEmptyLists()
    {
      // Arrange: Graph without nodes and edges.
      var graph = new LilGraph();

      // Act/Assert: Both orders are empty, not null.
      Assert.Empty(GraphIndex.NodeVisitOrder(graph));
      Assert.Empty(GraphIndex.EdgeVisitOrder(graph));
    }
  }

  /// <summary>Tests of node indexing and endpoint resolution.</summary>
  public sealed class IndexTests
  {
    [Fact]
    public void IndexNodesById_MapsEveryIdToItsStorageIndex()
    {
      // Act: Index the nodes stored as High, Low, Mid.
      var indexById = GraphIndex.IndexNodesById(CreateUnsortedGraph());

      // Assert: Every identifier points at its position in the node list.
      Assert.Equal(3, indexById.Count);
      Assert.Equal(0, indexById[High]);
      Assert.Equal(1, indexById[Low]);
      Assert.Equal(2, indexById[Mid]);
    }

    [Fact]
    public void IndexNodesById_WithDuplicateId_ThrowsArgumentException()
    {
      // Arrange: Two nodes sharing an identifier.
      var graph = new LilGraph { Nodes = [Node(Low), Node(Low)] };

      // Act/Assert: The duplicate is rejected.
      Assert.Throws<ArgumentException>(() => GraphIndex.IndexNodesById(graph));
    }

    [Fact]
    public void ResolveEndpoints_WithKnownNodes_ReturnsFromAndToIndices()
    {
      // Arrange: Index of the unsorted graph; its first edge leads High -> Low.
      var graph = CreateUnsortedGraph();
      var indexById = GraphIndex.IndexNodesById(graph);

      // Act: Resolve the first edge.
      var (from, to) = GraphIndex.ResolveEndpoints(indexById, graph.Edges[0]);

      // Assert: Storage indices of High and Low.
      Assert.Equal(0, from);
      Assert.Equal(1, to);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ResolveEndpoints_WithUnknownEndpoint_ThrowsInvalidOperationException(bool unknownFrom)
    {
      // Arrange: An edge with one endpoint outside the graph.
      var indexById = GraphIndex.IndexNodesById(CreateUnsortedGraph());
      var unknown = new Guid("00000000-0000-0000-0000-0000000000ff");
      var edge = unknownFrom
        ? new LilEdge { Id = Low, From = unknown, To = Low }
        : new LilEdge { Id = Low, From = Low, To = unknown };

      // Act/Assert: The missing endpoint is reported by name.
      var exception = Assert.Throws<InvalidOperationException>(() => GraphIndex.ResolveEndpoints(indexById, edge));
      Assert.Contains(unknown.ToString(), exception.Message);
    }
  }
}
