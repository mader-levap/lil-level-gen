using LostInLimbo.LevelGen.Data.Config;
using LostInLimbo.LevelGen.Data.Config.Graph;
using LostInLimbo.LevelGen.Data.Prepass;
using LostInLimbo.LevelGen.Data.Result.Graph;
using LostInLimbo.LevelGen.Internal.Generator.Graph.Builders;
using LostInLimbo.LevelGen.Internal.Generator.Graph.Validator;

namespace LostInLimbo.LevelGen.Tests.Generator.Graph;

/// <summary>
/// Shared helpers for graph tests: a factory-driven build runner, graph queries and
/// reusable assertions over <see cref="LilGraph"/> results.
/// </summary>
/// <remarks>
/// Not a test class itself - xUnit only collects types that contain test methods.
/// Assertions here must stay builder-neutral: builder-specific expectations belong in the
/// individual test classes (see e.g. <c>SimpleGraphBuilderTests.MaxDegree</c>).
/// </remarks>
internal static class GraphTestSupport
{
  /// <summary>Default graph sub-seed used by tests unless a test needs another one.</summary>
  public const long DefaultSeedGraph = 42;

  /// <summary>Relative size used by hand-built test nodes; only satisfies <see cref="LilNode.RelSize"/>.</summary>
  public const int DefaultNodeRelSize = 1;

  //
  // Graph construction
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
  /// <param name="group">Group where this node belongs.</param>
  /// <param name="pois">POIs contained in the node; none when omitted.</param>
  /// <returns>A node with <see cref="DefaultNodeRelSize"/> relative size.</returns>
  public static LilNode Node(int id, string? group, params LilPoi[] pois) => new()
  {
    Id = Id(id),
    RelSize = DefaultNodeRelSize,
    Group = group,
    Pois = [.. pois],
  };

  /// <summary>Creates a POI with the given identifier and type.</summary>
  /// <param name="id">Small integer identifier of the POI.</param>
  /// <param name="type">Type of the POI.</param>
  /// <returns>A POI without a name.</returns>
  public static LilPoi Poi(int id, EnPoiType type, string? name = null) => new() {
    Id = Id(id),
    Type = type,
    Name = name,
  };

  /// <summary>Creates an edge; defaults to an open bidirectional passage.</summary>
  /// <param name="id">Small integer identifier of the edge.</param>
  /// <param name="from">Small integer identifier of the source node.</param>
  /// <param name="to">Small integer identifier of the destination node.</param>
  /// <param name="direction">Passage direction; open bidirectional unless set otherwise.</param>
  /// <param name="conditions">Passage requirements; open passage when null or empty.</param>
  /// <returns>An edge with the given shape.</returns>
  public static LilEdge Edge(
    int id,
    int from,
    int to,
    EnPassageDirection direction = EnPassageDirection.Bidirectional,
    LilCondition[]? conditions = null) => new()
  {
    Id = Id(id),
    From = Id(from),
    To = Id(to),
    Direction = direction,
    Conditions = conditions is null ? new List<LilCondition>() : [.. conditions],
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
  // Build runner
  //

  /// <summary>
  /// Runs a graph builder through the factory exactly like production dispatch does and
  /// reports the outcome.
  /// </summary>
  /// <remarks>
  /// The config's <see cref="CnfGraph.Builder"/> is forced to match <paramref name="builder"/>,
  /// so a test can never accidentally pair one builder with another builder's config section.
  /// </remarks>
  /// <param name="builder">Which builder the factory should create.</param>
  /// <param name="graphConfig">Graph config to use; defaults to a plain config for <paramref name="builder"/>.</param>
  /// <param name="seedGraph">Graph sub-seed to use.</param>
  /// <returns>The reported build success and the filled target graph.</returns>
  public static (bool Success, LilGraph Graph) Build(
    EnGraphBuilder builder,
    CnfGraph? graphConfig = null,
    long seedGraph = DefaultSeedGraph)
  {
    // Arrange: Fresh builder from the factory, config and an empty target graph.
    var graphBuilder = GraphBuilderFactory.Create(builder);
    var config = CnfLevelGeneration.Default with
    {
      Graph = (graphConfig ?? new CnfGraph()) with { Builder = builder },
    };
    var prepData = new PrepData { SeedGraph = seedGraph };
    var graph = new LilGraph();

    // Act: Hand over the inputs, then build.
    var success = graphBuilder.Run(config, prepData, graph);

    // Assert-adjacent: Outcome and shape assertions live in the tests.
    return (success, graph);
  }

  //
  // Queries
  //

  /// <summary>
  /// Collects all POIs of the given type from every node of the graph.
  /// </summary>
  /// <param name="graph">Graph to search.</param>
  /// <param name="type">POI type to look for.</param>
  /// <returns>Matching POIs in node order; empty list when the graph has none.</returns>
  public static List<LilPoi> GetPois(LilGraph graph, EnPoiType type)
  {
    return graph.Nodes.SelectMany(node => node.Pois).Where(poi => poi.Type == type).ToList();
  }

  /// <summary>
  /// Counts how many edges touch each node, keyed by node id.
  /// </summary>
  /// <param name="graph">Graph to inspect.</param>
  /// <returns>Relationship count per node; zero for node-less graphs.</returns>
  /// <exception cref="KeyNotFoundException">An edge references a node that does not exist.</exception>
  public static Dictionary<Guid, int> GetDegrees(LilGraph graph)
  {
    var degrees = graph.Nodes.ToDictionary(node => node.Id, _ => 0);
    foreach (var edge in graph.Edges)
    {
      degrees[edge.From]++;
      degrees[edge.To]++;
    }

    return degrees;
  }

  /// <summary>
  /// Reads the current size assignment of a graph in a stable order.
  /// </summary>
  /// <param name="graph">Graph to read.</param>
  /// <returns>Identifier and size pairs sorted by identifier.</returns>
  public static List<(Guid Id, int RelSize)> GetAssignments(LilGraph graph)
  {
    return graph.Nodes
      .OrderBy(node => node.Id)
      .Select(node => (node.Id, node.RelSize))
      .ToList();
  }

  //
  // Assertions
  //

  /// <summary>
  /// Asserts structural invariants of the edge list: endpoints exist and differ, and -
  /// unless <paramref name="allowDuplicates"/> is set - no node pair is connected twice.
  /// Direction and conditions are builder expectations, see <see cref="AssertOpenPassages"/>.
  /// </summary>
  /// <param name="graph">Graph to check.</param>
  /// <param name="allowDuplicates">
  /// When true, multiple passages between the same node pair are accepted (e.g. the
  /// parallel passages of <c>LinearGraphBuilder</c>); otherwise the pair must be unique.
  /// </param>
  public static void AssertValidEdges(LilGraph graph, bool allowDuplicates = false)
  {
    var nodeIds = graph.Nodes.Select(node => node.Id).ToHashSet();
    var pairs = new HashSet<(Guid From, Guid To)>();
    foreach (var edge in graph.Edges)
    {
      // Assert: Endpoints reference two different existing nodes.
      Assert.NotEqual(edge.From, edge.To);
      Assert.Contains(edge.From, nodeIds);
      Assert.Contains(edge.To, nodeIds);

      if (allowDuplicates) continue;

      var low = edge.From.CompareTo(edge.To) < 0 ? edge.From : edge.To;
      var high = edge.From.CompareTo(edge.To) < 0 ? edge.To : edge.From;
      // Assert: The unordered node pair appears only once.
      Assert.True(pairs.Add((low, high)), $"Duplicate edge between {low} and {high}.");
    }
  }

  /// <summary>
  /// Asserts that every passage is bidirectional and unconditional (open).
  /// </summary>
  /// <param name="graph">Graph to check.</param>
  /// <remarks>
  /// Builders without obstacles must satisfy this. Once a builder emits one-way edges or
  /// conditions (e.g. Linear with complexity), its tests call <see cref="AssertValidEdges"/>
  /// only and check passages separately.
  /// </remarks>
  public static void AssertOpenPassages(LilGraph graph)
  {
    foreach (var edge in graph.Edges)
    {
      // Assert: This passage works in both directions and has no requirements.
      Assert.Equal(EnPassageDirection.Bidirectional, edge.Direction);
      Assert.Empty(edge.Conditions);
    }
  }

  /// <summary>
  /// Asserts that no node touches more than the given amount of relationships.
  /// </summary>
  /// <param name="graph">Graph to check.</param>
  /// <param name="maxDegree">Allowed relationships per node, inclusive.</param>
  public static void AssertDegreeCap(LilGraph graph, int maxDegree)
  {
    foreach (var (nodeId, degree) in GetDegrees(graph))
    {
      Assert.True(degree <= maxDegree, $"Node {nodeId} has {degree} relationships, cap is {maxDegree}.");
    }
  }

  //

  /// <summary>
  /// Asserts that two builds produced structurally identical graphs, in order.
  /// </summary>
  /// <param name="expected">Reference graph.</param>
  /// <param name="actual">Graph under test.</param>
  /// <remarks>
  /// Comparison is order-sensitive on purpose: deterministic builders construct nodes and
  /// edges in a stable order, so any reordering is a determinism break worth failing on.
  /// </remarks>
  public static void AssertEqualGraphs(LilGraph expected, LilGraph actual)
  {
    // Assert: Two given graphs are same.
    Assert.Equal(expected.Nodes.Count, actual.Nodes.Count);
    for (var i = 0; i < expected.Nodes.Count; i++)
    {
      AssertEqualNode(expected.Nodes[i], actual.Nodes[i]);
    }

    Assert.Equal(expected.Edges.Count, actual.Edges.Count);
    for (var i = 0; i < expected.Edges.Count; i++)
    {
      AssertEqualEdge(expected.Edges[i], actual.Edges[i]);
    }
  }

  /// <summary>
  /// Asserts two provided nodes are identical.
  /// </summary>
  /// <param name="expected">Reference node.</param>
  /// <param name="actual">Node under test.</param>
  private static void AssertEqualNode(LilNode expected, LilNode actual)
  {
    // Assert: Same node properties and POI contents in the same order.
    Assert.Equal(expected.Id, actual.Id);
    Assert.Equal(expected.RelSize, actual.RelSize);
    Assert.Equal(expected.Group, actual.Group);

    Assert.Equal(expected.Pois.Count, actual.Pois.Count);
    for (var i = 0; i < expected.Pois.Count; i++)
    {
      AssertEqualPoi(expected.Pois[i], actual.Pois[i]);
    }
  }

  /// <summary>
  /// Asserts two provided PoIs are identical.
  /// </summary>
  /// <param name="expected">Reference PoI.</param>
  /// <param name="actual">PoI under test.</param>
  private static void AssertEqualPoi(LilPoi expected, LilPoi actual)
  {
    // Assert: Same PoI properties.
    Assert.Equal(expected.Id, actual.Id);
    Assert.Equal(expected.Type, actual.Type);
    Assert.Equal(expected.Name, actual.Name);
  }

  /// <summary>
  /// Asserts two provided relationships are identical.
  /// </summary>
  /// <param name="expected">Reference relationship.</param>
  /// <param name="actual">Relationship under test.</param>
  private static void AssertEqualEdge(LilEdge expected, LilEdge actual)
  {
    // Assert: Same edge properties and conditions in the same order.
    Assert.Equal(expected.Id, actual.Id);
    Assert.Equal(expected.From, actual.From);
    Assert.Equal(expected.To, actual.To);
    Assert.Equal(expected.Direction, actual.Direction);
    Assert.Equal(expected.Chokepoint, actual.Chokepoint);
    Assert.Equal(expected.Secret, actual.Secret);

    Assert.Equal(expected.Conditions.Count, actual.Conditions.Count);
    for (var i = 0; i < expected.Conditions.Count; i++)
    {
      AssertEqualCondition(expected.Conditions[i], actual.Conditions[i]);
    }  
  }

  /// <summary>
  /// Asserts two provided conditions are identical.
  /// </summary>
  /// <param name="expected">Reference condition.</param>
  /// <param name="actual">Condition under test.</param>
  private static void AssertEqualCondition(LilCondition expected, LilCondition actual)
  {
    // Assert: Same condition properties.
    Assert.Equal(expected.Type, actual.Type);
    Assert.Equal(expected.RequiresPoi, actual.RequiresPoi);
  }

  //

  /// <summary>
  /// Asserts that the graph passes the beatability simulation - start reaches exit,
  /// every node is reachable and no condition can dangle.
  /// </summary>
  /// <param name="graph">Graph to check.</param>
  public static void AssertBeatable(LilGraph graph, string failMsg = "")
  {
    var result = new GraphValidator(graph).Validate();

    // Assert: The validation reports success; otherwise its reason explains the failure.
    if (failMsg != "") failMsg += "\n";
    Assert.True(result.Success, failMsg + result.FailureReason);
  }
}
