using System;
using LostInLimbo.LevelGen.Data.Config.Graph;
using LostInLimbo.LevelGen.Data.Config.Graph.Builder;

namespace LostInLimbo.LevelGen.Internal.Generator.Graph.Builders;

/// <summary>
/// Creates the <see cref="AGraphBuilder"/> implementation selected by configuration.
/// </summary>
/// <remarks>
/// Kept as a separate, pure factory so the dispatch rule is directly unit-testable.
/// Unknown values are rejected here as defense in depth; the config layer already
/// validates <see cref="CnfGraph.GraphBuilder"/> before a run is claimed.
/// </remarks>
internal static class GraphBuilderFactory
{
  /// <summary>
  /// Creates the builder implementation matching the configured value. The caller must
  /// invoke <see cref="AGraphBuilder.Setup"/> before <see cref="AGraphBuilder.Build"/>.
  /// </summary>
  /// <param name="cnfGraphBuilder">Configuration for builders.</param>
  /// <returns>Fresh builder instance.</returns>
  /// <exception cref="ArgumentOutOfRangeException">The value names no known builder.</exception>
  public static AGraphBuilder Create(CnfGraphBuilder cnfGraphBuilder)
  {
    return cnfGraphBuilder.Builder switch
    {
      EnGraphBuilder.OneNode => new GraphBuilderOneNode(),
      EnGraphBuilder.Simple => new GraphBuilderSimple(cnfGraphBuilder),
      EnGraphBuilder.Linear => new GraphBuilderLinear(cnfGraphBuilder),
      _ => throw new ArgumentOutOfRangeException(nameof(cnfGraphBuilder.Builder), cnfGraphBuilder.Builder, "Unknown graph builder."),
    };
  }
}
