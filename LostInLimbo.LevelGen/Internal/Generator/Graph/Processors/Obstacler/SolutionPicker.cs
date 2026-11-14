using System;
using System.Collections.Generic;
using LostInLimbo.LevelGen.Data.Result.Graph;
using LostInLimbo.LevelGen.Internal.Utils.Rng;

namespace LostInLimbo.LevelGen.Internal.Generator.Graph.Processors.Obstacler;

/// <summary>
/// Picks the node that receives the solution of a new obstacle, by weighted random choice
/// among the nodes the player can reach without that solution.
/// </summary>
/// <remarks>
/// <para>
/// Which nodes are safe is decided by the caller (the blocked flood); this class only chooses
/// among them. Secret nodes are never chosen: progress must not depend on finding a secret.
/// </para>
/// <para>Weights favour interesting places:</para>
/// <list type="bullet">
/// <item>every candidate starts at <see cref="WeightBase"/>;</item>
/// <item>a dead end adds <see cref="WeightDeadEnd"/> (a solution at the end of a side branch);</item>
/// <item>each hop away from the locked passages adds <see cref="WeightPerHop"/>, up to
/// <see cref="MaxHopBonus"/> hops (some walking between solution and obstacle);</item>
/// <item>the start node is divided by <see cref="StartDivisor"/> (a solution lying at the
/// player's feet is dull);</item>
/// <item>the result is divided by one plus the number of solutions already in the node
/// (spread solutions out).</item>
/// </list>
/// <para>
/// Every candidate keeps a weight of at least 1, so no safe node is ever impossible.
/// Deterministic for the same generator state: candidates are walked in work-index
/// (identifier) order and one random number is drawn per pick.
/// </para>
/// </remarks>
internal sealed class SolutionPicker
{
  /// <summary>Weight every candidate starts with.</summary>
  public const int WeightBase = 4;
  /// <summary>Weight added for a dead-end node.</summary>
  public const int WeightDeadEnd = 8;
  /// <summary>Weight added per hop between the node and the nearest locked passage.</summary>
  public const int WeightPerHop = 2;
  /// <summary>Largest number of hops that still adds weight.</summary>
  public const int MaxHopBonus = 3;
  /// <summary>Divisor applied to the start node.</summary>
  public const int StartDivisor = 4;

  /// <summary>Graph the nodes belong to.</summary>
  private readonly ObstacleWorkGraph _graph;
  /// <summary>Random stream of the obstacle placer.</summary>
  private readonly SeededRandom _random;

  /// <summary>
  /// Prepares a picker.
  /// </summary>
  /// <param name="graph">Graph the nodes belong to.</param>
  /// <param name="random">Random stream; one draw per successful pick.</param>
  /// <exception cref="ArgumentNullException">A parameter is null.</exception>
  public SolutionPicker(ObstacleWorkGraph graph, SeededRandom random)
  {
    _graph = graph ?? throw new ArgumentNullException(nameof(graph));
    _random = random ?? throw new ArgumentNullException(nameof(random));
  }

  /// <summary>
  /// Picks the node for a new solution.
  /// </summary>
  /// <param name="reachable">Flags the nodes the player can reach without the new solution.</param>
  /// <param name="lockEdges">Passages the new obstacle locks; hop distances are measured from them.</param>
  /// <param name="excluded">Nodes that must not be picked; null excludes nothing.</param>
  /// <returns>
  /// Work index of the picked node, or <see cref="ObstacleWorkGraph.NoNode"/> when no node
  /// qualifies (no random number is drawn then).
  /// </returns>
  /// <exception cref="ArgumentNullException"><paramref name="reachable"/> or <paramref name="lockEdges"/> is null.</exception>
  /// <exception cref="ArgumentException">A flag array does not hold one flag per node.</exception>
  public int Pick(bool[] reachable, IReadOnlyList<int> lockEdges, bool[]? excluded)
  {
    if (reachable is null) throw new ArgumentNullException(nameof(reachable));
    if (lockEdges is null) throw new ArgumentNullException(nameof(lockEdges));
    if (reachable.Length != _graph.NodeCount)
      throw new ArgumentException("One flag per node is required.", nameof(reachable));
    if (excluded != null && excluded.Length != _graph.NodeCount)
      throw new ArgumentException("One flag per node is required.", nameof(excluded));

    var hops = MeasureHops(reachable, lockEdges);
    var weights = new int[_graph.NodeCount];
    var total = 0;
    for (var node = 0; node < weights.Length; node++)
    {
      if (!reachable[node]) continue;
      if (excluded != null && excluded[node]) continue;
      if (_graph.GetNode(node).Secret) continue;

      weights[node] = ComputeWeight(node, hops[node]);
      total += weights[node];
    }

    if (total == 0) return ObstacleWorkGraph.NoNode;

    // Roll once on the cumulative weights and walk the candidates to the picked one.
    var roll = _random.NextInt(total);
    for (var node = 0; node < weights.Length; node++)
    {
      roll -= weights[node];
      if (roll < 0) return node;
    }

    throw new InvalidOperationException("Solution weight roll landed outside the candidate list.");
  }

  /// <summary>
  /// Computes the pick weight of a candidate node; see the type remarks for the rules.
  /// </summary>
  /// <param name="node">Work index of the node.</param>
  /// <param name="hops">Hops between the node and the nearest locked passage; 0 right next to it.</param>
  /// <returns>Weight of at least 1.</returns>
  public int ComputeWeight(int node, int hops)
  {
    var weight = WeightBase + WeightPerHop * Math.Min(Math.Max(hops, 0), MaxHopBonus);
    if (_graph.IsDeadEnd(node)) weight += WeightDeadEnd;
    if (node == _graph.StartNode) weight /= StartDivisor;

    var solutions = 0;
    foreach (var poi in _graph.GetNode(node).Pois)
    {
      if (poi.Type == EnPoiType.Solution) solutions++;
    }

    return Math.Max(1, weight / (1 + solutions));
  }

  /// <summary>
  /// Measures, inside the reachable area, the hop distance of every node from the locked
  /// passages: 0 for a reachable endpoint of a locked passage, one more per passage walked.
  /// </summary>
  /// <param name="reachable">Flags the nodes the player can reach.</param>
  /// <param name="lockEdges">Locked passages to measure from.</param>
  /// <returns>Distance per node; 0 for nodes that are not reachable or not connected to a lock.</returns>
  private int[] MeasureHops(bool[] reachable, IReadOnlyList<int> lockEdges)
  {
    var hops = new int[_graph.NodeCount];
    var visited = new bool[_graph.NodeCount];
    var queue = new Queue<int>();

    foreach (var edge in lockEdges)
    {
      Seed(_graph.EdgeFrom(edge));
      Seed(_graph.EdgeTo(edge));
    }

    while (queue.Count > 0)
    {
      var node = queue.Dequeue();
      foreach (var edge in _graph.IncidentEdges(node))
      {
        var next = _graph.OtherEnd(edge, node);
        if (visited[next] || !reachable[next]) continue;

        visited[next] = true;
        hops[next] = hops[node] + 1;
        queue.Enqueue(next);
      }
    }

    return hops;

    // Starts the measurement at a reachable endpoint of a locked passage.
    void Seed(int node)
    {
      if (visited[node] || !reachable[node]) return;
      visited[node] = true;
      queue.Enqueue(node);
    }
  }
}
