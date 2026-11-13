using System.Threading;
using LostInLimbo.LevelGen.Data.Status;
using LostInLimbo.LevelGen.Data.Result.Graph;
using LostInLimbo.LevelGen.Internal.Utils.Rng;

namespace LostInLimbo.LevelGen.Internal.Generator.Graph.Builders;

/// <summary>
/// Builds the simplest possible level graph: a single node containing both the start and
/// the exit POI, with no edges, conditions or obstacles. The player can beeline from start
/// to exit immediately. Useful for tests, debug and special scenarios.
/// </summary>
/// <remarks>
/// <para>This is the only builder that always creates the same graph shape regardless of graph
/// config; those settings are deliberately ignored and no randomness is involved.</para>
/// <para>The node identifier uses the <see cref="AGraphBuilder.SaltNode"/> role salt directly
/// (instead of the per-index derivation of <see cref="AGraphBuilder.CreateNodes"/>) because
/// the shape never changes: one node, one start POI, one exit POI.</para>
/// </remarks>
internal sealed class GraphBuilderOneNode : AGraphBuilder
{
  /// <summary>Relative size of the single node; baseline against which other nodes are compared.</summary>
  private const int NodeRelSize = 1;

  /// <inheritdoc/>
  protected override LilResult Build(CancellationToken cancellationToken)
  {
    // Identifiers are derived from the graph sub-seed; deterministic, no RNG.
    Graph.Nodes.Add(new LilNode
    {
      Id = IdDerivation.Derive(PrepData.SeedGraph, SaltNode),
      RelSize = NodeRelSize,
      MaxEdges = 0,
    });
    PlaceStartAndExit(0, 0);

    // No edges, conditions or obstacles: start and exit live in the same place.
    return LilResult.Succeed();
  }
}
