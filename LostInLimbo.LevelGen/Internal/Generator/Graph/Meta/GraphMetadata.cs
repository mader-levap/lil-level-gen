using LostInLimbo.LevelGen.Data.Result.Graph;

namespace LostInLimbo.LevelGen.Internal.Generator.Graph.Meta;

/// <summary>
/// <para>Adds metadata to graph.</para>
/// </summary>
internal sealed class GraphMetadata(LilGraph? graph)
{
  /// <summary>Graph to verify.</summary>
  private LilGraph? _graph = graph;

  /// <summary>
  /// Generate additional metadata for graph. These will be used for validation and as help for map generation.
  /// </summary>
  public void Generate()
  {
    // TODO: PLACEHOLDER
  }
}
