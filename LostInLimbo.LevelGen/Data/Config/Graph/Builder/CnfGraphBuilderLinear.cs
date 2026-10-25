using System;
using LostInLimbo.LevelGen.Data.Result.Graph;

namespace LostInLimbo.LevelGen.Data.Config.Graph.Builder;

/// <summary>
/// <para>Configuration of <see cref="Internal.Generator.Graph.Builders.GraphBuilderLinear"/>: degree caps,
/// condition limits and skip distance.</para>
/// </summary>
public sealed record CnfGraphBuilderLinear
{
  // Defaults.

  /// <summary>Default instance.</summary>
  public static CnfGraphBuilderLinear Default { get; } = new();

  // Fields.

  /// <summary>
  /// Maximum number of relationships a node may have, counting skeleton and extra
  /// relationships together. Assigned to every node as its <see cref="LilNode.MaxEdges"/>
  /// hard cap.
  /// </summary>
  public int MaxEdges { get; init; } = 5;

  /// <summary>
  /// Maximum number of extra relationships a node may gain in the extra-relationships phase (inclusive).
  /// Important: we count in and out relationships separately.
  /// </summary>
  public int MaxExtraDegree { get; init; } = 2;

  /// <summary>Maximum number of conditions that a single relationship can have.</summary>
  public int MaxConditionPerRel { get; init; } = 3;

  /// <summary>
  /// Maximum distance between the solution and the guarded node's chain predecessor (so at most
  /// <c>MaxConditionDistance + 1</c> from the guarded node).
  /// </summary>
  public int MaxConditionDistance { get; init; } = 3;

  /// <summary>Maximum number of nodes that can be skipped by an extra relationship.</summary>
  public int MaxSkipDistance { get; init; } = 2;

  // Helpers.

  /// <summary>
  /// Checks that this config can be used for generation.
  /// </summary>
  /// <exception cref="ArgumentOutOfRangeException">A value is out of range.</exception>
  public void Validate()
  {
    if (MaxEdges < 1)
      throw new ArgumentOutOfRangeException(nameof(MaxEdges), MaxEdges, "MaxEdges must be at least 1.");
    if (MaxExtraDegree < 0)
      throw new ArgumentOutOfRangeException(nameof(MaxExtraDegree), MaxExtraDegree, "MaxExtraDegree must be at least 0.");
    if (MaxConditionPerRel < 1)
      throw new ArgumentOutOfRangeException(nameof(MaxConditionPerRel), MaxConditionPerRel, "MaxConditionPerRel must be at least 1.");
    if (MaxConditionDistance < 0)
      throw new ArgumentOutOfRangeException(nameof(MaxConditionDistance), MaxConditionDistance, "MaxConditionDistance must be at least 0.");
    if (MaxSkipDistance < 0)
      throw new ArgumentOutOfRangeException(nameof(MaxSkipDistance), MaxSkipDistance, "MaxSkipDistance must be at least 0.");
  }
}
