using LostInLimbo.LevelGen.Data.Config.Graph.Proc;
using LostInLimbo.LevelGen.Data.Result.Graph;
using LostInLimbo.LevelGen.Internal.Generator.Graph.Processors.Obstacler;

namespace LostInLimbo.LevelGen.Tests.Generator.Graph.Processors.Obstacler;

/// <summary>
/// Shared helpers for the obstacle placer tests: small hand-made graphs with predictable
/// work indices.
/// </summary>
/// <remarks>
/// Nodes get the identifiers <c>1 .. nodeCount</c>, so node <c>n</c> has work index
/// <c>n - 1</c>. Edges get the identifiers <c>101, 102, ...</c> in the order they are passed,
/// so the <c>k</c>-th edge (zero-based) has work index <c>k</c>.
/// </remarks>
internal static class ObstaclerTestSupport
{
  /// <summary>Identifier of the first edge; later edges count up from here.</summary>
  public const int FirstEdgeId = 101;

  /// <summary>Describes one passage of a hand-made graph by node numbers (one-based).</summary>
  /// <param name="From">Number of the source node.</param>
  /// <param name="To">Number of the destination node.</param>
  /// <param name="Chokepoint">Whether the passage is a chokepoint.</param>
  /// <param name="Secret">Whether the passage is secret.</param>
  /// <param name="OneWay">Whether the passage leads From -> To only.</param>
  /// <param name="Conditions">Number of conditions already on the passage.</param>
  public sealed record Passage(
    int From,
    int To,
    bool Chokepoint = false,
    bool Secret = false,
    bool OneWay = false,
    int Conditions = 0);

  /// <summary>
  /// Creates a graph with nodes <c>1 .. nodeCount</c> and the given passages.
  /// </summary>
  /// <param name="nodeCount">Number of nodes.</param>
  /// <param name="passages">Passages; the k-th one gets work index k.</param>
  /// <returns>The graph; no POIs.</returns>
  public static LilGraph CreateGraph(int nodeCount, params Passage[] passages)
  {
    var graph = new LilGraph();
    for (var id = 1; id <= nodeCount; id++) graph.Nodes.Add(GraphTestCreate.Node(id));

    for (var index = 0; index < passages.Length; index++)
    {
      var passage = passages[index];
      var conditions = new LilCondition[passage.Conditions];
      for (var slot = 0; slot < conditions.Length; slot++)
      {
        // Distinct fake solutions; the cut machinery only counts conditions.
        conditions[slot] = GraphTestCreate.Condition(9000 + index * 10 + slot);
      }

      graph.Edges.Add(GraphTestCreate.Edge(
        FirstEdgeId + index,
        passage.From,
        passage.To,
        passage.OneWay ? EnPassageDirection.OneWay : EnPassageDirection.Bidirectional,
        conditions,
        passage.Secret,
        passage.Chokepoint));
    }

    return graph;
  }

  /// <summary>Identifier of the start POI added by <see cref="CreateLevel"/>.</summary>
  public const int StartPoiId = 500;
  /// <summary>Identifier of the exit POI added by <see cref="CreateLevel"/>.</summary>
  public const int ExitPoiId = 501;

  /// <summary>
  /// Creates a graph like <see cref="CreateGraph"/> and adds a start and an exit POI.
  /// </summary>
  /// <param name="nodeCount">Number of nodes.</param>
  /// <param name="start">Number (one-based) of the node receiving the start POI.</param>
  /// <param name="exit">Number (one-based) of the node receiving the exit POI; may equal <paramref name="start"/>.</param>
  /// <param name="passages">Passages; the k-th one gets work index k.</param>
  /// <returns>The graph.</returns>
  public static LilGraph CreateLevel(int nodeCount, int start, int exit, params Passage[] passages)
  {
    var graph = CreateGraph(nodeCount, passages);
    graph.Nodes[start - 1].Pois.Add(GraphTestCreate.Poi(StartPoiId, EnPoiType.Start));
    graph.Nodes[exit - 1].Pois.Add(GraphTestCreate.Poi(ExitPoiId, EnPoiType.Exit));
    return graph;
  }

  /// <summary>Marks nodes of a graph as secret (replaces the node records).</summary>
  /// <param name="graph">Graph to change in place.</param>
  /// <param name="nodes">Numbers (one-based) of the nodes to mark.</param>
  public static void MarkSecret(LilGraph graph, params int[] nodes)
  {
    foreach (var node in nodes) graph.Nodes[node - 1] = graph.Nodes[node - 1] with { Secret = true };
  }

  /// <summary>
  /// Creates the work graph of <see cref="CreateGraph"/> with default options.
  /// </summary>
  /// <param name="nodeCount">Number of nodes.</param>
  /// <param name="passages">Passages; the k-th one gets work index k.</param>
  /// <returns>The work graph.</returns>
  public static ObstacleWorkGraph CreateWorkGraph(int nodeCount, params Passage[] passages)
  {
    return new ObstacleWorkGraph(CreateGraph(nodeCount, passages), CnfProcObstaclePlacerOpt.Default);
  }

  /// <summary>Creates a chain <c>1 - 2 - ... - nodeCount</c> of open, non-chokepoint passages.</summary>
  /// <param name="nodeCount">Number of nodes; edge k joins nodes k + 1 and k + 2.</param>
  /// <returns>The passages of the chain.</returns>
  public static Passage[] Chain(int nodeCount)
  {
    var passages = new Passage[nodeCount - 1];
    for (var index = 0; index < passages.Length; index++) passages[index] = new Passage(index + 1, index + 2);
    return passages;
  }
}
