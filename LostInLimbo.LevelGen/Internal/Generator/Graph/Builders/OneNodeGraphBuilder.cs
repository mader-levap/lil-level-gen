using LostInLimbo.LevelGen.Data.Result.Graph;
using LostInLimbo.LevelGen.Internal.Utils;

namespace LostInLimbo.LevelGen.Internal.Generator.Graph.Builders;

/// <summary>
/// Builds the simplest possible level graph: a single node containing both the start and
/// the exit POI, with no edges, conditions or obstacles. The player can beeline from start
/// to exit immediately. Useful for tests, debug and special scenarios.
/// </summary>
/// <remarks>
/// <para>This is the only builder that always creates the same graph shape regardless of graph
/// config; those settings are deliberately ignored and no randomness is involved.</para>
/// <para>Identifier salts are fixed role constants (instead of a seed-driven sequence) because
/// the shape never changes: one node, one start POI, one exit POI.</para>
/// </remarks>
internal sealed class OneNodeGraphBuilder : AGraphBuilder
{
  /// <summary>Relative size of the single node; baseline against which other nodes are compared.</summary>
  private const int NodeRelSize = 1;

  /// <summary>Salt for the single node identifier.</summary>
  private const long SaltNode = 0x6E6F6465; // "node"
  /// <summary>Salt for the start POI identifier.</summary>
  private const long SaltStartPoi = 0x7374617274; // "start"
  /// <summary>Salt for the exit POI identifier.</summary>
  private const long SaltExitPoi = 0x65786974; // "exit"

  /// <inheritdoc/>
  protected override bool Build()
  {
    // Identifiers are derived from the graph sub-seed; deterministic, no RNG.
    var nodeId = IdDerivation.Derive(PrepData.SeedGraph, SaltNode);
    var startPoiId = IdDerivation.Derive(PrepData.SeedGraph, SaltStartPoi);
    var exitPoiId = IdDerivation.Derive(PrepData.SeedGraph, SaltExitPoi);

    var node = new LilNode
    {
      Id = nodeId,
      RelSize = NodeRelSize,
      Pois =
      [
        new LilPoi { Id = startPoiId, Type = EnPoiType.Start },
        new LilPoi { Id = exitPoiId, Type = EnPoiType.Exit },
      ],
    };

    Graph.Nodes.Add(node);
    // No edges, conditions or obstacles: start and exit live in the same place.
    return true;
  }
}
