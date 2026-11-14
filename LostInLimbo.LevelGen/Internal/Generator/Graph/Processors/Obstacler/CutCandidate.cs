using System.Collections.Generic;

namespace LostInLimbo.LevelGen.Internal.Generator.Graph.Processors.Obstacler;

/// <summary>
/// One way to separate a target node from the start: the passages an obstacle would lock and
/// the nodes that end up behind it.
/// </summary>
/// <remarks>
/// All indices are work indices of <see cref="ObstacleWorkGraph"/> (positions in identifier
/// order), not positions in the graph lists. Immutable; the lists are sorted ascending, so two
/// candidates locking the same passages have equal <see cref="LockEdges"/> sequences.
/// </remarks>
internal sealed class CutCandidate
{
  /// <summary>Creates a candidate.</summary>
  /// <param name="target">Node the cut keeps away from the start.</param>
  /// <param name="lockEdges">Passages to lock, ascending.</param>
  /// <param name="farNodes">Nodes behind the cut, ascending.</param>
  /// <param name="ordinaryWidth">Number of non-secret passages among <paramref name="lockEdges"/>.</param>
  /// <param name="cost">Summed cut cost of <paramref name="lockEdges"/>.</param>
  /// <param name="maxConditions">Highest condition count already present on a passage to lock.</param>
  public CutCandidate(
    int target,
    IReadOnlyList<int> lockEdges,
    IReadOnlyList<int> farNodes,
    int ordinaryWidth,
    long cost,
    int maxConditions)
  {
    Target = target;
    LockEdges = lockEdges;
    FarNodes = farNodes;
    OrdinaryWidth = ordinaryWidth;
    Cost = cost;
    MaxConditions = maxConditions;
  }

  /// <summary>Node the cut keeps away from the start; always one of <see cref="FarNodes"/>.</summary>
  public int Target { get; }

  /// <summary>
  /// Every passage between the start side and the far side, secret ones included, in
  /// ascending order. Never empty. Locking all of them makes the far side unreachable.
  /// </summary>
  public IReadOnlyList<int> LockEdges { get; }

  /// <summary>Nodes on the far side of the cut, in ascending order. Never contains the start.</summary>
  public IReadOnlyList<int> FarNodes { get; }

  /// <summary>Number of non-secret passages among <see cref="LockEdges"/>; may be 0.</summary>
  public int OrdinaryWidth { get; }

  /// <summary>Summed cut cost of <see cref="LockEdges"/>; lower is a better bottleneck.</summary>
  public long Cost { get; }

  /// <summary>
  /// Highest number of conditions already present on any passage of <see cref="LockEdges"/>;
  /// 0 means a fresh cut that stacks onto nothing.
  /// </summary>
  public int MaxConditions { get; }
}
