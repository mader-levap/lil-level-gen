namespace LostInLimbo.LevelGen.Internal.Generator.Graph;

/// <summary>
/// Builds the simplest possible level graph: a single node containing both the start and
/// the exit POI, with no edges, conditions or obstacles. The player can beeline from start
/// to exit immediately. Useful for tests, debug and special scenarios.
/// </summary>
/// <remarks>
/// <para>This is only builder that always creates same graph regardless of graph size, graph complexity etc.</para>
/// <para>NOTE: right now this is a placeholder; the real algorithm lands later.</para>
/// </remarks>
internal sealed class OneNodeGraphBuilder : AGraphBuilder
{
  /// <inheritdoc/>
  public override bool Build()
  {
    // Placeholder: the real algorithm (single node + Start/Exit POIs) lands later.
    return true;
  }
}
