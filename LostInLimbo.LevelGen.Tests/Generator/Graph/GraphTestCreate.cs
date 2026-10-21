using LostInLimbo.LevelGen.Data.Result.Graph;

namespace LostInLimbo.LevelGen.Tests.Generator.Graph;

/// <summary>
/// Shared construction fixtures for graph tests: deterministic identifier and element
/// primitives plus ready-made shapes.
/// </summary>
/// <remarks>
/// Not a test class itself - xUnit only collects types that contain test methods.
/// Every identifier scheme here is fixed and readable (see <see cref="Id"/>), so
/// hand-built graphs stay recognizable in failure messages.
/// </remarks>
internal static class GraphTestCreate
{
  /// <summary>Relative size used by hand-built test nodes; only satisfies <see cref="LilNode.RelSize"/>.</summary>
  public const int DefaultNodeRelSize = 1;

  //
  // Identifier and element primitives
  //

  /// <summary>
  /// Derives a stable, readable test identifier from a small integer.
  /// </summary>
  /// <remarks>Produces <c>00000000-0000-0000-0000-000000000042</c> for <paramref name="value"/> 42,
  /// so hand-built graphs stay readable in failure messages.</remarks>
  /// <param name="value">Small integer encoding the identifier's identity.</param>
  /// <returns>A deterministic identifier derived from <paramref name="value"/>.</returns>
  public static Guid Id(int value) => Guid.Parse($"00000000-0000-0000-0000-{value:D12}");

  /// <summary>Creates a node with the given identifier and POIs.</summary>
  /// <param name="id">Small integer identifier of the node.</param>
  /// <param name="pois">POIs contained in the node; none when omitted.</param>
  /// <returns>A node with <see cref="DefaultNodeRelSize"/> relative size.</returns>
  public static LilNode Node(int id, params LilPoi[] pois) => new()
  {
    Id = Id(id),
    RelSize = DefaultNodeRelSize,
    Pois = [.. pois],
  };

  /// <summary>Creates a node with the given identifier and POIs.</summary>
  /// <param name="id">Small integer identifier of the node.</param>
  /// <param name="style">Styling of this node.</param>
  /// <param name="pois">POIs contained in the node; none when omitted.</param>
  /// <returns>A node with <see cref="DefaultNodeRelSize"/> relative size.</returns>
  public static LilNode Node(int id, LilStyle style, params LilPoi[] pois) => new()
  {
    Id = Id(id),
    RelSize = DefaultNodeRelSize,
    Style = style,
    Pois = [.. pois],
  };

  /// <summary>Creates a POI with the given identifier and type.</summary>
  /// <param name="id">Small integer identifier of the POI.</param>
  /// <param name="type">Type of the POI.</param>
  /// <returns>A POI without a name.</returns>
  public static LilPoi Poi(int id, EnPoiType type, string? name = null) => new()
  {
    Id = Id(id),
    Type = type,
    Name = name,
  };

  /// <summary>Creates an edge with an explicit identifier; defaults to an open bidirectional passage.</summary>
  /// <param name="id">Small integer identifier of the edge.</param>
  /// <param name="from">Small integer identifier of the source node.</param>
  /// <param name="to">Small integer identifier of the destination node.</param>
  /// <param name="direction">Passage direction; open bidirectional unless set otherwise.</param>
  /// <param name="conditions">Passage requirements; open passage when null or empty.</param>
  /// <param name="secret">Whether the passage is secret.</param>
  /// <param name="chokepoint">Whether the passage represents a single physical opening.</param>
  /// <returns>An edge with the given shape.</returns>
  public static LilEdge Edge(
    int id,
    int from,
    int to,
    EnPassageDirection direction = EnPassageDirection.Bidirectional,
    LilCondition[]? conditions = null,
    bool secret = false,
    bool chokepoint = false) => new()
  {
    Id = Id(id),
    From = Id(from),
    To = Id(to),
    Direction = direction,
    Conditions = conditions is null ? new List<LilCondition>() : [.. conditions],
    Secret = secret,
    Chokepoint = chokepoint,
  };

  /// <summary>Creates a solution requirement pointing at the given POI.</summary>
  /// <param name="poiId">Small integer identifier of the required POI.</param>
  /// <returns>A condition of type <see cref="EnConditionType.RequiresSolution"/>.</returns>
  public static LilCondition Condition(int poiId) => new()
  {
    Type = EnConditionType.RequiresSolution,
    RequiresPoi = Id(poiId),
  };

  //
  // Graph shapes
  //

  /// <summary>
  /// Creates a chain graph: node 0 - node 1 - ... with deterministic identifiers.
  /// </summary>
  /// <param name="nodeCount">Amount of nodes; at least 1.</param>
  /// <param name="secretEdges">When true, every relationship is a secret passage.</param>
  /// <param name="oneWayEdges">When true, every relationship only allows traversal forward.</param>
  /// <param name="poiNodeIndex">Node that receives POIs, or -1 for none.</param>
  /// <param name="poiCount">Amount of POIs to place on <paramref name="poiNodeIndex"/>.</param>
  /// <returns>The constructed graph with <c>nodeCount - 1</c> edges.</returns>
  public static LilGraph CreateChainGraph(
    int nodeCount,
    bool secretEdges = false,
    bool oneWayEdges = false,
    int poiNodeIndex = -1,
    int poiCount = 0)
  {
    var graph = CreateNodes(nodeCount, poiNodeIndex, poiCount);
    for (var i = 0; i + 1 < nodeCount; i++)
    {
      graph.Edges.Add(CreateEdge(
        i,
        i + 1,
        direction: oneWayEdges ? EnPassageDirection.OneWay : EnPassageDirection.Bidirectional,
        secret: secretEdges));
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
  public static LilGraph CreateStarGraph(int nodeCount, int poiNodeIndex = -1, int poiCount = 0)
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
  public static LilGraph CreateCycleGraph(int nodeCount)
  {
    var graph = CreateNodes(nodeCount, poiNodeIndex: -1, poiCount: 0);
    for (var i = 0; i < nodeCount; i++)
    {
      graph.Edges.Add(CreateEdge(i, (i + 1) % nodeCount));
    }

    return graph;
  }

  /// <summary>
  /// Creates a chain whose relationships cycle through the three restricted kinds:
  /// secret, one-way and conditional.
  /// </summary>
  /// <param name="nodeCount">Amount of nodes; at least 1.</param>
  /// <returns>The constructed graph with <c>nodeCount - 1</c> restricted edges.</returns>
  public static LilGraph CreateRestrictedChainGraph(int nodeCount)
  {
    var graph = CreateNodes(nodeCount, poiNodeIndex: -1, poiCount: 0);
    for (var i = 0; i + 1 < nodeCount; i++)
    {
      var kind = i % 3;
      graph.Edges.Add(kind switch
      {
        0 => CreateEdge(i, i + 1, secret: true),
        1 => CreateEdge(i, i + 1, direction: EnPassageDirection.OneWay),
        _ => CreateEdge(i, i + 1, conditions: [Condition(i)]),
      });
    }

    return graph;
  }

  /// <summary>
  /// Creates an edge between two node indices with a pair-derived deterministic identifier.
  /// </summary>
  /// <param name="fromIndex">Source node index.</param>
  /// <param name="toIndex">Target node index; must differ.</param>
  /// <param name="direction">Passage direction; open bidirectional unless set otherwise.</param>
  /// <param name="secret">Whether the passage is secret.</param>
  /// <param name="conditions">Passage requirements; open passage when null or empty.</param>
  /// <returns>The constructed edge.</returns>
  public static LilEdge CreateEdge(
    int fromIndex,
    int toIndex,
    EnPassageDirection direction = EnPassageDirection.Bidirectional,
    bool secret = false,
    LilCondition[]? conditions = null)
  {
    // Low/high ordering keeps identifiers unique per unordered pair.
    var low = Math.Min(fromIndex, toIndex);
    var high = Math.Max(fromIndex, toIndex);
    return new LilEdge
    {
      Id = Guid.Parse($"00000000-0000-0000-0001-{low:D6}{high:D6}"),
      From = Id(fromIndex),
      To = Id(toIndex),
      Direction = direction,
      Secret = secret,
      Conditions = conditions is null ? new List<LilCondition>() : [.. conditions],
    };
  }

  /// <summary>
  /// Creates a deterministic POI identifier unique per node and slot.
  /// </summary>
  /// <param name="nodeIndex">Index of the owning node.</param>
  /// <param name="slot">POI slot on that node.</param>
  /// <returns>A distinct, non-empty identifier; the POI type is Start.</returns>
  public static LilPoi CreatePoi(int nodeIndex, int slot)
  {
    return new LilPoi
    {
      Id = Guid.Parse($"00000000-0000-0000-0002-{nodeIndex:D6}{slot:D6}"),
      Type = EnPoiType.Start,
    };
  }

  //
  // Internals
  //

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
      var node = new LilNode { Id = Id(i), RelSize = DefaultNodeRelSize };
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
}
