using System;
using LostInLimbo.LevelGen.Data.Config.Graph;

namespace LostInLimbo.LevelGen.Internal.Generator.Graph.Builders;

/// <summary>
/// Creates the <see cref="AGraphBuilder"/> implementation selected by configuration.
/// </summary>
/// <remarks>
/// Kept as a separate, pure factory so the dispatch rule is directly unit-testable.
/// Unknown values are rejected here as defense in depth; the config layer already
/// validates <see cref="CnfGraph.Builder"/> before a run is claimed.
/// </remarks>
internal static class GraphBuilderFactory
{
  /// <summary>
  /// Creates the builder implementation matching the configured value. The caller must
  /// invoke <see cref="AGraphBuilder.Setup"/> before <see cref="AGraphBuilder.Build"/>.
  /// </summary>
  /// <param name="builder">Configured graph builder kind.</param>
  /// <returns>Fresh builder instance.</returns>
  /// <exception cref="ArgumentOutOfRangeException">The value names no known builder.</exception>
  public static AGraphBuilder Create(EnGraphBuilder builder)
  {
    return builder switch
    {
      EnGraphBuilder.OneNode => new GraphBuilderOneNode(),
      EnGraphBuilder.Simple => new GraphBuilderSimple(),
      EnGraphBuilder.Linear => new GraphBuilderLinear(),
      _ => throw new ArgumentOutOfRangeException(nameof(builder), builder, "Unknown graph builder."),
    };
  }
}
