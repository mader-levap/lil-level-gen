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
/// <para>Two passes share the Complexity:</para>
/// <list type="bullet">
/// <item><b>Main pass</b> (<see cref="ObstaclePassMain"/>): obstacles between the start and the
/// exit; skipped when both share a node. Secret passages that would bypass an obstacle are
/// locked with it, except for rare deliberate bypasses
/// (<see cref="CnfProcObstaclePlacerOpt.ChanceSecretBypass"/>,
/// <see cref="CnfProcObstaclePlacerOpt.ChanceBypass"/>). Ends early when only stacking is
/// left and <see cref="CnfProcObstaclePlacerOpt.MaxStackLoad"/> forbids it.</item>
/// <item><b>Side pass</b> (<see cref="ObstaclePassSide"/>): obstacles guarding dead ends
/// (pockets) with a secret node, a goal or an optional goal. Never bypassed.</item>
/// </list>
/// <para>
/// <b>Budget split.</b> The side pass first lists its pockets. Without pockets the main pass
/// gets everything; without a main pass the side pass does. Otherwise the side pass gets:
/// above <see cref="CnfProcObstaclePlacerOpt.SmallComplexityMax"/> the part left by
/// <see cref="CnfProcObstaclePlacerOpt.MainShare"/>; for a small Complexity one point with
/// <see cref="CnfProcObstaclePlacerOpt.ChanceReserveSide"/>; for Complexity 1 that point with
/// <see cref="CnfProcObstaclePlacerOpt.ChanceReserveSideOnly"/>. The side budget never exceeds
/// the number of pockets, and that many pockets are protected: the main pass puts no solution
/// into them. What the main pass could not spend falls to the side pass as well.
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
  /// Number of obstacles the main pass placed in the last <see cref="Execute"/> call.
  /// </summary>
  public int PlacedMainObstacles { get; private set; }

  /// <summary>
  /// Number of obstacles the side pass placed in the last <see cref="Execute"/> call.
  /// </summary>
  public int PlacedSideObstacles { get; private set; }

  /// <summary>
  /// Number of obstacles reserved for the side pass by the budget split of the last
  /// <see cref="Execute"/> call; also the number of protected pockets.
  /// </summary>
  public int ReservedSideBudget { get; private set; }

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
    PlacedMainObstacles = 0;
    PlacedSideObstacles = 0;
    ReservedSideBudget = 0;
    if (_complexity == 0) return; // Nothing to place.
    if (_graph.Nodes.Count < MinNodeCount || _graph.Edges.Count == 0) return; // Nothing to lock.

    cancellationToken.ThrowIfCancellationRequested();
    var state = new ObstacleState(_graph, _options, _seedGraph);
    if (state.Work.StartNode == ObstacleWorkGraph.NoNode || state.Work.ExitNode == ObstacleWorkGraph.NoNode)
      return; // Start or exit missing, or spread over several nodes.

    // The pockets are listed before any obstacle exists, so the budget can be planned on them.
    var sidePass = new ObstaclePassSide(state);
    var pocketCount = sidePass.Scan().Count;
    var hasMainPass = state.Work.StartNode != state.Work.ExitNode;

    ReservedSideBudget = hasMainPass ? SplitOffSideBudget(state, pocketCount) : 0;
    if (hasMainPass)
    {
      var protectedNodes = sidePass.BuildNodeMask(ReservedSideBudget);
      PlacedMainObstacles = new ObstaclePassMain(state, protectedNodes)
        .Execute(_complexity - ReservedSideBudget, cancellationToken);
    }

    // Everything the main pass left, reserved or not.
    PlacedSideObstacles = sidePass.Execute(_complexity - PlacedMainObstacles, cancellationToken);
    PlacedObstacles = state.ObstacleCount;
  }

  /// <summary>
  /// Decides how much of the Complexity is kept back for the side pass; see the budget split
  /// in the type remarks. Rolls only when a reserve is possible.
  /// </summary>
  /// <param name="state">Shared state; provides the random stream.</param>
  /// <param name="pocketCount">Number of pockets the side pass listed.</param>
  /// <returns>Side budget within [0, min(Complexity, <paramref name="pocketCount"/>)].</returns>
  private int SplitOffSideBudget(ObstacleState state, int pocketCount)
  {
    if (pocketCount == 0) return 0; // Nothing to lock on the side.

    int side;
    if (_complexity > _options.SmallComplexityMax)
    {
      // Decimal keeps a share like 0.7 exact, so halves round up as written (5 * 0.7 -> 4).
      var main = (int)Math.Round(_complexity * (decimal)_options.MainShare, MidpointRounding.AwayFromZero);
      side = _complexity - main;
    }
    else
    {
      // Reserving the only point leaves the main pass nothing, hence its own, lower chance.
      var chance = _complexity > 1 ? _options.ChanceReserveSide : _options.ChanceReserveSideOnly;
      side = state.Random.NextBool(chance) ? 1 : 0;
    }

    return Math.Min(side, pocketCount);
  }
}
