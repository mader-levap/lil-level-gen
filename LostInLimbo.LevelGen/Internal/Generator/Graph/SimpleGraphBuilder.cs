using System.Threading;
using LostInLimbo.LevelGen.Data.Config;
using LostInLimbo.LevelGen.Data.Result.Graph;

namespace LostInLimbo.LevelGen.Internal.Generator.Graph;

/// <summary>
/// Builds the simplest possible level graph: a single node containing both the start and
/// the exit POI, with no edges, conditions or obstacles. The player can beeline from start
/// to exit immediately. Useful for tests, debug and special scenarios.
/// </summary>
/// <remarks>NOTE: right now this is a placeholder; the real algorithm lands later.</remarks>
internal sealed class SimpleGraphBuilder : IGraphBuilder
{
  /// <inheritdoc/>
  public void Build(CnfLevelGeneration config, LilGraph graph, CancellationToken cancellationToken)
  {
    cancellationToken.ThrowIfCancellationRequested();
    // Placeholder: the real algorithm (single node + Start/Exit POIs) lands later.
  }
}
