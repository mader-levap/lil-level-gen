using System;
using LostInLimbo.LevelGen.Data.Config;
using LostInLimbo.LevelGen.Data.Config.Graph;
using LostInLimbo.LevelGen.Data.Prepass;
using LostInLimbo.LevelGen.Data.Result.Graph;

namespace LostInLimbo.LevelGen.Internal.Generator.Graph;

/// <summary>
/// Strategy that fills a level graph with nodes, edges, POIs and conditions.
/// One implementation exists per <see cref="EnGraphBuilder"/> value.
/// </summary>
/// <remarks>
/// Usage contract: call <see cref="Setup"/> once to hand over the inputs, then call
/// <see cref="Build"/> once to fill the target graph. <see cref="Build"/> must not be
/// called before <see cref="Setup"/>.
/// <para>Implementations must obey the following contract:</para>
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
/// <item><b>Cancellation</b>: cancellation is observed by the caller at phase boundaries
/// before the builder runs; a cancelled run reports <c>EnGenResult.Cancelled</c> and its
/// partial world is discarded. A token passed to builders lands later, with the real
/// algorithms.</item>
/// </list>
/// </remarks>
internal abstract class AGraphBuilder
{
  /// <summary>
  /// Full generation configuration. Set by <see cref="Setup"/> before <see cref="Build"/>
  /// runs; not initialized in the constructor.
  /// </summary>
  protected CnfLevelGeneration Config = null!;

  /// <summary>
  /// Preparatory data based on config. Set by <see cref="Setup"/> before <see cref="Build"/>
  /// runs; not initialized in the constructor.
  /// </summary>
  protected PrepData PrepData = null!;

  /// <summary>
  /// Target graph. Set by <see cref="Setup"/> before <see cref="Build"/> runs; not
  /// initialized in the constructor.
  /// </summary>
  protected LilGraph Graph = null!;

  /// <summary>
  /// Hands the builder its inputs. Must be called once before <see cref="Build"/>.
  /// </summary>
  /// <param name="config">Full generation configuration.</param>
  /// <param name="prepData">Preparatory data based on config.</param>
  /// <param name="graph">Target graph. Empty on entry; <see cref="Build"/> appends nodes and edges in place.</param>
  /// <exception cref="ArgumentNullException">A parameter is <see langword="null"/>.</exception>
  public void Setup(CnfLevelGeneration config, PrepData prepData, LilGraph graph)
  {
    if (config is null) throw new ArgumentNullException(nameof(config));
    if (prepData is null) throw new ArgumentNullException(nameof(prepData));
    if (graph is null) throw new ArgumentNullException(nameof(graph));

    Config = config;
    PrepData = prepData;
    Graph = graph;
  }

  /// <summary>Fills the target graph with the complete abstract level description.</summary>
  public abstract void Build();
}
