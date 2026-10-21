using LostInLimbo.LevelGen.Data.Result.Graph;
using LostInLimbo.LevelGen.Internal.Generator.Graph.Validator;

namespace LostInLimbo.LevelGen.Tests.Generator.Graph;

/// <summary>
/// Shared graph assertions for tests: structural edge invariants, open passages, degree
/// caps, whole-graph equality and the beatability simulation.
/// </summary>
/// <remarks>
/// Not a test class itself - xUnit only collects types that contain test methods.
/// Assertions here must stay builder-neutral: builder-specific expectations belong in the
/// individual test classes (see e.g. <c>SimpleGraphBuilderTests.MaxDegree</c>).
/// Pairs of this class and its siblings: <see cref="GraphTestCreate"/> builds graphs,
/// <see cref="GraphTestSupport"/> runs builders and queries results, this class asserts
/// outcomes.
/// </remarks>
internal static class GraphTestAssert
{
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
  /// <param name="includeSecretEdges">
  /// When false, secret relationships (appended by the finalisation pass) are not counted -
  /// builder-owned degree caps only cover builder-created passages.
  /// </param>
  public static void AssertDegreeCap(LilGraph graph, int maxDegree, bool includeSecretEdges = true)
  {
    foreach (var (nodeId, degree) in GraphTestSupport.GetDegrees(graph, includeSecretEdges))
    {
      Assert.True(degree <= maxDegree, $"Node {nodeId} has {degree} relationships, cap is {maxDegree}.");
    }
  }

  //
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
    Assert.Equal(expected.Secret, actual.Secret);
    Assert.Equal(expected.Style, actual.Style);

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
