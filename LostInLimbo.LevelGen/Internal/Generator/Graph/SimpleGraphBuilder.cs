namespace LostInLimbo.LevelGen.Internal.Generator.Graph;

/// <summary>
/// <para>
/// Builds the simple level graph: One or more nodes, but no obstacles. Start and exit may or may not be in same node.
/// Node can be connected to any other node, but there cannot be more than 4 outgoing relationships from same node.
/// </para>
/// </summary>
/// <remarks>
/// <para>Rules:</para>
/// <list type="bullet">
/// <item>Graph size is followed.</item>
/// <item>Graph connectivity means amount of relationships above minimum. But there is upper limit per node.</item>
/// <item>Ignores graph complexity.</item>
/// </list>
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
