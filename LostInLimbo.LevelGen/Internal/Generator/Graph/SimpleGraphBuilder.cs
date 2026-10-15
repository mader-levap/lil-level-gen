namespace LostInLimbo.LevelGen.Internal.Generator.Graph;

/// <summary>
/// <para>
/// Builds the simple level graph: One or more nodes, but no obstacles. Start and exit may or may not be in same node.
/// Node can be connected to any other node, but there cannot be more than 4 outgoing relationships from same node.
/// </para>
/// </summary>
/// <remarks>
/// <para>Ignores graph complexity.</para>
/// <para>NOTE: right now this is a placeholder; the real algorithm lands later.</para>
/// </remarks>
internal sealed class SimpleGraphBuilder : AGraphBuilder
{
  /// <inheritdoc/>
  public override bool Build()
  {
    // Placeholder: the real algorithm lands later.
    return true;
  }
}
