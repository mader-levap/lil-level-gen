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
/// <item><b>Stack limit.</b> A stacking cut is only considered while the graph stays within
/// <see cref="CnfProcObstaclePlacerOpt.MaxStackLoad"/> with it (see <see cref="IsStackAllowed"/>);
/// otherwise it is refused without a roll and cannot be forced either. When nothing fresh is
/// left and the limit forbids stacking, the pass ends early.</item>
/// <item><b>Cramped cuts last.</b> A cut that leaves no node but the start for the solution
/// (a door at the start with its key lying next to it) is only used when nothing else is left,
/// even after forced stacking. Such a solution cannot be guarded by a later obstacle either.</item>
/// </list>
/// <para>
/// <b>Bypasses.</b> After the solution is placed, some passages of the cut may be opened again
/// (<see cref="ApplyBypasses"/>): each secret passage with
/// <see cref="CnfProcObstaclePlacerOpt.ChanceSecretBypass"/>, and - one roll per obstacle with
/// <see cref="CnfProcObstaclePlacerOpt.ChanceBypass"/> - one ordinary passage. At least one
/// ordinary passage always stays locked. A bypassed obstacle is no longer needed for the exit.
/// </para>
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
      ApplyBypasses(cut);
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
    _state.Work.CountConditionLoad(out var locked, out var extra);
    CutCandidate? leastStacked = null;
    CutCandidate? cramped = null;

    foreach (var target in targets)
    {
      var all = _state.Search.FindCandidates(start, target, costs);
      var candidates = all.FindAll(HasRoomForSolution);
      if (candidates.Count == 0)
      {
        // Only cramped cuts (or none): keep the cheapest of the first such target as a last resort.
        cramped ??= all.Find(candidate => candidate.MaxConditions == 0 || IsStackAllowed(candidate, locked, extra));
        continue;
      }

      var best = PickCheapest(candidates);
      if (best.MaxConditions == 0) return best;

      // Over the stack limit: refused without a roll, and not available for forced stacking.
      var allowed = IsStackAllowed(best, locked, extra);
      if (allowed)
      {
        var chance = (float)Math.Pow(_state.Options.ChanceStack, best.MaxConditions);
        if (_state.Random.NextBool(chance)) return best;
      }

      // Stacking refused: a fresh cut of the same target is still better than moving on.
      var fresh = FindCheapestFresh(candidates);
      if (fresh != null) return fresh;

      if (allowed && (leastStacked is null || best.MaxConditions < leastStacked.MaxConditions)) leastStacked = best;
    }

    return leastStacked ?? cramped;
  }

  /// <summary>
  /// Tells whether the graph stays within the stack limit when a cut is locked:
  /// <c>extra &lt;= FreeStackedConditions + MaxStackLoad * locked</c>, counted with the cut in place.
  /// </summary>
  /// <param name="candidate">Cut to check; a fresh cut always passes.</param>
  /// <param name="locked">Passages carrying a condition now.</param>
  /// <param name="extra">Stacked conditions now (conditions beyond the first on a passage).</param>
  /// <returns>True when the cut may be locked.</returns>
  private bool IsStackAllowed(CutCandidate candidate, int locked, int extra)
  {
    foreach (var edge in candidate.LockEdges)
    {
      // A first condition locks one more passage; any further one is a stacked condition.
      if (_state.Work.GetEdge(edge).Conditions.Count == 0) locked++;
      else extra++;
    }

    var options = _state.Options;
    return extra <= options.FreeStackedConditions + (double)options.MaxStackLoad * locked;
  }

  /// <summary>
  /// Tells whether a cut leaves the solution somewhere to go besides the start node: a node on
  /// the start side that is neither secret nor protected.
  /// </summary>
  /// <remarks>
  /// A quick structural check; the exact answer (reachability with other obstacles in the way)
  /// is given later by <see cref="ObstacleState.TryPlaceObstacle"/>.
  /// </remarks>
  /// <param name="candidate">Cut to inspect.</param>
  /// <returns>True when at least one such node exists.</returns>
  private bool HasRoomForSolution(CutCandidate candidate)
  {
    var work = _state.Work;
    var far = new bool[work.NodeCount];
    foreach (var node in candidate.FarNodes) far[node] = true;

    for (var node = 0; node < far.Length; node++)
    {
      if (far[node] || node == work.StartNode) continue;
      if (work.GetNode(node).Secret) continue;
      if (_protectedNodes != null && _protectedNodes[node]) continue;
      return true;
    }

    return false;
  }

  #region Bypasses

  /// <summary>Smallest number of ordinary passages in a cut that allows an ordinary bypass.</summary>
  private const int MinOrdinaryWidthForBypass = 2;

  /// <summary>
  /// Opens some passages of the obstacle just placed; see the bypass paragraph of the type remarks.
  /// </summary>
  /// <remarks>
  /// Runs after the solution is placed, so the solution was chosen with the whole cut locked
  /// and stays on the start side. A cut of secret passages only gets no bypass: something
  /// must stay locked.
  /// </remarks>
  /// <param name="cut">Cut of the obstacle placed last; all its passages carry the new condition.</param>
  private void ApplyBypasses(CutCandidate cut)
  {
    if (cut.OrdinaryWidth == 0) return;

    var work = _state.Work;
    var options = _state.Options;

    // Secret bypass: one roll per secret passage, in passage index order.
    foreach (var edge in cut.LockEdges)
    {
      if (work.GetEdge(edge).Secret && _state.Random.NextBool(options.ChanceSecretBypass)) _state.ReopenPassage(edge);
    }

    // Ordinary bypass: one roll per obstacle, and only when another ordinary passage stays locked.
    if (cut.OrdinaryWidth < MinOrdinaryWidthForBypass) return;

    var eligible = FindOrdinaryBypassCandidates(cut);
    if (eligible.Count == 0) return;
    if (!_state.Random.NextBool(options.ChanceBypass)) return;

    _state.ReopenPassage(eligible[_state.Random.NextInt(eligible.Count)]);
  }

  /// <summary>
  /// Lists the ordinary passages of a cut that may be left open: those without a parallel
  /// passage in the same cut. An open door right next to a locked one between the same two
  /// nodes would make the obstacle meaningless.
  /// </summary>
  /// <param name="cut">Cut to inspect.</param>
  /// <returns>Work indices of the eligible passages, in passage index order.</returns>
  private List<int> FindOrdinaryBypassCandidates(CutCandidate cut)
  {
    var work = _state.Work;
    var eligible = new List<int>();
    foreach (var edge in cut.LockEdges)
    {
      if (work.GetEdge(edge).Secret) continue;

      var hasTwin = false;
      foreach (var other in cut.LockEdges)
      {
        if (other == edge) continue;
        var sameWay = work.EdgeFrom(other) == work.EdgeFrom(edge) && work.EdgeTo(other) == work.EdgeTo(edge);
        var opposite = work.EdgeFrom(other) == work.EdgeTo(edge) && work.EdgeTo(other) == work.EdgeFrom(edge);
        if (sameWay || opposite)
        {
          hasTwin = true;
          break;
        }
      }

      if (!hasTwin) eligible.Add(edge);
    }

    return eligible;
  }

  #endregion

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
