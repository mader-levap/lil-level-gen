using LostInLimbo.LevelGen.Data.Config;
using LostInLimbo.LevelGen.Data.Config.Graph;
using LostInLimbo.LevelGen.Data.Prepass;
using LostInLimbo.LevelGen.Data.Result.Graph;
using LostInLimbo.LevelGen.Internal.Generator.Graph.Builders;

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
    graphBuilder.Setup(config, prepData, graph);
    var success = graphBuilder.Build();

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

  //
  // Assertions
  //

  /// <summary>
  /// Asserts that every node is reachable from the first node. Ignores conditions.
  /// </summary>
  /// <param name="graph">Graph to check; graphs with at most one node are connected by definition.</param>
  /// <param name="message">Optional failure message.</param>
  public static void AssertConnected(LilGraph graph, string? message = null)
  {
    Assert.True(IsConnected(graph), message ?? "Graph must be connected.");
  }

  /// <summary>
  /// Asserts structural invariants of the edge list: endpoints exist and differ, and no
  /// node pair is connected twice. Direction and conditions are builder expectations, see
  /// <see cref="AssertOpenPassages"/>.
  /// </summary>
  /// <param name="graph">Graph to check.</param>
  public static void AssertValidEdges(LilGraph graph)
  {
    var nodeIds = graph.Nodes.Select(node => node.Id).ToHashSet();
    var pairs = new HashSet<(Guid From, Guid To)>();
    foreach (var edge in graph.Edges)
    {
      // Assert: Endpoints reference two different existing nodes.
      Assert.NotEqual(edge.From, edge.To);
      Assert.Contains(edge.From, nodeIds);
      Assert.Contains(edge.To, nodeIds);

      // Assert: The unordered node pair appears only once.
      var low = edge.From.CompareTo(edge.To) < 0 ? edge.From : edge.To;
      var high = edge.From.CompareTo(edge.To) < 0 ? edge.To : edge.From;
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
    Assert.Equal(expected.Nodes.Count, actual.Nodes.Count);
    for (var i = 0; i < expected.Nodes.Count; i++)
    {
      // Assert: Same node identity, relative size and POI contents in the same order.
      Assert.Equal(expected.Nodes[i].Id, actual.Nodes[i].Id);
      Assert.Equal(expected.Nodes[i].RelSize, actual.Nodes[i].RelSize);
      Assert.Equal(expected.Nodes[i].Pois.Count, actual.Nodes[i].Pois.Count);
      for (var j = 0; j < expected.Nodes[i].Pois.Count; j++)
      {
        Assert.Equal(expected.Nodes[i].Pois[j].Id, actual.Nodes[i].Pois[j].Id);
        Assert.Equal(expected.Nodes[i].Pois[j].Type, actual.Nodes[i].Pois[j].Type);
      }
    }

    Assert.Equal(expected.Edges.Count, actual.Edges.Count);
    for (var i = 0; i < expected.Edges.Count; i++)
    {
      // Assert: Same edge identity, endpoints, direction and conditions in the same order.
      Assert.Equal(expected.Edges[i].Id, actual.Edges[i].Id);
      Assert.Equal(expected.Edges[i].From, actual.Edges[i].From);
      Assert.Equal(expected.Edges[i].To, actual.Edges[i].To);
      Assert.Equal(expected.Edges[i].Direction, actual.Edges[i].Direction);
      Assert.Equal(expected.Edges[i].Conditions.Count, actual.Edges[i].Conditions.Count);
    }
  }

  //
  // Internals
  //

  /// <summary>
  /// <para>Verifies that every node is reachable from the first node.</para>
  /// <para>Note: this specific method ignores conditions.</para>
  /// </summary>
  /// <param name="graph">Graph to check.</param>
  /// <returns>True when every node is reachable from the first node.</returns>
  private static bool IsConnected(LilGraph graph)
  {
    if (graph.Nodes.Count <= 1) return true;

    // Build index-based adjacency: edge endpoints reference existing nodes.
    var indexById = graph.Nodes.Select((node, index) => (node.Id, index))
      .ToDictionary(pair => pair.Id, pair => pair.index);
    var adjacency = new List<int>[graph.Nodes.Count];

    for (var i = 0; i < adjacency.Length; i++)
      adjacency[i] = new List<int>();

    foreach (var edge in graph.Edges)
    {
      var from = indexById[edge.From];
      var to = indexById[edge.To];
      adjacency[from].Add(to);
      adjacency[to].Add(from);
    }

    // Breadth-first search from node 0.
    var visited = new bool[graph.Nodes.Count];
    var queue = new Queue<int>();
    queue.Enqueue(0);
    visited[0] = true;

    while (queue.Count > 0)
    {
      foreach (var neighbor in adjacency[queue.Dequeue()])
      {
        if (visited[neighbor]) continue;
        visited[neighbor] = true;
        queue.Enqueue(neighbor);
      }
    }

    return visited.All(wasVisited => wasVisited);
  }
}
