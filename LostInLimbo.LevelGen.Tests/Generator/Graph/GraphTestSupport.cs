using LostInLimbo.LevelGen.Api;
using LostInLimbo.LevelGen.Data.Config;
using LostInLimbo.LevelGen.Data.Config.Graph;
using LostInLimbo.LevelGen.Data.Config.Graph.Builder;
using LostInLimbo.LevelGen.Data.Config.Graph.Proc;
using LostInLimbo.LevelGen.Data.Result.Graph;
using LostInLimbo.LevelGen.Internal.Generator.Graph.Builders;
using LostInLimbo.LevelGen.Internal.Generator.Prepass;

namespace LostInLimbo.LevelGen.Tests.Generator.Graph;

/// <summary>
/// Shared helpers for graph tests: a factory-driven build runner and graph queries over
/// <see cref="LilGraph"/> results.
/// </summary>
/// <remarks>
/// Not a test class itself - xUnit only collects types that contain test methods.
/// </remarks>
internal static class GraphTestSupport
{
  /// <summary>Default graph sub-seed used by tests unless a test needs another one.</summary>
  public const long DefaultSeedGraph = 42;

  /// <summary>
  /// Proc options with the secrets processor disabled - for builder tests that assert
  /// plain skeleton shape without compensating for the default secret node.
  /// </summary>
  public static CnfProcOpt NoSecrets { get; } = CnfProcOpt.Default with
  {
    RelSecrets = new CnfProcRelSecretsOpt { Amount = 0 },
  };

  //
  // Build runner
  //

  /// <summary>
  /// Runs a graph builder through the factory exactly like production dispatch does and
  /// reports the outcome.
  /// </summary>
  /// <remarks>
  /// The config's <see cref="CnfGraph.GraphBuilder"/> is forced to match <paramref name="builder"/>,
  /// so a test can never accidentally pair one builder with another builder's config section.
  /// </remarks>
  /// <param name="builder">Which builder the factory should create.</param>
  /// <param name="graphConfig">Graph config to use; defaults to a plain config for <paramref name="builder"/>.</param>
  /// <param name="seedGraph">Graph sub-seed to use.</param>
  /// <returns>The reported build result and the filled target graph.</returns>
  public static (LilResult Result, LilGraph Graph) Build(
    EnGraphBuilder builder,
    CnfGraph? graphConfig = null,
    long seedGraph = DefaultSeedGraph)
  {
    // Arrange: Fresh builder from the factory, config and an empty target graph.
    CnfGraphBuilder defBuilder = CreateDefBuilder(builder);
    var graphBuilder = GraphBuilderFactory.Create(defBuilder);
    var config = CnfLevelGeneration.Default with
    {
      Graph = (graphConfig ?? new CnfGraph()) with { GraphBuilder = defBuilder },
    };
    var prepData = new PrepData { SeedGraph = seedGraph };
    var graph = new LilGraph();

    // Act: Hand over the inputs, then build.
    var result = graphBuilder.Run(config, prepData, graph);

    // Assert-adjacent: Outcome and shape assertions live in the tests.
    return (result, graph);
  }

  /// <summary>
  /// Create default graph builder configuration for given enum.
  /// </summary>
  /// <param name="builder">Builder to use.</param>
  /// <returns></returns>
  public static CnfGraphBuilder CreateDefBuilder(EnGraphBuilder builder)
  {
    return builder switch {
      EnGraphBuilder.OneNode => CnfGraphBuilder.Default with { Builder = builder },
      EnGraphBuilder.Simple => CnfGraphBuilder.Default with { Builder = builder, CnfSimple = CnfGraphBuilderSimple.Default },
      EnGraphBuilder.Linear => CnfGraphBuilder.Default with { Builder = builder, CnfLinear = CnfGraphBuilderLinear.Default },
      _ => throw new ArgumentOutOfRangeException(nameof(builder), builder, "Unknown graph builder."),
    };
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
  /// <param name="includeSecretEdges">
  /// When false, secret relationships (appended by the finalization pass) do not count -
  /// useful for asserting builder-owned degree caps.
  /// </param>
  /// <returns>Relationship count per node; zero for node-less graphs.</returns>
  /// <exception cref="KeyNotFoundException">An edge references a node that does not exist.</exception>
  public static Dictionary<Guid, int> GetDegrees(LilGraph graph, bool includeSecretEdges = true)
  {
    var degrees = graph.Nodes.ToDictionary(node => node.Id, _ => 0);
    foreach (var edge in graph.Edges)
    {
      if (!includeSecretEdges && edge.Secret) continue;
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

  /// <summary>
  /// Reads the current group assignment of a graph in a stable order.
  /// </summary>
  /// <param name="graph">Graph to read.</param>
  /// <returns>Identifier and group pairs sorted by identifier.</returns>
  public static List<(Guid Id, string? Group)> GetGroupAssignments(LilGraph graph)
  {
    return graph.Nodes
      .OrderBy(node => node.Id)
      .Select(node => (node.Id, node.Style.Group))
      .ToList();
  }
}
