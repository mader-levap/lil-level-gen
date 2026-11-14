using System;
using System.Collections.Generic;
using LostInLimbo.LevelGen.Data.Config.Graph.Proc;

namespace LostInLimbo.LevelGen.Internal.Generator.Graph.Processors.Obstacler;

/// <summary>
/// Finds the candidate cuts that separate a target node from a source node, cheapest first.
/// </summary>
/// <remarks>
/// <para>
/// One minimum cut is not enough: obstacles should land at different places along the way.
/// So several cuts are computed per target, each with a terminal grown into the ball of nodes
/// within <c>r</c> hops (<c>r = 0 .. </c><see cref="MaxBallRadius"/>):
/// </para>
/// <list type="bullet">
/// <item>target grown: the cut nearest the target is taken, pushed outwards as the ball grows;</item>
/// <item>source grown: the cut nearest the source is taken, pushed outwards as the ball grows.</item>
/// </list>
/// <para>
/// A ball that swallows the opposite terminal ends its series. Cuts that lock the same
/// passages are reported once; cuts through passages that cannot be locked, and cuts wider
/// than the allowed number of ordinary passages, are dropped.
/// </para>
/// <para>Deterministic for the same graph state; consumes no random numbers.</para>
/// </remarks>
internal sealed class CutSearch
{
  /// <summary>Largest ball radius (in hops) a terminal is grown to.</summary>
  public const int MaxBallRadius = 3;

  /// <summary>Graph to search.</summary>
  private readonly ObstacleWorkGraph _graph;
  /// <summary>Cut finder over the indices of <see cref="_graph"/>.</summary>
  private readonly MinCutFinder _finder;
  /// <summary>Largest allowed <see cref="CutCandidate.OrdinaryWidth"/> (inclusive).</summary>
  private readonly int _maxOrdinaryWidth;

  /// <summary>
  /// Prepares a search over <paramref name="graph"/>.
  /// </summary>
  /// <param name="graph">Graph to search.</param>
  /// <param name="maxOrdinaryWidth">
  /// Largest number of ordinary passages a cut may lock (inclusive), see
  /// <see cref="CnfProcObstaclePlacerOpt.MaxEdgesPerObstacle"/>. Must be at least 1.
  /// </param>
  /// <exception cref="ArgumentNullException"><paramref name="graph"/> is null.</exception>
  /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxOrdinaryWidth"/> is below 1.</exception>
  public CutSearch(ObstacleWorkGraph graph, int maxOrdinaryWidth)
  {
    if (graph is null) throw new ArgumentNullException(nameof(graph));
    if (maxOrdinaryWidth < 1)
      throw new ArgumentOutOfRangeException(nameof(maxOrdinaryWidth), maxOrdinaryWidth, "At least one ordinary passage must be allowed.");

    _graph = graph;
    _finder = graph.CreateMinCutFinder();
    _maxOrdinaryWidth = maxOrdinaryWidth;
  }

  /// <summary>
  /// Finds the candidate cuts between two nodes.
  /// </summary>
  /// <param name="source">Work index of the node the player comes from (the start).</param>
  /// <param name="target">Work index of the node to keep away from the source.</param>
  /// <param name="costs">
  /// Cut cost per edge, e.g. <see cref="ObstacleWorkGraph.BuildMainCutCosts"/>. A cost of
  /// <see cref="ObstacleWorkGraph.CostUncuttable"/> or more marks a passage no cut may use.
  /// </param>
  /// <returns>
  /// Distinct candidates ordered by cost, then by ordinary width, then by discovery (target
  /// series before source series, small radius first). Empty when no valid cut exists, in
  /// particular when source and target are the same node.
  /// </returns>
  /// <exception cref="ArgumentNullException"><paramref name="costs"/> is null.</exception>
  /// <exception cref="ArgumentException"><paramref name="costs"/> does not hold one cost per edge.</exception>
  /// <exception cref="ArgumentOutOfRangeException">A node index is out of range.</exception>
  public List<CutCandidate> FindCandidates(int source, int target, long[] costs)
  {
    if (costs is null) throw new ArgumentNullException(nameof(costs));
    if (costs.Length != _graph.EdgeCount)
      throw new ArgumentException("One cost per edge is required.", nameof(costs));
    if (source < 0 || source >= _graph.NodeCount)
      throw new ArgumentOutOfRangeException(nameof(source), source, "Source is not a node.");
    if (target < 0 || target >= _graph.NodeCount)
      throw new ArgumentOutOfRangeException(nameof(target), target, "Target is not a node.");

    var candidates = new List<CutCandidate>();
    if (source == target) return candidates;

    var seen = new HashSet<string>();
    var single = new bool[_graph.NodeCount];

    // Target series: grow the target, take the cut nearest to it.
    single[source] = true;
    for (var radius = 0; radius <= MaxBallRadius; radius++)
    {
      var ball = _graph.Ball(target, radius);
      if (ball[source]) break;

      var result = _finder.Find(costs, single, ball, ObstacleWorkGraph.CostUncuttable);
      if (!result.Limited) AddCandidate(source, target, costs, result.SinkSide, candidates, seen);
    }

    // Source series: grow the source, take the cut nearest to it.
    single[source] = false;
    single[target] = true;
    for (var radius = 0; radius <= MaxBallRadius; radius++)
    {
      var ball = _graph.Ball(source, radius);
      if (ball[target]) break;

      var result = _finder.Find(costs, ball, single, ObstacleWorkGraph.CostUncuttable);
      if (!result.Limited) AddCandidate(source, target, costs, result.SourceSide, candidates, seen);
    }

    // Stable ordering: List.Sort is unstable, so discovery order is an explicit last key.
    var order = new List<int>(candidates.Count);
    for (var index = 0; index < candidates.Count; index++) order.Add(index);
    order.Sort((left, right) =>
    {
      var byCost = candidates[left].Cost.CompareTo(candidates[right].Cost);
      if (byCost != 0) return byCost;
      var byWidth = candidates[left].OrdinaryWidth.CompareTo(candidates[right].OrdinaryWidth);
      return byWidth != 0 ? byWidth : left.CompareTo(right);
    });

    var sorted = new List<CutCandidate>(candidates.Count);
    foreach (var index in order) sorted.Add(candidates[index]);
    return sorted;
  }

  /// <summary>
  /// Builds the candidate for one flow result and keeps it when it is valid, narrow enough
  /// and not a duplicate.
  /// </summary>
  /// <param name="source">Work index of the source node.</param>
  /// <param name="target">Work index of the target node.</param>
  /// <param name="costs">Cut cost per edge.</param>
  /// <param name="side">One side of the minimum cut, as reported by the finder.</param>
  /// <param name="candidates">Receives the candidate.</param>
  /// <param name="seen">Keys of the lock sets already reported.</param>
  private void AddCandidate(
    int source,
    int target,
    long[] costs,
    bool[] side,
    List<CutCandidate> candidates,
    HashSet<string> seen)
  {
    var cutEdges = _finder.CrossingEdges(costs, side);
    var candidate = _graph.BuildCandidate(source, target, cutEdges, costs);
    if (candidate is null) return;
    if (candidate.OrdinaryWidth > _maxOrdinaryWidth) return;
    if (!seen.Add(string.Join(",", candidate.LockEdges))) return;

    candidates.Add(candidate);
  }
}
