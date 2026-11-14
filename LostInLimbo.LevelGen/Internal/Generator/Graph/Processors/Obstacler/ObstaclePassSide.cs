using System;
using System.Collections.Generic;
using System.Threading;
using LostInLimbo.LevelGen.Data.Config.Graph.Proc;
using LostInLimbo.LevelGen.Data.Result.Graph;
using LostInLimbo.LevelGen.Internal.Generator.Graph.Meta;

namespace LostInLimbo.LevelGen.Internal.Generator.Graph.Processors.Obstacler;

/// <summary>
/// Side pass of the obstacle placer: obstacles that guard dead ends holding a treasure - a
/// secret node, a goal or an optional goal.
/// </summary>
/// <remarks>
/// <para>
/// A <b>pocket</b> (<see cref="ObstaclePocket"/>) is a part of the graph joined to the rest
/// through one node pair only. One obstacle locks every passage of that pair that leads into
/// the pocket: two-way ones and one-way ones pointing in. A one-way passage pointing out stays
/// open, so of an <c>A -> S -> A</c> secret loop only the entry is locked.
/// </para>
/// <para>The work is split in two steps, so the budget can be planned in between:</para>
/// <list type="number">
/// <item><see cref="Scan"/> lists the pockets to lock. Treasure nodes are walked in shuffled
/// order; each one not yet covered gets the smallest or the largest valid pocket around it
/// (roll with <see cref="ChanceLargestPocket"/>). Listed pockets never overlap, so a pocket
/// with several treasures costs one obstacle.</item>
/// <item><see cref="Execute"/> walks that list in order. Every pocket is validated again,
/// because obstacles placed in between may have spoiled it; a spoiled one is skipped.</item>
/// </list>
/// <para>
/// A pocket is <b>valid</b> when: no passage of the pair carries a condition; it has at least
/// one and at most <see cref="CnfProcObstaclePlacerOpt.MaxEdgesPerObstacle"/> entry passages;
/// it holds no Start, Exit or Solution POI and no conditioned passage; and it is not larger
/// than a <see cref="MaxPocketNodeShareDivisor"/>-th of the graph.
/// </para>
/// <para>
/// Solutions of this pass never go into a listed pocket, so side obstacles never depend on
/// each other. Deterministic: pockets are found in passage index order, rolls come from the
/// shared random stream.
/// </para>
/// </remarks>
internal sealed class ObstaclePassSide
{
  /// <summary>Chance that a treasure with several valid pockets gets the largest instead of the smallest.</summary>
  public const float ChanceLargestPocket = 0.5f;

  /// <summary>
  /// A pocket holds at most one part in this many of all nodes (half the graph): a larger
  /// "pocket" is the level itself, seen from a start node in a dead end.
  /// </summary>
  public const int MaxPocketNodeShareDivisor = 2;

  /// <summary>Shared state of the run.</summary>
  private readonly ObstacleState _state;
  /// <summary>Pockets listed by <see cref="Scan"/>; null before the scan.</summary>
  private List<ObstaclePocket>? _pockets;

  /// <summary>
  /// Prepares the pass.
  /// </summary>
  /// <param name="state">Shared state; its graph must have a single start node.</param>
  /// <exception cref="ArgumentNullException"><paramref name="state"/> is null.</exception>
  public ObstaclePassSide(ObstacleState state)
  {
    _state = state ?? throw new ArgumentNullException(nameof(state));
  }

  #region Scan

  /// <summary>
  /// Lists the pockets this pass wants to lock, in the order it will lock them. The first call
  /// does the work and the rolls; later calls return the same list.
  /// </summary>
  /// <returns>Valid, non-overlapping pockets; empty when the graph has none or no start node.</returns>
  public IReadOnlyList<ObstaclePocket> Scan()
  {
    if (_pockets != null) return _pockets;

    _pockets = [];
    var work = _state.Work;
    if (work.StartNode == ObstacleWorkGraph.NoNode) return _pockets;

    var valid = new List<ObstaclePocket>();
    foreach (var pocket in FindAllPockets())
    {
      if (IsValid(pocket)) valid.Add(pocket);
    }

    if (valid.Count == 0) return _pockets; // No roll is spent on a graph without pockets.

    var treasures = new List<int>();
    for (var node = 0; node < work.NodeCount; node++)
    {
      if (IsTreasure(node)) treasures.Add(node);
    }

    _state.Random.Shuffle(treasures);

    var covered = new bool[work.NodeCount];
    foreach (var treasure in treasures)
    {
      if (covered[treasure]) continue; // Already inside a listed pocket.

      var pocket = ChoosePocket(treasure, valid, covered);
      if (pocket is null) continue;

      _pockets.Add(pocket);
      foreach (var node in pocket.Nodes) covered[node] = true;
    }

    return _pockets;
  }

  /// <summary>
  /// Flags the nodes of the first listed pockets; used to keep solutions out of them.
  /// </summary>
  /// <param name="pocketCount">Number of pockets to take from the start of the list; clamped to the list.</param>
  /// <returns>Flag per node work index: inside one of those pockets.</returns>
  public bool[] BuildNodeMask(int pocketCount)
  {
    var pockets = Scan();
    var mask = new bool[_state.Work.NodeCount];
    for (var index = 0; index < pockets.Count && index < pocketCount; index++)
    {
      foreach (var node in pockets[index].Nodes) mask[node] = true;
    }

    return mask;
  }

  /// <summary>
  /// Chooses the pocket of one treasure: the smallest or the largest valid pocket around it
  /// that does not touch an already listed one.
  /// </summary>
  /// <param name="treasure">Work index of the treasure node.</param>
  /// <param name="valid">All valid pockets of the graph.</param>
  /// <param name="covered">Flag per node: inside an already listed pocket.</param>
  /// <returns>The chosen pocket, or <see langword="null"/> when the treasure is in no usable pocket.</returns>
  private ObstaclePocket? ChoosePocket(int treasure, List<ObstaclePocket> valid, bool[] covered)
  {
    ObstaclePocket? smallest = null;
    ObstaclePocket? largest = null;
    foreach (var pocket in valid)
    {
      if (!pocket.Contains(treasure) || Overlaps(pocket, covered)) continue;

      if (smallest is null || pocket.Nodes.Count < smallest.Nodes.Count) smallest = pocket;
      if (largest is null || pocket.Nodes.Count > largest.Nodes.Count) largest = pocket;
    }

    if (smallest is null || largest is null) return null;
    if (ReferenceEquals(smallest, largest)) return smallest; // One choice needs no roll.

    return _state.Random.NextBool(ChanceLargestPocket) ? largest : smallest;
  }

  /// <summary>Tells whether a pocket shares a node with the already listed pockets.</summary>
  /// <param name="pocket">Pocket to check.</param>
  /// <param name="covered">Flag per node: inside an already listed pocket.</param>
  /// <returns>True when at least one node is shared.</returns>
  private static bool Overlaps(ObstaclePocket pocket, bool[] covered)
  {
    foreach (var node in pocket.Nodes)
    {
      if (covered[node]) return true;
    }

    return false;
  }

  /// <summary>
  /// Tells whether a node is worth guarding: a secret node, or one with a goal or optional goal.
  /// </summary>
  /// <param name="node">Work index of the node.</param>
  /// <returns>True for a treasure node.</returns>
  private bool IsTreasure(int node)
  {
    var data = _state.Work.GetNode(node);
    return data.Secret || data.HasPoi(EnPoiType.Goal) || data.HasPoi(EnPoiType.OptionalGoal);
  }

  #endregion

  #region Pocket search

  /// <summary>
  /// Finds every pocket of the graph: for each pair of joined nodes, takes all their passages
  /// away and checks whether one of the two loses its way to the start.
  /// </summary>
  /// <returns>All pockets, valid or not, in order of the first passage of their pair.</returns>
  private List<ObstaclePocket> FindAllPockets()
  {
    var work = _state.Work;
    var pockets = new List<ObstaclePocket>();
    var seenPairs = new HashSet<long>(); // Membership only; never iterated.

    for (var edge = 0; edge < work.EdgeCount; edge++)
    {
      var low = Math.Min(work.EdgeFrom(edge), work.EdgeTo(edge));
      var high = Math.Max(work.EdgeFrom(edge), work.EdgeTo(edge));
      if (!seenPairs.Add((long)low * work.NodeCount + high)) continue;

      var pocket = FindPocketBehindPair(low, high);
      if (pocket != null) pockets.Add(pocket);
    }

    return pockets;
  }

  /// <summary>
  /// Builds the pocket hanging on one node pair, if the pair is the only link of some nodes
  /// to the start.
  /// </summary>
  /// <param name="first">One node of the pair.</param>
  /// <param name="second">The other node of the pair.</param>
  /// <returns>The pocket, or <see langword="null"/> when both nodes still reach the start.</returns>
  private ObstaclePocket? FindPocketBehindPair(int first, int second)
  {
    var work = _state.Work;
    var nearStart = FloodWithoutPair(work.StartNode, first, second);
    if (nearStart[first] == nearStart[second]) return null; // A way around exists (or neither reaches the start).

    var gate = nearStart[first] ? second : first;
    var host = nearStart[first] ? first : second;
    var inside = FloodWithoutPair(gate, first, second);

    var pairEdges = new List<int>();
    var entryEdges = new List<int>();
    foreach (var edge in work.IncidentEdges(gate))
    {
      if (work.OtherEnd(edge, gate) != host) continue;

      pairEdges.Add(edge);
      var oneWay = work.GetEdge(edge).Direction != EnPassageDirection.Bidirectional;
      // A one-way passage leads From -> To: it enters the pocket only when it ends at the gate.
      if (!oneWay || work.EdgeTo(edge) == gate) entryEdges.Add(edge);
    }

    pairEdges.Sort();
    entryEdges.Sort();
    return new ObstaclePocket(gate, host, inside, pairEdges, entryEdges);
  }

  /// <summary>
  /// Floods over every passage, in any direction, except those joining the given node pair.
  /// </summary>
  /// <param name="origin">Node the flood starts at.</param>
  /// <param name="first">One node of the removed pair.</param>
  /// <param name="second">The other node of the removed pair.</param>
  /// <returns>Flag per node work index: reached.</returns>
  private bool[] FloodWithoutPair(int origin, int first, int second)
  {
    var work = _state.Work;
    var reached = new bool[work.NodeCount];
    var queue = new Queue<int>();
    reached[origin] = true;
    queue.Enqueue(origin);

    while (queue.Count > 0)
    {
      var node = queue.Dequeue();
      foreach (var edge in work.IncidentEdges(node))
      {
        var next = work.OtherEnd(edge, node);
        if (reached[next]) continue;
        if ((node == first && next == second) || (node == second && next == first)) continue;

        reached[next] = true;
        queue.Enqueue(next);
      }
    }

    return reached;
  }

  /// <summary>
  /// Checks the pocket rules against the graph as it is now; see the type remarks.
  /// </summary>
  /// <param name="pocket">Pocket to check.</param>
  /// <returns>True when the pocket may be locked by one obstacle.</returns>
  private bool IsValid(ObstaclePocket pocket)
  {
    var work = _state.Work;
    if (pocket.EntryEdges.Count == 0 || pocket.EntryEdges.Count > _state.Options.MaxEdgesPerObstacle) return false;
    if (pocket.Nodes.Count * MaxPocketNodeShareDivisor > work.NodeCount) return false;

    foreach (var edge in pocket.PairEdges)
    {
      if (work.GetEdge(edge).Conditions.Count > 0) return false;
    }

    foreach (var node in pocket.Nodes)
    {
      var data = work.GetNode(node);
      if (data.HasPoi(EnPoiType.Start) || data.HasPoi(EnPoiType.Exit) || data.HasPoi(EnPoiType.Solution)) return false;

      // Every passage of a pocket node other than the pair lies inside the pocket.
      foreach (var edge in work.IncidentEdges(node))
      {
        if (work.GetEdge(edge).Conditions.Count > 0) return false;
      }
    }

    return true;
  }

  #endregion

  #region Execute

  /// <summary>
  /// Locks up to <paramref name="budget"/> listed pockets, one obstacle each, in list order.
  /// </summary>
  /// <param name="budget">Largest number of obstacles to place; 0 or less places none.</param>
  /// <param name="cancellationToken">Token checked before every obstacle.</param>
  /// <returns>Number of obstacles placed; less than the budget when the usable pockets run out.</returns>
  /// <exception cref="OperationCanceledException">The token was cancelled; obstacles placed so far stay valid.</exception>
  public int Execute(int budget, CancellationToken cancellationToken = default)
  {
    var pockets = Scan();
    if (budget <= 0 || pockets.Count == 0) return 0;

    // No solution of this pass goes into any listed pocket, locked or not.
    var excluded = BuildNodeMask(pockets.Count);
    var placed = 0;

    foreach (var pocket in pockets)
    {
      if (placed >= budget) break;
      cancellationToken.ThrowIfCancellationRequested();

      if (!IsValid(pocket)) continue; // Spoiled by an obstacle placed after the scan.
      if (_state.TryPlaceObstacle(pocket.EntryEdges, excluded, out _, allowOneWay: true)) placed++;
    }

    return placed;
  }

  #endregion
}
