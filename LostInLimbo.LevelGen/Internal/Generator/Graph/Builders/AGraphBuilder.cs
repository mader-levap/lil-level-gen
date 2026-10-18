using System;
using LostInLimbo.LevelGen.Data.Config;
using LostInLimbo.LevelGen.Data.Config.Graph;
using LostInLimbo.LevelGen.Data.Result.Graph;
using LostInLimbo.LevelGen.Internal.Generator.Prepass;
using LostInLimbo.LevelGen.Internal.Utils;

namespace LostInLimbo.LevelGen.Internal.Generator.Graph.Builders;

/// <summary>
/// <para>
/// Strategy that fills a level graph with nodes, edges, POIs and conditions.
/// One implementation exists per <see cref="EnGraphBuilder"/> value.
/// </para>
/// </summary>
/// <remarks>
/// <para>Usage contract: call <see cref="Run"/> to hand over the inputs and fill the target graph.</para>
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
/// world is discarded when it is cancelled).</item>
/// <item><b>Failsafe</b>: Graph build can fail, though builders should give best effort to make it as unlikely as possible.
/// Graph configuration variables are soft constraints: builders are allowed to ignore unreasonable values.</item>
/// </list>
/// <para>
/// Note: overly complex graphs might fail later during map generation. For example, it might be
/// physically impossible to generate large amount of passages from one place to another.
/// </para>
/// </remarks>
internal abstract class AGraphBuilder
{
  /// <summary>Full generation configuration.</summary>
  protected CnfLevelGeneration Config = null!;

  /// <summary>Preparatory data based on config.</summary>
  protected PrepData PrepData = null!;

  /// <summary>Target graph.</summary>
  protected LilGraph Graph = null!;

  /// <summary>RNG generator.</summary>
  protected SeededRandom Random = null!;

  /// <summary>
  /// Setups and runs build.
  /// </summary>
  /// <param name="config">Full generation configuration.</param>
  /// <param name="prepData">Preparatory data based on config.</param>
  /// <param name="graph">Target graph. Empty on entry; <see cref="Build"/> appends nodes and edges in place.</param>
  /// <returns>True if graph generation was successful, otherwise false.</returns>
  /// <exception cref="ArgumentNullException">A parameter is <see langword="null"/>.</exception>
  public bool Run(CnfLevelGeneration config, PrepData prepData, LilGraph graph)
  {
    Setup(config, prepData, graph);
    return Build();
  }

  /// <summary>
  /// Hands the builder its inputs. Must be called once before <see cref="Build"/>.
  /// </summary>
  /// <param name="config">Full generation configuration.</param>
  /// <param name="prepData">Preparatory data based on config.</param>
  /// <param name="graph">Target graph.</param>
  /// <exception cref="ArgumentNullException">A parameter is <see langword="null"/>.</exception>
  internal void Setup(CnfLevelGeneration config, PrepData prepData, LilGraph graph)
  {
    if (config is null) throw new ArgumentNullException(nameof(config));
    if (prepData is null) throw new ArgumentNullException(nameof(prepData));
    if (graph is null) throw new ArgumentNullException(nameof(graph));

    Config = config;
    PrepData = prepData;
    Graph = graph;
    Random = new SeededRandom(PrepData.SeedGraph);
  }

  /// <summary>Fills the target graph with the complete abstract level description.</summary>
  /// <remarks>You must run <see cref="Setup"/> before calling <see cref="Build"/>.</remarks>
  /// <returns>True if graph generation was successful, otherwise false.</returns>
  protected abstract bool Build();
}
