using System;
using System.Collections.Generic;
using System.Threading;
using LostInLimbo.LevelGen.Data.Config.Graph.Proc;

namespace LostInLimbo.LevelGen.Internal.Generator.Graph.Processors.Obstacler;

/// <summary>
/// Main pass of the obstacle placer: obstacles between the start and the exit.
/// </summary>
/// <remarks>
/// <para>
/// The pass keeps a list of <b>required nodes</b> - nodes the player must visit to finish:
/// first only the exit, later also every node holding a solution placed by this pass. Each
/// obstacle cuts one required node off from the start, so every obstacle of this pass is
/// needed to reach the exit: either directly, or because it guards the solution of another one.
/// </para>
/// <para>Per obstacle:</para>
/// <list type="number">
/// <item><b>Target order.</b> The exit comes first for the first obstacle. Afterwards the node
/// of the most recent solution comes first with
/// <see cref="CnfProcObstaclePlacerOpt.ChanceNestSolution"/> (a solution behind another
/// obstacle), otherwise the exit. The other required nodes follow in shuffled order.</item>
/// <item><b>Cut per target.</b> The cheapest candidate cut is taken, ties broken by roll. A
/// fresh cut (no passage of it carries a condition) is accepted at once. A stacking cut is
/// accepted with chance <c>ChanceStack ^ c</c>, where <c>c</c> is the highest condition count
/// on its passages; when refused, the cheapest fresh cut of the same target is taken instead,
/// and without one the next target is tried.</item>
/// <item><b>Forced stacking.</b> When no target yields an accepted cut, the least stacked of
/// the refused cuts is used, so Complexity is not wasted on graphs where stacking is the only
/// option. With no cut at all the pass ends early.</item>
/// </list>
/// <para>
/// Start and exit next to each other need no special case: every cut for the exit contains the
/// direct passage, so after the first obstacle it can only be stacked, which is rare; later
/// obstacles mostly lock away earlier solutions.
/// </para>
/// <para>Deterministic for the same generator state.</para>
/// </remarks>
internal sealed class ObstaclePassMain
{
  /// <summary>Shared state of the run.</summary>
  private readonly ObstacleState _state;
  /// <summary>Flag per node: must not receive a solution (protected side pockets); null protects nothing.</summary>
  private readonly bool[]? _protectedNodes;

  /// <summary>
  /// Prepares the pass.
  /// </summary>
  /// <param name="state">Shared state; its graph must have a single start node and a single exit node.</param>
  /// <param name="protectedNodes">
  /// Flag per node work index: nodes that must not receive a solution of this pass - the
  /// pockets reserved for the side pass. Null protects nothing.
  /// </param>
  /// <exception cref="ArgumentNullException"><paramref name="state"/> is null.</exception>
  /// <exception cref="ArgumentException"><paramref name="protectedNodes"/> does not hold one flag per node.</exception>
  public ObstaclePassMain(ObstacleState state, bool[]? protectedNodes = null)
  {
    _state = state ?? throw new ArgumentNullException(nameof(state));
    if (protectedNodes != null && protectedNodes.Length != state.Work.NodeCount)
      throw new ArgumentException("One flag per node is required.", nameof(protectedNodes));

    _protectedNodes = protectedNodes;
  }

  /// <summary>
  /// Places up to <paramref name="budget"/> obstacles.
  /// </summary>
  /// <param name="budget">Largest number of obstacles to place; 0 or less places none.</param>
  /// <param name="cancellationToken">Token checked before every obstacle.</param>
  /// <returns>Number of obstacles placed; less than the budget when no further cut exists.</returns>
  /// <exception cref="OperationCanceledException">The token was cancelled; obstacles placed so far stay valid.</exception>
  public int Execute(int budget, CancellationToken cancellationToken = default)
  {
    var work = _state.Work;
    var start = work.StartNode;
    var exit = work.ExitNode;
    if (start == ObstacleWorkGraph.NoNode || exit == ObstacleWorkGraph.NoNode || start == exit) return 0;

    var required = new List<int> { exit };
    var lastSolutionNode = ObstacleWorkGraph.NoNode;
    var placed = 0;

    while (placed < budget)
    {
      cancellationToken.ThrowIfCancellationRequested();

      var targets = OrderTargets(required, exit, lastSolutionNode, isFirst: placed == 0);
      var cut = ChooseCut(start, targets);
      if (cut is null) break; // Nothing left to lock.

      if (!_state.TryPlaceObstacle(cut.LockEdges, _protectedNodes, out var solutionNode)) break;

      placed++;
      if (solutionNode == start)
      {
        // A solution in the start node cannot be locked away.
        lastSolutionNode = ObstacleWorkGraph.NoNode;
      }
      else
      {
        lastSolutionNode = solutionNode;
        if (!required.Contains(solutionNode)) required.Add(solutionNode);
      }
    }

    return placed;
  }

  /// <summary>
  /// Orders the required nodes for the next obstacle; see step 1 of the type remarks.
  /// </summary>
  /// <param name="required">Required nodes; always contains the exit.</param>
  /// <param name="exit">Work index of the exit node.</param>
  /// <param name="lastSolutionNode">Node of the most recent solution, or <see cref="ObstacleWorkGraph.NoNode"/>.</param>
  /// <param name="isFirst">True for the first obstacle of the pass.</param>
  /// <returns>Every required node once, most wanted first.</returns>
  private List<int> OrderTargets(List<int> required, int exit, int lastSolutionNode, bool isFirst)
  {
    var favourite = exit;
    if (!isFirst
        && lastSolutionNode != ObstacleWorkGraph.NoNode
        && lastSolutionNode != exit
        && _state.Random.NextBool(_state.Options.ChanceNestSolution))
    {
      favourite = lastSolutionNode;
    }

    // The rest in identifier order first, so the shuffle does not depend on insertion order.
    var rest = new List<int>(required.Count);
    foreach (var node in required)
    {
      if (node != favourite) rest.Add(node);
    }

    rest.Sort();
    _state.Random.Shuffle(rest);

    var targets = new List<int>(required.Count) { favourite };
    targets.AddRange(rest);
    return targets;
  }

  /// <summary>
  /// Walks the targets and chooses the cut of the next obstacle; see steps 2 and 3 of the type
  /// remarks.
  /// </summary>
  /// <param name="start">Work index of the start node.</param>
  /// <param name="targets">Required nodes, most wanted first.</param>
  /// <returns>The chosen cut, or <see langword="null"/> when no target can be cut off.</returns>
  private CutCandidate? ChooseCut(int start, List<int> targets)
  {
    // Costs follow the conditions, so they are rebuilt for every obstacle.
    var costs = _state.Work.BuildMainCutCosts();
    CutCandidate? leastStacked = null;

    foreach (var target in targets)
    {
      var candidates = _state.Search.FindCandidates(start, target, costs);
      if (candidates.Count == 0) continue;

      var best = PickCheapest(candidates);
      if (best.MaxConditions == 0) return best;

      var chance = (float)Math.Pow(_state.Options.ChanceStack, best.MaxConditions);
      if (_state.Random.NextBool(chance)) return best;

      // Stacking refused: a fresh cut of the same target is still better than moving on.
      var fresh = FindCheapestFresh(candidates);
      if (fresh != null) return fresh;

      if (leastStacked is null || best.MaxConditions < leastStacked.MaxConditions) leastStacked = best;
    }

    return leastStacked;
  }

  /// <summary>
  /// Picks among the cheapest candidates; several equally cheap ones are decided by roll.
  /// </summary>
  /// <param name="candidates">Candidates ordered cheapest first; not empty.</param>
  /// <returns>One of the candidates sharing the lowest cost.</returns>
  private CutCandidate PickCheapest(List<CutCandidate> candidates)
  {
    var ties = 1;
    while (ties < candidates.Count && candidates[ties].Cost == candidates[0].Cost) ties++;
    return ties == 1 ? candidates[0] : candidates[_state.Random.NextInt(ties)];
  }

  /// <summary>
  /// Finds the cheapest candidate that stacks onto nothing.
  /// </summary>
  /// <param name="candidates">Candidates ordered cheapest first.</param>
  /// <returns>The first fresh candidate, or <see langword="null"/> when all of them stack.</returns>
  private static CutCandidate? FindCheapestFresh(List<CutCandidate> candidates)
  {
    foreach (var candidate in candidates)
    {
      if (candidate.MaxConditions == 0) return candidate;
    }

    return null;
  }
}
