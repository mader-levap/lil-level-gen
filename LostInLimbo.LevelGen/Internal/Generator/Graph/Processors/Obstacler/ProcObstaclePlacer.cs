using System;
using System.Threading;
using LostInLimbo.LevelGen.Data.Config.Graph;
using LostInLimbo.LevelGen.Data.Config.Graph.Proc;
using LostInLimbo.LevelGen.Data.Result.Graph;

namespace LostInLimbo.LevelGen.Internal.Generator.Graph.Processors.Obstacler;

/// <summary>
/// <para>
/// Graph pass that places obstacles on any graph: conditions on passages
/// (<see cref="LilEdge.Conditions"/>) and the solutions that open them
/// (<see cref="EnPoiType.Solution"/> POIs in nodes). Never produces a softlock.
/// </para>
/// </summary>
/// <remarks>
/// <para>
/// One obstacle is one new solution plus the same condition on one or more passages.
/// <see cref="CnfGraph.Complexity"/> is the largest number of obstacles; it is a soft
/// constraint - what cannot be placed sensibly is dropped silently.
/// </para>
/// <para>The pass does nothing when:</para>
/// <list type="bullet">
/// <item>Complexity is 0;</item>
/// <item>the graph has fewer than two nodes or no passage;</item>
/// <item>there is not exactly one node with a Start POI and exactly one node with an Exit POI
/// (missing, or spread over several nodes - not supported).</item>
/// </list>
/// <para>
/// Currently only the main pass runs (<see cref="ObstaclePassMain"/>): obstacles between the
/// start and the exit, skipped when both share a node. Secret passages that would bypass an
/// obstacle are always locked with it.
/// </para>
/// <para>
/// No nodes or passages are added, so <see cref="LilNode.MaxEdges"/> caps are untouched.
/// Existing conditions and solutions are kept and respected. Deterministic for the same graph
/// contents, options, complexity and seed; independent of the order of the graph lists.
/// See <c>Docs/OBSTACLES.md</c> for the design.
/// </para>
/// </remarks>
internal sealed class ProcObstaclePlacer
{
  /// <summary>Smallest graph that can hold an obstacle: two nodes.</summary>
  private const int MinNodeCount = 2;

  /// <summary>Graph that receives obstacles in place.</summary>
  private readonly LilGraph _graph;
  /// <summary>Obstacle placer options; validated at construction.</summary>
  private readonly CnfProcObstaclePlacerOpt _options;
  /// <summary>Largest number of obstacles to place.</summary>
  private readonly int _complexity;
  /// <summary>Graph sub-seed the random stream and identifier derivations are based on.</summary>
  private readonly long _seedGraph;

  /// <summary>
  /// Prepares the pass over <paramref name="graph"/>. Validates the options eagerly, so bad
  /// options are rejected even when the pass would do nothing.
  /// </summary>
  /// <param name="graph">Graph that receives obstacles in place by <see cref="Execute"/>; may be empty.</param>
  /// <param name="options">Obstacle placer options; validated before use.</param>
  /// <param name="complexity">Largest number of obstacles to place, see <see cref="CnfGraph.Complexity"/>. Must not be negative.</param>
  /// <param name="seedGraph">Graph sub-seed the random stream and identifier derivations are based on.</param>
  /// <exception cref="ArgumentNullException"><paramref name="graph"/> or <paramref name="options"/> is null.</exception>
  /// <exception cref="ArgumentOutOfRangeException">
  /// <paramref name="complexity"/> is negative, or options are out of range; see
  /// <see cref="CnfProcObstaclePlacerOpt.Validate"/>.
  /// </exception>
  public ProcObstaclePlacer(LilGraph graph, CnfProcObstaclePlacerOpt options, int complexity, long seedGraph)
  {
    if (graph is null) throw new ArgumentNullException(nameof(graph));
    if (options is null) throw new ArgumentNullException(nameof(options));
    if (complexity < 0)
      throw new ArgumentOutOfRangeException(nameof(complexity), complexity, "Complexity must not be negative.");

    _graph = graph;
    _options = options;
    _complexity = complexity;
    _seedGraph = seedGraph;
    // Validation runs even when the pass is a no-op, so bad options never pass silently.
    options.Validate();
  }

  /// <summary>
  /// Number of obstacles placed by the last <see cref="Execute"/> call; 0 before the first call.
  /// </summary>
  public int PlacedObstacles { get; private set; }

  /// <summary>
  /// Places up to Complexity obstacles on the graph given to the constructor.
  /// </summary>
  /// <param name="cancellationToken">Token checked before every obstacle.</param>
  /// <exception cref="OperationCanceledException">
  /// The token was cancelled. Obstacles placed so far are complete and the graph is still beatable.
  /// </exception>
  /// <exception cref="ArgumentException">Two nodes share an identifier.</exception>
  /// <exception cref="InvalidOperationException">A passage references a missing node.</exception>
  public void Execute(CancellationToken cancellationToken = default)
  {
    PlacedObstacles = 0;
    if (_complexity == 0) return; // Nothing to place.
    if (_graph.Nodes.Count < MinNodeCount || _graph.Edges.Count == 0) return; // Nothing to lock.

    cancellationToken.ThrowIfCancellationRequested();
    var state = new ObstacleState(_graph, _options, _seedGraph);
    if (state.Work.StartNode == ObstacleWorkGraph.NoNode || state.Work.ExitNode == ObstacleWorkGraph.NoNode)
      return; // Start or exit missing, or spread over several nodes.

    // The main pass skips itself when start and exit share a node.
    new ObstaclePassMain(state).Execute(_complexity, cancellationToken);
    PlacedObstacles = state.ObstacleCount;
  }
}
