namespace LostInLimbo.LevelGen.Internal.Generator.Graph;

/// <summary>
/// Builds a linear graph: a chain of nodes where the first node holds the start POI, the
/// last node holds the exit POI, and every node between links to both its previous and next
/// node. Amount of nodes and obstacles is determined by graph complexity.
/// </summary>
/// <remarks>
/// Even this simple shape allows different scenarios, e.g. every node holding a solution for
/// the conditional passage to the next node, or every passage but the last one being
/// unconditional with the last one requiring all solutions. Scenario selection is deferred
/// to the algorithm phase.
/// NOTE: right now this is a placeholder; the real algorithm lands later.
/// </remarks>
internal sealed class LinearGraphBuilder : AGraphBuilder
{
  /// <inheritdoc/>
  public override void Build()
  {
    // Placeholder: the real algorithm (complexity-driven chain with scenarios) lands later.
  }
}
