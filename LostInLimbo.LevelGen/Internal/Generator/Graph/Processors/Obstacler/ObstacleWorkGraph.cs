using System;
using System.Collections.Generic;
using LostInLimbo.LevelGen.Data.Config.Graph.Proc;
using LostInLimbo.LevelGen.Data.Result.Graph;
using LostInLimbo.LevelGen.Internal.Generator.Graph.Meta;

namespace LostInLimbo.LevelGen.Internal.Generator.Graph.Processors.Obstacler;

/// <summary>
/// Index view of a level graph for the obstacle placer: nodes and passages addressed by small
/// integers, plus the cut costs and the side assignment of a cut.
/// </summary>
/// <remarks>
/// <para>
/// <b>Work indices.</b> Node <c>i</c> is the node with the <c>i</c>-th smallest identifier,
/// edge <c>e</c> the passage with the <c>e</c>-th smallest identifier. They are not positions
/// in <see cref="LilGraph.Nodes"/> or <see cref="LilGraph.Edges"/>, so results never depend on
/// list order. Self-loops are left out of the view.
/// </para>
/// <para>
/// <b>Parallel passages.</b> Every passage is its own edge, even when several join the same two
/// nodes. Nothing here is keyed by node pair.
/// </para>
/// <para>
/// The structure (nodes, passages, endpoints) is captured at construction and must not change
/// afterwards. Conditions and POIs are read live, so obstacles added later are seen.
/// For structural purposes (hops, sides of a cut) every passage counts as two-way.
/// </para>
/// </remarks>
internal sealed class ObstacleWorkGraph
{
  #region Constants

  /// <summary>Returned where a node is asked for but none qualifies.</summary>
  public const int NoNode = -1;

  /// <summary>
  /// Cut cost of a passage that must never be locked. Far above any sum of real costs, so a
  /// cut of this weight or more means "no valid cut".
  /// </summary>
  public const long CostUncuttable = 1L << 40;

  /// <summary>Cut cost of a chokepoint passage (single physical opening): the preferred place for an obstacle.</summary>
  public const long CostChokepoint = 4;

  /// <summary>
  /// Cut cost of a non-chokepoint passage. Higher than <see cref="CostChokepoint"/>, yet below
  /// two chokepoints: one wide passage still beats locking two passages.
  /// </summary>
  public const long CostOpen = 6;

  /// <summary>Cut cost added per condition already on the passage, so fresh passages are preferred.</summary>
  public const long CostStack = 2;

  /// <summary>Cut cost of a secret passage: free, it never makes a bottleneck look wider.</summary>
  public const long CostSecret = 0;

  #endregion

  #region State

  /// <summary>Limits of the obstacle placer.</summary>
  private readonly CnfProcObstaclePlacerOpt _options;
  /// <summary>Nodes by work index.</summary>
  private readonly LilNode[] _nodes;
  /// <summary>Passages by work index.</summary>
  private readonly LilEdge[] _edges;
  /// <summary>Work index of <see cref="LilEdge.From"/> per edge.</summary>
  private readonly int[] _edgeFrom;
  /// <summary>Work index of <see cref="LilEdge.To"/> per edge.</summary>
  private readonly int[] _edgeTo;
  /// <summary>Incident edge indices per node, ascending.</summary>
  private readonly List<int>[] _incident;

  #endregion

  #region Construction

  /// <summary>
  /// Builds the index view of <paramref name="graph"/>.
  /// </summary>
  /// <param name="graph">Graph to index; only read here.</param>
  /// <param name="options">Obstacle placer options; supply the per-passage condition cap.</param>
  /// <exception cref="ArgumentNullException">A parameter is null.</exception>
  /// <exception cref="ArgumentException">Two nodes share an identifier.</exception>
  /// <exception cref="InvalidOperationException">A passage references a missing node.</exception>
  public ObstacleWorkGraph(LilGraph graph, CnfProcObstaclePlacerOpt options)
  {
    if (graph is null) throw new ArgumentNullException(nameof(graph));
    if (options is null) throw new ArgumentNullException(nameof(options));

    _options = options;

    var nodeOrder = GraphIndex.NodeVisitOrder(graph);
    _nodes = new LilNode[nodeOrder.Count];
    var workIndexById = new Dictionary<Guid, int>(nodeOrder.Count);
    for (var index = 0; index < nodeOrder.Count; index++)
    {
      _nodes[index] = graph.Nodes[nodeOrder[index]];
      workIndexById.Add(_nodes[index].Id, index);
    }

    var edges = new List<LilEdge>(graph.Edges.Count);
    var edgeFrom = new List<int>(graph.Edges.Count);
    var edgeTo = new List<int>(graph.Edges.Count);
    foreach (var listIndex in GraphIndex.EdgeVisitOrder(graph))
    {
      var edge = graph.Edges[listIndex];
      var (from, to) = GraphIndex.ResolveEndpoints(workIndexById, edge);
      if (from == to) continue; // A self-loop separates nothing.

      edges.Add(edge);
      edgeFrom.Add(from);
      edgeTo.Add(to);
    }

    _edges = [.. edges];
    _edgeFrom = [.. edgeFrom];
    _edgeTo = [.. edgeTo];

    _incident = new List<int>[_nodes.Length];
    for (var node = 0; node < _nodes.Length; node++) _incident[node] = [];
    for (var edge = 0; edge < _edges.Length; edge++)
    {
      _incident[_edgeFrom[edge]].Add(edge);
      _incident[_edgeTo[edge]].Add(edge);
    }

    StartNode = FindSingleNodeWithPoi(EnPoiType.Start);
    ExitNode = FindSingleNodeWithPoi(EnPoiType.Exit);
    MainTargets = FindMainTargets();
  }

  #endregion

  #region Structure

  /// <summary>Number of nodes.</summary>
  public int NodeCount => _nodes.Length;

  /// <summary>Number of passages in the view (self-loops excluded).</summary>
  public int EdgeCount => _edges.Length;

  /// <summary>
  /// The only node holding a <see cref="EnPoiType.Start"/> POI, or <see cref="NoNode"/> when
  /// no node or more than one node holds one. Captured at construction.
  /// </summary>
  public int StartNode { get; }

  /// <summary>
  /// The only node holding an <see cref="EnPoiType.Exit"/> POI, or <see cref="NoNode"/> when
  /// no node or more than one node holds one. Captured at construction.
  /// </summary>
  public int ExitNode { get; }

  /// <summary>
  /// Nodes the main pass cuts off the start, in ascending work index. Captured at construction.
  /// </summary>
  /// <remarks>
  /// <list type="bullet">
  /// <item>Start and exit in different nodes: the exit node only.</item>
  /// <item>Start and exit in one node: every node with a <see cref="EnPoiType.Goal"/> POI except
  /// the start node; when there is none, every node with an <see cref="EnPoiType.OptionalGoal"/>
  /// POI except the start node. The level is "fetch the goals and come back".</item>
  /// <item>Start or exit missing or spread over several nodes: empty.</item>
  /// </list>
  /// </remarks>
  public IReadOnlyList<int> MainTargets { get; }

  /// <summary>Gets a node.</summary>
  /// <param name="node">Work index of the node.</param>
  /// <returns>The node.</returns>
  public LilNode GetNode(int node) => _nodes[node];

  /// <summary>Gets a passage.</summary>
  /// <param name="edge">Work index of the passage.</param>
  /// <returns>The passage.</returns>
  public LilEdge GetEdge(int edge) => _edges[edge];

  /// <summary>Gets the node at the <see cref="LilEdge.From"/> end of a passage.</summary>
  /// <param name="edge">Work index of the passage.</param>
  /// <returns>Work index of the node.</returns>
  public int EdgeFrom(int edge) => _edgeFrom[edge];

  /// <summary>Gets the node at the <see cref="LilEdge.To"/> end of a passage.</summary>
  /// <param name="edge">Work index of the passage.</param>
  /// <returns>Work index of the node.</returns>
  public int EdgeTo(int edge) => _edgeTo[edge];

  /// <summary>Gets the endpoint of a passage opposite to the given one.</summary>
  /// <param name="edge">Work index of the passage.</param>
  /// <param name="node">One endpoint of the passage.</param>
  /// <returns>Work index of the other endpoint.</returns>
  public int OtherEnd(int edge, int node) => _edgeFrom[edge] == node ? _edgeTo[edge] : _edgeFrom[edge];

  /// <summary>Gets the passages touching a node.</summary>
  /// <param name="node">Work index of the node.</param>
  /// <returns>Work indices of the passages, ascending; parallel passages listed separately.</returns>
  public IReadOnlyList<int> IncidentEdges(int node) => _incident[node];

  /// <summary>
  /// Checks whether a node is a dead end: all its ordinary (non-secret) passages lead to at
  /// most one other node. Parallel passages to the same neighbour do not change that, and a
  /// node whose only passages are secret counts as a dead end too.
  /// </summary>
  /// <param name="node">Work index of the node.</param>
  /// <returns>True when the node has at most one distinct ordinary neighbour.</returns>
  public bool IsDeadEnd(int node)
  {
    var neighbour = NoNode;
    foreach (var edge in _incident[node])
    {
      if (_edges[edge].Secret) continue;

      var other = OtherEnd(edge, node);
      if (neighbour == NoNode) neighbour = other;
      else if (other != neighbour) return false;
    }

    return true;
  }

  /// <summary>
  /// Creates a cut finder over the same node and edge indices.
  /// </summary>
  /// <returns>A new finder; it shares no mutable state with this view.</returns>
  public MinCutFinder CreateMinCutFinder() => new(_nodes.Length, _edgeFrom, _edgeTo);

  /// <summary>
  /// Collects the nodes within a number of hops of a center node. Every passage counts,
  /// whatever its direction, conditions or secrecy.
  /// </summary>
  /// <param name="center">Work index of the center node.</param>
  /// <param name="radius">Largest hop distance included; 0 yields the center alone. Must not be negative.</param>
  /// <param name="blocked">
  /// Node the ball never enters (nor reaches through), or <see cref="NoNode"/> for none. Must
  /// differ from <paramref name="center"/>.
  /// </param>
  /// <returns>Flags per node; the center is always set, the blocked node never.</returns>
  /// <exception cref="ArgumentOutOfRangeException"><paramref name="radius"/> is negative.</exception>
  /// <exception cref="ArgumentException"><paramref name="blocked"/> is the center.</exception>
  public bool[] Ball(int center, int radius, int blocked = NoNode)
  {
    if (radius < 0)
      throw new ArgumentOutOfRangeException(nameof(radius), radius, "Radius must not be negative.");
    if (blocked == center)
      throw new ArgumentException("The center cannot be blocked.", nameof(blocked));

    var inside = new bool[_nodes.Length];
    var distance = new int[_nodes.Length];
    var queue = new Queue<int>();
    inside[center] = true;
    queue.Enqueue(center);

    while (queue.Count > 0)
    {
      var node = queue.Dequeue();
      if (distance[node] == radius) continue;

      foreach (var edge in _incident[node])
      {
        var next = OtherEnd(edge, node);
        if (inside[next] || next == blocked) continue;

        inside[next] = true;
        distance[next] = distance[node] + 1;
        queue.Enqueue(next);
      }
    }

    return inside;
  }

  #endregion

  #region Cut costs

  /// <summary>
  /// Checks whether a passage may receive one more condition: it is two-way and below
  /// <see cref="CnfProcObstaclePlacerOpt.MaxConditionsPerEdge"/>. Reads the conditions live.
  /// </summary>
  /// <param name="edge">Work index of the passage.</param>
  /// <returns>True when an obstacle may lock the passage.</returns>
  public bool IsLockable(int edge)
  {
    return _edges[edge].Direction == EnPassageDirection.Bidirectional && HasConditionRoom(edge);
  }

  /// <summary>
  /// Tells whether a passage is below the condition cap
  /// (<see cref="CnfProcObstaclePlacerOpt.MaxConditionsPerEdge"/>), whatever its direction.
  /// </summary>
  /// <param name="edge">Work index of the passage.</param>
  /// <returns>True when one more condition fits.</returns>
  public bool HasConditionRoom(int edge) => _edges[edge].Conditions.Count < _options.MaxConditionsPerEdge;

  /// <summary>
  /// Cost of cutting a passage in the main pass. Reads the conditions live.
  /// </summary>
  /// <remarks>
  /// Not lockable (one-way or at the condition cap): <see cref="CostUncuttable"/>. Secret:
  /// <see cref="CostSecret"/>. Otherwise <see cref="CostChokepoint"/> or <see cref="CostOpen"/>,
  /// plus <see cref="CostStack"/> per condition already present.
  /// </remarks>
  /// <param name="edge">Work index of the passage.</param>
  /// <returns>Cut cost; never negative.</returns>
  public long MainCutCost(int edge)
  {
    if (!IsLockable(edge)) return CostUncuttable;

    var passage = _edges[edge];
    if (passage.Secret) return CostSecret;

    var baseCost = passage.Chokepoint ? CostChokepoint : CostOpen;
    return baseCost + CostStack * passage.Conditions.Count;
  }

  /// <summary>
  /// Builds the main-pass cut cost of every passage, see <see cref="MainCutCost"/>.
  /// </summary>
  /// <returns>Cost per edge work index; a snapshot of the current conditions.</returns>
  public long[] BuildMainCutCosts()
  {
    var costs = new long[_edges.Length];
    for (var edge = 0; edge < _edges.Length; edge++) costs[edge] = MainCutCost(edge);
    return costs;
  }

  /// <summary>
  /// Counts how loaded the passages are with conditions; the measure behind
  /// <see cref="CnfProcObstaclePlacerOpt.MaxStackLoad"/>. Every passage counts, secret and
  /// one-way ones included, and so do conditions that existed before the placer ran.
  /// </summary>
  /// <param name="locked">Number of passages carrying at least one condition.</param>
  /// <param name="extra">Number of stacked conditions: conditions beyond the first, summed over all passages.</param>
  public void CountConditionLoad(out int locked, out int extra)
  {
    locked = 0;
    extra = 0;
    foreach (var passage in _edges)
    {
      var count = passage.Conditions.Count;
      if (count == 0) continue;

      locked++;
      extra += count - 1;
    }
  }

  #endregion

  #region Sides of a cut

  /// <summary>
  /// Turns a set of cut passages into a full candidate: decides which nodes end up behind the
  /// cut and which passages must be locked.
  /// </summary>
  /// <remarks>
  /// <para>Rules, with "solid" meaning a passage with a positive cost:</para>
  /// <list type="number">
  /// <item>The far core is everything reachable from the target over solid passages that are
  /// not cut.</item>
  /// <item>The start side is everything reachable from the source over any passage without
  /// entering the far core. So a node hanging on a zero-cost (secret) passage joins the start
  /// side whenever it touches it.</item>
  /// <item>All remaining nodes are far: the far core plus what is reachable only through it
  /// (for example a secret leaf of a far node).</item>
  /// <item>Every passage between the two sides is locked. Solid ones are always cut passages;
  /// zero-cost ones are the secret routes that would otherwise bypass the obstacle. Cut
  /// passages that turn out not to separate the sides are dropped.</item>
  /// </list>
  /// </remarks>
  /// <param name="source">Work index of the node the player comes from (the start).</param>
  /// <param name="target">Work index of the node to keep away from the source.</param>
  /// <param name="cutEdges">Solid passages forming a cut between source and target.</param>
  /// <param name="costs">Cut cost per edge, as used to find <paramref name="cutEdges"/>.</param>
  /// <returns>
  /// The candidate, or <see langword="null"/> when the cut does not separate the two nodes or
  /// would need a passage that cannot be locked. Deterministic.
  /// </returns>
  /// <exception cref="ArgumentNullException"><paramref name="cutEdges"/> or <paramref name="costs"/> is null.</exception>
  /// <exception cref="ArgumentException"><paramref name="costs"/> does not hold one cost per edge.</exception>
  public CutCandidate? BuildCandidate(int source, int target, IReadOnlyList<int> cutEdges, long[] costs)
  {
    if (cutEdges is null) throw new ArgumentNullException(nameof(cutEdges));
    if (costs is null) throw new ArgumentNullException(nameof(costs));
    if (costs.Length != _edges.Length)
      throw new ArgumentException("One cost per edge is required.", nameof(costs));
    if (source == target) return null;

    var isCut = new bool[_edges.Length];
    foreach (var edge in cutEdges) isCut[edge] = true;

    // Rule 1: far core.
    var farCore = Flood(target, edge => costs[edge] > 0 && !isCut[edge], blocked: null);
    if (farCore[source]) return null; // Not a cut at all.

    // Rules 2 and 3: start side; everything else is far.
    var near = Flood(source, _ => true, blocked: farCore);

    // Rule 4: lock every passage between the sides.
    var lockEdges = new List<int>();
    var ordinaryWidth = 0;
    long cost = 0;
    var maxConditions = 0;
    for (var edge = 0; edge < _edges.Length; edge++)
    {
      if (near[_edgeFrom[edge]] == near[_edgeTo[edge]]) continue;
      if (!IsLockable(edge)) return null;

      lockEdges.Add(edge);
      cost += costs[edge];
      var passage = _edges[edge];
      if (!passage.Secret) ordinaryWidth++;
      maxConditions = Math.Max(maxConditions, passage.Conditions.Count);
    }

    if (lockEdges.Count == 0) return null; // Target is not connected to the source.

    var farNodes = new List<int>();
    for (var node = 0; node < _nodes.Length; node++)
    {
      if (!near[node]) farNodes.Add(node);
    }

    return new CutCandidate(target, lockEdges, farNodes, ordinaryWidth, cost, maxConditions);
  }

  /// <summary>
  /// Breadth-first flood over the passages a predicate allows, treating all of them as two-way.
  /// </summary>
  /// <param name="start">Work index of the node to start from.</param>
  /// <param name="usable">Decides per edge work index whether the passage may be walked.</param>
  /// <param name="blocked">Nodes that must never be entered; null blocks nothing.</param>
  /// <returns>Flags of the reached nodes, the start included.</returns>
  private bool[] Flood(int start, Func<int, bool> usable, bool[]? blocked)
  {
    var reached = new bool[_nodes.Length];
    var queue = new Queue<int>();
    reached[start] = true;
    queue.Enqueue(start);

    while (queue.Count > 0)
    {
      var node = queue.Dequeue();
      foreach (var edge in _incident[node])
      {
        var next = OtherEnd(edge, node);
        if (reached[next]) continue;
        if (blocked != null && blocked[next]) continue;
        if (!usable(edge)) continue;

        reached[next] = true;
        queue.Enqueue(next);
      }
    }

    return reached;
  }

  #endregion

  #region Internals

  /// <summary>
  /// Finds the node holding a POI of the given type when exactly one node does.
  /// </summary>
  /// <param name="type">POI type to look for.</param>
  /// <returns>Work index of that node, or <see cref="NoNode"/> for none or several.</returns>
  private int FindSingleNodeWithPoi(EnPoiType type)
  {
    var found = NoNode;
    for (var node = 0; node < _nodes.Length; node++)
    {
      if (!_nodes[node].HasPoi(type)) continue;
      if (found != NoNode) return NoNode; // Several nodes: unsupported.
      found = node;
    }

    return found;
  }

  /// <summary>
  /// Computes <see cref="MainTargets"/>; requires <see cref="StartNode"/> and
  /// <see cref="ExitNode"/> to be set.
  /// </summary>
  /// <returns>Target work indices in ascending order; empty when the main pass has nothing to do.</returns>
  private List<int> FindMainTargets()
  {
    if (StartNode == NoNode || ExitNode == NoNode) return [];
    if (StartNode != ExitNode) return [ExitNode];

    var goals = FindNodesWithPoiExceptStart(EnPoiType.Goal);
    return goals.Count > 0 ? goals : FindNodesWithPoiExceptStart(EnPoiType.OptionalGoal);
  }

  /// <summary>
  /// Lists every node holding a POI of the given type, except the start node.
  /// </summary>
  /// <param name="type">POI type to look for.</param>
  /// <returns>Work indices in ascending order; empty when none qualify.</returns>
  private List<int> FindNodesWithPoiExceptStart(EnPoiType type)
  {
    var found = new List<int>();
    for (var node = 0; node < _nodes.Length; node++)
    {
      if (node != StartNode && _nodes[node].HasPoi(type)) found.Add(node);
    }

    return found;
  }

  #endregion
}
