using System;

namespace LostInLimbo.LevelGen.Data.Result.Graph;

/// <summary>
/// Single requirement that must be met before a passage (<see cref="LilEdge"/>) can be used.
/// </summary>
/// <remarks>
/// <para>
/// Conditions reference a specific POI, not just its node: one node can contain multiple
/// solutions, each opening access to a different place (e.g. red key and blue key in the
/// same room, each unlocking a different door). Pointing at the POI removes that ambiguity.
/// </para>
/// <para>
/// Because a POI always lives inside exactly one node, "reach the node containing the POI"
/// is implied by "collect the POI"; the containing node can be found by lookup when needed.
/// </para>
/// <para>
/// Example: door requiring both red and blue keys produces two <see cref="LilCondition"/>
/// entries on the same edge, each pointing to the respective key POI.
/// </para>
/// Invariants (documented, not yet enforced): for <see cref="EnConditionType.RequiresSolution"/>,
/// <see cref="RequiresPoi"/> must reference an existing POI whose type is
/// <see cref="EnPoiType.Solution"/>.
/// </remarks>
public sealed record LilCondition
{
  /// <summary>Kind of this condition.</summary>
  public required EnConditionType Type { get; init; }

  /// <summary>
  /// Identifier of the POI that must be collected before this passage can be used.
  /// Must not be <see cref="Guid.Empty"/> and should reference an existing POI.
  /// </summary>
  public required Guid RequiresPoi { get; init; }
}
