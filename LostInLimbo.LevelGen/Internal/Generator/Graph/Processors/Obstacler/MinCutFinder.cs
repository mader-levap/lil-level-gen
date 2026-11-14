using System;
using System.Collections.Generic;

namespace LostInLimbo.LevelGen.Internal.Generator.Graph.Processors.Obstacler;

/// <summary>
/// Result of one <see cref="MinCutFinder.Find"/> call.
/// </summary>
/// <remarks>
/// Both side arrays are indexed by node. When <see cref="Limited"/> is true the search was
/// abandoned, so the side arrays describe no valid cut and must not be used.
/// </remarks>
internal sealed class MinCutResult
{
  /// <summary>Creates a result.</summary>
  /// <param name="flow">Total flow pushed, i.e. the weight of the minimum cut unless limited.</param>
  /// <param name="limited">True when the flow reached the limit and the search stopped.</param>
  /// <param name="sourceSide">Nodes on the source side of the minimum cut nearest the sources.</param>
  /// <param name="sinkSide">Nodes on the sink side of the minimum cut nearest the sinks.</param>
  public MinCutResult(long flow, bool limited, bool[] sourceSide, bool[] sinkSide)
  {
    Flow = flow;
    Limited = limited;
    SourceSide = sourceSide;
    SinkSide = sinkSide;
  }

  /// <summary>Total flow pushed: the weight of the minimum cut, unless <see cref="Limited"/>.</summary>
  public long Flow { get; }

  /// <summary>True when the flow reached the caller's limit: no cut below the limit exists.</summary>
  public bool Limited { get; }

  /// <summary>
  /// Nodes still reachable from the sources once the flow is maximal. The edges leaving this
  /// set form the minimum cut nearest the sources.
  /// </summary>
  public bool[] SourceSide { get; }

  /// <summary>
  /// Nodes that can still reach a sink once the flow is maximal. The edges entering this set
  /// form the minimum cut nearest the sinks.
  /// </summary>
  public bool[] SinkSide { get; }
}

/// <summary>
/// Minimum cut between two node sets of an undirected multigraph, by maximum flow
/// (Edmonds-Karp: shortest augmenting paths).
/// </summary>
/// <remarks>
/// <para>
/// Nodes are indices <c>0 .. nodeCount - 1</c>. Every edge is its own unit, identified by its
/// index: parallel edges between the same two nodes are never merged, and each carries its own
/// capacity. Edges are undirected - flow may use them in either direction. Self-loops are ignored.
/// </para>
/// <para>
/// Several sources or sinks act as one contracted terminal. An edge with capacity 0 does not
/// exist for the flow. Deterministic: nodes and edges are visited in index order.
/// </para>
/// </remarks>
internal sealed class MinCutFinder
{
  /// <summary>Marks a node without a predecessor edge in the breadth-first search.</summary>
  private const int NoEdge = -1;

  /// <summary>Number of nodes.</summary>
  private readonly int _nodeCount;
  /// <summary>First endpoint per edge.</summary>
  private readonly int[] _edgeFrom;
  /// <summary>Second endpoint per edge.</summary>
  private readonly int[] _edgeTo;
  /// <summary>Incident edge indices per node, ascending; self-loops left out.</summary>
  private readonly List<int>[] _incident;

  /// <summary>
  /// Indexes the graph.
  /// </summary>
  /// <param name="nodeCount">Number of nodes; must not be negative.</param>
  /// <param name="edgeFrom">First endpoint per edge.</param>
  /// <param name="edgeTo">Second endpoint per edge; same length as <paramref name="edgeFrom"/>.</param>
  /// <exception cref="ArgumentNullException">An array is null.</exception>
  /// <exception cref="ArgumentOutOfRangeException"><paramref name="nodeCount"/> is negative or an endpoint is not a node.</exception>
  /// <exception cref="ArgumentException">The endpoint arrays differ in length.</exception>
  public MinCutFinder(int nodeCount, int[] edgeFrom, int[] edgeTo)
  {
    if (edgeFrom is null) throw new ArgumentNullException(nameof(edgeFrom));
    if (edgeTo is null) throw new ArgumentNullException(nameof(edgeTo));
    if (nodeCount < 0)
      throw new ArgumentOutOfRangeException(nameof(nodeCount), nodeCount, "Node count must not be negative.");
    if (edgeFrom.Length != edgeTo.Length)
      throw new ArgumentException("Endpoint arrays must have the same length.", nameof(edgeTo));

    _nodeCount = nodeCount;
    _edgeFrom = edgeFrom;
    _edgeTo = edgeTo;
    _incident = new List<int>[nodeCount];
    for (var node = 0; node < nodeCount; node++) _incident[node] = [];

    for (var edge = 0; edge < edgeFrom.Length; edge++)
    {
      var from = edgeFrom[edge];
      var to = edgeTo[edge];
      if (from < 0 || from >= nodeCount)
        throw new ArgumentOutOfRangeException(nameof(edgeFrom), from, $"Edge {edge} starts at an unknown node.");
      if (to < 0 || to >= nodeCount)
        throw new ArgumentOutOfRangeException(nameof(edgeTo), to, $"Edge {edge} ends at an unknown node.");
      if (from == to) continue; // A self-loop never carries flow between two sides.

      _incident[from].Add(edge);
      _incident[to].Add(edge);
    }
  }

  /// <summary>
  /// Finds the minimum cut separating every source from every sink.
  /// </summary>
  /// <param name="capacities">Capacity per edge; must not be negative. 0 removes the edge.</param>
  /// <param name="sources">Flags the source nodes; at least one.</param>
  /// <param name="sinks">Flags the sink nodes; at least one, none of them a source.</param>
  /// <param name="flowLimit">
  /// The search stops as soon as the flow reaches this value and reports
  /// <see cref="MinCutResult.Limited"/>. Use it to give up on cuts through "uncuttable" edges
  /// with a huge capacity. Must be positive.
  /// </param>
  /// <returns>Cut weight and both extreme minimum cuts. Deterministic.</returns>
  /// <exception cref="ArgumentNullException">An array is null.</exception>
  /// <exception cref="ArgumentException">
  /// An array has the wrong length, a capacity is negative, a terminal set is empty or a node
  /// is both source and sink.
  /// </exception>
  /// <exception cref="ArgumentOutOfRangeException"><paramref name="flowLimit"/> is not positive.</exception>
  public MinCutResult Find(long[] capacities, bool[] sources, bool[] sinks, long flowLimit)
  {
    ValidateArguments(capacities, sources, sinks, flowLimit);

    // Signed flow per edge: positive runs From -> To, negative To -> From.
    var flow = new long[capacities.Length];
    var parentEdge = new int[_nodeCount];
    var visited = new bool[_nodeCount];
    long total = 0;

    while (true)
    {
      var sink = FindAugmentingPath(capacities, flow, sources, sinks, visited, parentEdge);
      if (sink < 0) break; // Flow is maximal; 'visited' is the source side.

      total += Augment(capacities, flow, sources, parentEdge, sink);
      if (total >= flowLimit)
        return new MinCutResult(total, limited: true, new bool[_nodeCount], new bool[_nodeCount]);
    }

    var sinkSide = FloodTowardsSinks(capacities, flow, sinks);
    return new MinCutResult(total, limited: false, visited, sinkSide);
  }

  /// <summary>
  /// Lists the edges with a positive capacity that have exactly one endpoint inside a node set.
  /// </summary>
  /// <param name="capacities">Capacity per edge.</param>
  /// <param name="side">Flags the nodes of the set.</param>
  /// <returns>Edge indices in ascending order.</returns>
  public List<int> CrossingEdges(long[] capacities, bool[] side)
  {
    var crossing = new List<int>();
    for (var edge = 0; edge < _edgeFrom.Length; edge++)
    {
      if (capacities[edge] <= 0) continue;
      if (side[_edgeFrom[edge]] != side[_edgeTo[edge]]) crossing.Add(edge);
    }

    return crossing;
  }

  #region Internals

  /// <summary>Checks the arguments of <see cref="Find"/>.</summary>
  /// <param name="capacities">Capacity per edge.</param>
  /// <param name="sources">Source flags per node.</param>
  /// <param name="sinks">Sink flags per node.</param>
  /// <param name="flowLimit">Flow limit.</param>
  private void ValidateArguments(long[] capacities, bool[] sources, bool[] sinks, long flowLimit)
  {
    if (capacities is null) throw new ArgumentNullException(nameof(capacities));
    if (sources is null) throw new ArgumentNullException(nameof(sources));
    if (sinks is null) throw new ArgumentNullException(nameof(sinks));
    if (capacities.Length != _edgeFrom.Length)
      throw new ArgumentException("One capacity per edge is required.", nameof(capacities));
    if (sources.Length != _nodeCount)
      throw new ArgumentException("One source flag per node is required.", nameof(sources));
    if (sinks.Length != _nodeCount)
      throw new ArgumentException("One sink flag per node is required.", nameof(sinks));
    if (flowLimit <= 0)
      throw new ArgumentOutOfRangeException(nameof(flowLimit), flowLimit, "Flow limit must be positive.");

    foreach (var capacity in capacities)
    {
      if (capacity < 0) throw new ArgumentException("Capacities must not be negative.", nameof(capacities));
    }

    var hasSource = false;
    var hasSink = false;
    for (var node = 0; node < _nodeCount; node++)
    {
      if (sources[node] && sinks[node])
        throw new ArgumentException($"Node {node} is both source and sink.", nameof(sinks));
      hasSource |= sources[node];
      hasSink |= sinks[node];
    }

    if (!hasSource) throw new ArgumentException("At least one source is required.", nameof(sources));
    if (!hasSink) throw new ArgumentException("At least one sink is required.", nameof(sinks));
  }

  /// <summary>
  /// Remaining capacity of an edge when leaving it from the given endpoint.
  /// </summary>
  /// <param name="capacities">Capacity per edge.</param>
  /// <param name="flow">Signed flow per edge.</param>
  /// <param name="edge">Edge index.</param>
  /// <param name="fromNode">Endpoint the edge is left from.</param>
  /// <returns>Flow that may still be pushed in that direction.</returns>
  private long Residual(long[] capacities, long[] flow, int edge, int fromNode)
  {
    return fromNode == _edgeFrom[edge] ? capacities[edge] - flow[edge] : capacities[edge] + flow[edge];
  }

  /// <summary>
  /// Breadth-first search from all sources over edges with remaining capacity.
  /// </summary>
  /// <param name="capacities">Capacity per edge.</param>
  /// <param name="flow">Signed flow per edge.</param>
  /// <param name="sources">Source flags per node.</param>
  /// <param name="sinks">Sink flags per node.</param>
  /// <param name="visited">Filled with the nodes reached; complete only when no sink is found.</param>
  /// <param name="parentEdge">Filled with the edge each reached node was entered through.</param>
  /// <returns>The first sink reached, or -1 when no sink is reachable.</returns>
  private int FindAugmentingPath(
    long[] capacities,
    long[] flow,
    bool[] sources,
    bool[] sinks,
    bool[] visited,
    int[] parentEdge)
  {
    var queue = new Queue<int>();
    for (var node = 0; node < _nodeCount; node++)
    {
      visited[node] = sources[node];
      parentEdge[node] = NoEdge;
      if (sources[node]) queue.Enqueue(node);
    }

    while (queue.Count > 0)
    {
      var node = queue.Dequeue();
      foreach (var edge in _incident[node])
      {
        var next = OtherEnd(edge, node);
        if (visited[next] || Residual(capacities, flow, edge, node) <= 0) continue;

        visited[next] = true;
        parentEdge[next] = edge;
        if (sinks[next]) return next;
        queue.Enqueue(next);
      }
    }

    return -1;
  }

  /// <summary>
  /// Pushes as much flow as the found path allows.
  /// </summary>
  /// <param name="capacities">Capacity per edge.</param>
  /// <param name="flow">Signed flow per edge, updated in place.</param>
  /// <param name="sources">Source flags per node; the walk back ends at a source.</param>
  /// <param name="parentEdge">Edge each node of the path was entered through.</param>
  /// <param name="sink">Sink the path ends at.</param>
  /// <returns>Amount of flow pushed; positive.</returns>
  private long Augment(long[] capacities, long[] flow, bool[] sources, int[] parentEdge, int sink)
  {
    // First walk: bottleneck of the path.
    var bottleneck = long.MaxValue;
    for (var node = sink; !sources[node];)
    {
      var edge = parentEdge[node];
      var previous = OtherEnd(edge, node);
      bottleneck = Math.Min(bottleneck, Residual(capacities, flow, edge, previous));
      node = previous;
    }

    // Second walk: push it.
    for (var node = sink; !sources[node];)
    {
      var edge = parentEdge[node];
      var previous = OtherEnd(edge, node);
      flow[edge] += previous == _edgeFrom[edge] ? bottleneck : -bottleneck;
      node = previous;
    }

    return bottleneck;
  }

  /// <summary>
  /// Collects every node that can still reach a sink over edges with remaining capacity.
  /// </summary>
  /// <param name="capacities">Capacity per edge.</param>
  /// <param name="flow">Signed, maximal flow per edge.</param>
  /// <param name="sinks">Sink flags per node.</param>
  /// <returns>Flags of the nodes on the sink side, sinks included.</returns>
  private bool[] FloodTowardsSinks(long[] capacities, long[] flow, bool[] sinks)
  {
    var reached = new bool[_nodeCount];
    var queue = new Queue<int>();
    for (var node = 0; node < _nodeCount; node++)
    {
      if (!sinks[node]) continue;
      reached[node] = true;
      queue.Enqueue(node);
    }

    while (queue.Count > 0)
    {
      var node = queue.Dequeue();
      foreach (var edge in _incident[node])
      {
        // Walking backwards: the neighbour must be able to push flow towards this node.
        var previous = OtherEnd(edge, node);
        if (reached[previous] || Residual(capacities, flow, edge, previous) <= 0) continue;

        reached[previous] = true;
        queue.Enqueue(previous);
      }
    }

    return reached;
  }

  /// <summary>Gets the endpoint of an edge opposite to the given one.</summary>
  /// <param name="edge">Edge index.</param>
  /// <param name="node">One endpoint of the edge.</param>
  /// <returns>The other endpoint.</returns>
  private int OtherEnd(int edge, int node)
  {
    return _edgeFrom[edge] == node ? _edgeTo[edge] : _edgeFrom[edge];
  }

  #endregion
}
