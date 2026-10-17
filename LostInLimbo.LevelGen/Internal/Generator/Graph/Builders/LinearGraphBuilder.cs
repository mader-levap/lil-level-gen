namespace LostInLimbo.LevelGen.Internal.Generator.Graph.Builders;

/// <summary>
/// <para>Builds a linear graph: a chain of nodes where the first node holds the start POI, the
/// last node holds the exit POI, and every node between links to both its previous and next
/// node.</para>
/// </summary>
/// <remarks>
/// <para>Rules:</para>
/// <list type="bullet">
/// <item>Node count follows <c>CnfGraph.Size</c>.</item>
/// <item><c>CnfGraph.Connectivity</c> is the amount of relationships above the minimum
/// (the spanning structure). That allows multiple passages from previous node to next node
/// and/or shortcuts (passage that skips one or more nodes in chain - must respect required
/// obstacles).
/// Builder can ignore excess Connectivity if it cannot make more relationships.</item>
/// <item><c>CnfGraph.Complexity</c> slightly indirectly determines amount of obstacles.
/// Like Connectivity, it is soft constraint.</item>
/// </list>
/// <para>Even this simple shape allows different scenarios:</para>
/// <list type="bullet">
/// <item>Every node holding a solution for the conditional passage to the next node.</item>
/// <item>Some passages are open, other require solution that may be found earlier.</item>
/// <item>Every passage but the last one is unconditional with the last one requiring all solutions.</item>
/// </list>
/// <para>The graph is built in five passes:</para>
/// <list type="bullet">
/// <item>A linearly connected skeleton.</item>
/// <item>Start and exit POIs always at beginning and end node respectively.</item>
/// <item>Extra relationships between nodes up to connectivity and <see cref="MaxExtraDegree"/>.</item>
/// <item>Create obstacles for relationships up to complexity and <see cref="MaxConditionPerRel"/>.</item>
/// <item>Relative sizes distributed over the finished nodes via <see cref="RelSizeDistributor"/>.</item>
/// </list>
/// </remarks>
internal sealed class LinearGraphBuilder : AGraphBuilder
{
  /// <summary>
  /// Maximum amount of extra relationships a node may gain in the extra-relationships phase (inclusive).
  /// Important: we count in and out relationships separately.
  /// </summary>
  internal const int MaxExtraDegree = 2;
  /// <summary>Maximum amount of conditions that single relationship can have.</summary>
  internal const int MaxConditionPerRel = 3;
  /// <summary>Maximum distance between solution and node having relationship with relevant condition.</summary>
  internal const int MaxConditionDistance = 3;
  /// <summary>Maximum amount of nodes that can be skipped by extra relationship.</summary>
  internal const int MaxSkipDistance = 2;

  /// <inheritdoc/>
  protected override bool Build()
  {
    // Placeholder: the real algorithm lands later.
    return true;
  }
}
