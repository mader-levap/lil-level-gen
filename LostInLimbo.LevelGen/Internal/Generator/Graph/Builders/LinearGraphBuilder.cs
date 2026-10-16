namespace LostInLimbo.LevelGen.Internal.Generator.Graph.Builders;

/// <summary>
/// <para>Builds a linear graph: a chain of nodes where the first node holds the start POI, the
/// last node holds the exit POI, and every node between links to both its previous and next
/// node.</para>
/// </summary>
/// <remarks>
/// <para>
/// Amount of nodes is determined by graph size. Amount of obstacles is determined by graph complexity.
/// High connectivity means you may get multiple passages from prev to next node, though all of them
/// will have same obstacle (if present).
/// </para>
/// <para>Even this simple shape allows different scenarios, e.g. every node holding a solution for
/// the conditional passage to the next node, or every passage but the last one being
/// unconditional with the last one requiring all solutions. Scenario selection is deferred
/// to the algorithm phase.</para>
/// <para>NOTE: right now this is a placeholder; the real algorithm lands later.</para>
/// </remarks>
internal sealed class LinearGraphBuilder : AGraphBuilder
{
  /// <inheritdoc/>
  public override bool Build()
  {
    // Placeholder: the real algorithm lands later.
    return true;
  }
}
