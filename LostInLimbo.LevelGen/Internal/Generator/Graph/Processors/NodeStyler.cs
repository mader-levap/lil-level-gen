using LostInLimbo.LevelGen.Data.Result.Graph;

namespace LostInLimbo.LevelGen.Internal.Generator.Graph.Processors;

/// <summary>
/// <para>
/// Graph pass that assigns <see cref="LilNode.Style"/> to every node. It runs
/// after nodes, relationships and POIs are known.
/// </para>
/// </summary>
/// <remarks>
/// <para>
/// </para>
/// </remarks>
internal static class NodeStyler
{
  /// <summary>Salt for the distribution sub-seed; keeps this pass off other passes' streams.</summary>
  private const long SaltStyle = 0x32658C73697A65; // "style"

  /// <summary> 
  /// Assigns style to <see cref="LilNode.Style"/> in every node of <paramref name="graph"/>.
  /// </summary>
  /// <param name="graph">Graph whose nodes are rewritten in place; may be empty.</param>
  /// <param name="options">Node styling parameters; validated before use.</param>
  /// <param name="seedGraph">Graph sub-seed the distribution stream is derived from.</param>
  public static void Execute(LilGraph graph, NodeStylerOptions options, long seedGraph)
  {
    // PLACEHOLDER
  }
}
