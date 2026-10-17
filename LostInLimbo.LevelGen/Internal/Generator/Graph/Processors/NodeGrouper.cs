using LostInLimbo.LevelGen.Data.Result.Graph;

namespace LostInLimbo.LevelGen.Internal.Generator.Graph.Processors;

/// <summary>
/// <para>
/// Graph pass that assigns <see cref="LilNode.Group"/> to every node. It runs
/// after nodes, relationships and POIs are known.
/// </para>
/// </summary>
/// <remarks>
/// <para>
/// </para>
/// </remarks>
internal static class NodeGrouper
{
  /// <summary>Salt for the distribution sub-seed; keeps this pass off other passes' streams.</summary>
  private const long SaltGroup = 0x32658C73697A65; // "group"

  /// <summary> 
  /// Assigns group to <see cref="LilNode.Group"/> to every node of <paramref name="graph"/>.
  /// </summary>
  /// <param name="graph">Graph whose nodes are rewritten in place; may be empty.</param>
  /// <param name="options">Node grouping parameters; validated before use.</param>
  /// <param name="seedGraph">Graph sub-seed the distribution stream is derived from.</param>
  public static void Execute(LilGraph graph, NodeGrouperOptions options, long seedGraph)
  {
    // PLACEHOLDER
  }
}
