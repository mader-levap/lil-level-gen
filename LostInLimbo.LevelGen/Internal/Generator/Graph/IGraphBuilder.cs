using System.Threading;
using LostInLimbo.LevelGen.Data.Config;
using LostInLimbo.LevelGen.Data.Result.Graph;

namespace LostInLimbo.LevelGen.Internal.Generator.Graph;

/// <summary>
/// Strategy that fills a level graph with nodes, edges, POIs and conditions.
/// One implementation exists per <see cref="EnGraphBuilder"/> value.
/// </summary>
/// <remarks>
/// Implementations must obey the following contract:
/// <list type="bullet">
/// <item><b>Determinism</b>: the same configuration (including the world seed) always produces
/// the same graph. Assign IDs by deriving them from the seed (or from a deterministic sequence)
/// - never from <c>Guid.NewGuid()</c>.</item>
/// <item><b>Solvability</b>: the produced graph must be beatable - a solution is always
/// reachable before any condition requiring it, and the start is connected to the exit.
/// No softlocks.</item>
/// <item><b>Preconditions</b>: the target graph is guaranteed to be empty when
/// <see cref="Build"/> is called (graph generation is a single phase step and the whole run's
/// world is discarded when it ends cancelled).</item>
/// <item><b>Cancellation</b>: long-running implementations check
/// <paramref name="cancellationToken"/> at pass boundaries; a cancelled
/// run reports <c>EnGenResult.Cancelled</c> and its partial world is discarded.</item>
/// </list>
/// </remarks>
internal interface IGraphBuilder
{
  /// <summary>
  /// Fills the target graph with the complete abstract level description.
  /// </summary>
  /// <param name="config">Full generation configuration.</param>
  /// <param name="graph">Target graph. Empty on entry; append nodes and edges in place.</param>
  /// <param name="cancellationToken">Token checked at pass boundaries.</param>
  void Build(CnfLevelGeneration config, LilGraph graph, CancellationToken cancellationToken);
}
