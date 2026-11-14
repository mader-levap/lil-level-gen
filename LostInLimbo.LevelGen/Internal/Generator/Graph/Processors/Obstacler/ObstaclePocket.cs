using System;
using System.Collections.Generic;

namespace LostInLimbo.LevelGen.Internal.Generator.Graph.Processors.Obstacler;

/// <summary>
/// A dead-end part of the graph: a group of nodes whose every passage to the rest of the graph
/// joins one node pair - the gate node inside and the host node outside.
/// </summary>
/// <remarks>
/// All numbers are work indices of <see cref="ObstacleWorkGraph"/>. The start node is never
/// inside a pocket. Passages count regardless of kind: secret and one-way ones included.
/// </remarks>
internal sealed class ObstaclePocket
{
  /// <summary>Membership flag per node work index.</summary>
  private readonly bool[] _inside;

  /// <summary>
  /// Creates a pocket.
  /// </summary>
  /// <param name="gateNode">Node of the pocket that the passages of the pair lead to.</param>
  /// <param name="hostNode">Node outside the pocket that the passages of the pair start at.</param>
  /// <param name="inside">Membership flag per node work index; taken over, not copied.</param>
  /// <param name="pairEdges">Every passage between gate and host, ascending.</param>
  /// <param name="entryEdges">Passages of the pair that can be walked into the pocket, ascending.</param>
  /// <exception cref="ArgumentNullException">A list argument is null.</exception>
  public ObstaclePocket(
    int gateNode,
    int hostNode,
    bool[] inside,
    IReadOnlyList<int> pairEdges,
    IReadOnlyList<int> entryEdges)
  {
    _inside = inside ?? throw new ArgumentNullException(nameof(inside));
    PairEdges = pairEdges ?? throw new ArgumentNullException(nameof(pairEdges));
    EntryEdges = entryEdges ?? throw new ArgumentNullException(nameof(entryEdges));
    GateNode = gateNode;
    HostNode = hostNode;

    var nodes = new List<int>();
    for (var node = 0; node < inside.Length; node++)
    {
      if (inside[node]) nodes.Add(node);
    }

    Nodes = nodes;
  }

  /// <summary>Node of the pocket that every passage of the pair leads to.</summary>
  public int GateNode { get; }

  /// <summary>Node outside the pocket that every passage of the pair starts at.</summary>
  public int HostNode { get; }

  /// <summary>Nodes of the pocket, ascending; never empty, always contains <see cref="GateNode"/>.</summary>
  public IReadOnlyList<int> Nodes { get; }

  /// <summary>Every passage between <see cref="GateNode"/> and <see cref="HostNode"/>, ascending.</summary>
  public IReadOnlyList<int> PairEdges { get; }

  /// <summary>
  /// Passages of the pair that can be walked into the pocket, ascending: two-way ones and
  /// one-way ones pointing at the gate. These are what an obstacle locks.
  /// </summary>
  public IReadOnlyList<int> EntryEdges { get; }

  /// <summary>Tells whether a node belongs to the pocket.</summary>
  /// <param name="node">Work index of the node.</param>
  /// <returns>True when the node is inside.</returns>
  public bool Contains(int node) => _inside[node];
}
