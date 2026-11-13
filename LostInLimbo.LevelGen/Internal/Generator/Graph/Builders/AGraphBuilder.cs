using System;
using System.Threading;
using LostInLimbo.LevelGen.Data.Status;
using LostInLimbo.LevelGen.Data.Config;
using LostInLimbo.LevelGen.Data.Config.Graph;
using LostInLimbo.LevelGen.Data.Result.Graph;
using LostInLimbo.LevelGen.Internal.Generator.Graph.Processors;
using LostInLimbo.LevelGen.Internal.Generator.Prepass;
using LostInLimbo.LevelGen.Internal.Utils.Rng;

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
/// <see cref="Build"/> is called (graph generation is a single phase step; a cancelled build
/// discards the world, and a pause that wins over the cancellation resets the graph to an empty
/// one, see <see cref="GraphGenerator.Execute"/>, so the phase re-runs from scratch).</item>
/// <item><b>Failsafe</b>: Graph build can fail, though builders should make a best effort to make it as unlikely as possible.
/// Graph configuration variables are soft constraints: builders are allowed to ignore unreasonable values.
/// A failed build (a false return or an exception) fails the whole run: the world is discarded
/// and never published, so a partially mutated graph is never resumed or re-run - builders need
/// no rollback.</item>
/// <item><b>MaxEdges cap</b>: every created node must receive a <see cref="LilNode.MaxEdges"/>
/// value (negative means no limit), and the builder must never produce a node whose total
/// relationship count - skeleton, extras and parallel passages alike - exceeds that cap.</item>
/// </list>
/// <para>
/// Note: overly complex graphs might fail later during map generation. For example, it might be
/// physically impossible to generate a large number of passages from one place to another.
/// </para>
/// </remarks>
internal abstract class AGraphBuilder
{
  /// <summary>Salt for node identifiers.</summary>
  protected const long SaltNode = 0x6E6F6465; // "node"
  /// <summary>Salt for edge identifiers.</summary>
  protected const long SaltEdge = 0x65646765; // "edge"
  /// <summary>Salt for the start POI identifier.</summary>
  protected const long SaltStartPoi = 0x7374617274; // "start"
  /// <summary>Salt for the exit POI identifier.</summary>
  protected const long SaltExitPoi = 0x65786974; // "exit"

  /// <summary>
  /// Initial relative size of every created node; replaced by the <see cref="ProcNodeRelSize"/>
  /// pass once the whole graph (relationships and POIs included) is known.
  /// </summary>
  protected const int InitialNodeRelSize = 1;

  /// <summary>Full generation configuration.</summary>
  protected CnfLevelGeneration Config = null!;

  /// <summary>Preparatory data based on config.</summary>
  protected PrepData PrepData = null!;

  /// <summary>Target graph.</summary>
  protected LilGraph Graph = null!;

  /// <summary>RNG generator.</summary>
  protected SeededRandom Random = null!;

  /// <summary>
  /// Sets up and runs the build.
  /// </summary>
  /// <param name="config">Full generation configuration.</param>
  /// <param name="prepData">Preparatory data based on config.</param>
  /// <param name="graph">Target graph. Empty on entry; <see cref="Build"/> appends nodes and edges in place.</param>
  /// <param name="cancellationToken">Token checked between build passes.</param>
  /// <returns>True if graph generation was successful, otherwise false.</returns>
  /// <exception cref="ArgumentNullException">A parameter is <see langword="null"/>.</exception>
  /// <exception cref="OperationCanceledException">The token was cancelled; the graph may be partially filled.</exception>
  public LilResult Run(CnfLevelGeneration config, PrepData prepData, LilGraph graph, CancellationToken cancellationToken = default)
  {
    Setup(config, prepData, graph);
    // Debug-only deliberate failure, mirroring CnfDebug.FailAtStart at the run level.
    if (Config.Debug?.FailGraphBuild == true) return LilResult.Fail("Deliberate failure at the very start.");
    return Build(cancellationToken);
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
  /// <param name="cancellationToken">Token to check between build passes.</param>
  /// <returns>
  /// A result whose <see cref="LilResult.Success"/> is <see langword="true"/> when
  /// the graph is successfully generated; otherwise a failed result with the reason of the failure.
  /// </returns>
  /// <exception cref="OperationCanceledException">The token was cancelled; the graph may be partially filled.</exception>
  protected abstract LilResult Build(CancellationToken cancellationToken);

  #region Shared build steps

  /// <summary>
  /// Creates <paramref name="size"/> nodes in index order with deterministic identifiers
  /// (graph sub-seed, <see cref="SaltNode"/>, node index as sub-channel) and
  /// <see cref="InitialNodeRelSize"/>. Consumes no RNG draws.
  /// </summary>
  /// <param name="size">Number of nodes to create.</param>
  /// <param name="maxEdges">Hard relationship cap of every node (<see cref="LilNode.MaxEdges"/>).</param>
  /// <returns>Node identifiers in index order; node <c>i</c> is <c>Graph.Nodes[i]</c>.</returns>
  protected Guid[] CreateNodes(int size, int maxEdges)
  {
    var nodeIds = new Guid[size];
    for (var index = 0; index < size; index++)
    {
      // Nested derivation: index is the sub-channel, so ids are distinct per index
      // and never collide with role salts of other identifiers.
      var subSeed = SeedDerivation.Derive(PrepData.SeedGraph, SaltNode, index);
      var id = IdDerivation.Derive(subSeed, SaltNode);
      nodeIds[index] = id;
      Graph.Nodes.Add(new LilNode
      {
        Id = id,
        RelSize = InitialNodeRelSize,
        MaxEdges = maxEdges,
      });
    }

    return nodeIds;
  }

  /// <summary>
  /// Places the start POI and the exit POI, with identifiers derived from the graph sub-seed.
  /// Both may land on the same node. Consumes no RNG draws.
  /// </summary>
  /// <param name="startIndex">Index of the node receiving the start POI.</param>
  /// <param name="exitIndex">Index of the node receiving the exit POI.</param>
  protected void PlaceStartAndExit(int startIndex, int exitIndex)
  {
    Graph.Nodes[startIndex].Pois.Add(new LilPoi
    {
      Id = IdDerivation.Derive(PrepData.SeedGraph, SaltStartPoi),
      Type = EnPoiType.Start,
    });
    Graph.Nodes[exitIndex].Pois.Add(new LilPoi
    {
      Id = IdDerivation.Derive(PrepData.SeedGraph, SaltExitPoi),
      Type = EnPoiType.Exit,
    });
  }

  /// <summary>
  /// Runs the finalization processors in their fixed order: secret nodes, objective POIs,
  /// node relative size and grouping. Each processor uses its own salted stream of the graph
  /// sub-seed, so it never shifts the builder's <see cref="Random"/> draws.
  /// </summary>
  /// <param name="cancellationToken">Token checked before every processor.</param>
  /// <exception cref="OperationCanceledException">The token was cancelled.</exception>
  protected void RunFinalization(CancellationToken cancellationToken)
  {
    var procOpts = Config.Graph.ProcOpts;
    cancellationToken.ThrowIfCancellationRequested();
    new ProcRelSecrets(Graph, procOpts.RelSecrets, PrepData.SeedGraph).Execute();
    cancellationToken.ThrowIfCancellationRequested();
    new ProcNodeObjectives(Graph, procOpts.NodeObjectives, PrepData.SeedGraph).Execute();
    cancellationToken.ThrowIfCancellationRequested();
    new ProcNodeRelSize(Graph, procOpts.NodeRelSize, PrepData.SeedGraph).Execute();
    cancellationToken.ThrowIfCancellationRequested();
    new ProcNodeStyle(Graph, procOpts.NodeStyle, PrepData.SeedGraph).Execute();
  }

  #endregion
}
