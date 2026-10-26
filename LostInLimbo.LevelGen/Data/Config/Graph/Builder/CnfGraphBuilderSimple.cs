using System;

namespace LostInLimbo.LevelGen.Data.Config.Graph.Builder;

/// <summary>
/// <para>Configuration of <see cref="Internal.Generator.Graph.Builders.GraphBuilderSimple"/>: passage chokepoint chance and degree caps.</para>
/// </summary>
public sealed record CnfGraphBuilderSimple
{
  // Defaults.

  /// <summary>Default instance.</summary>
  public static CnfGraphBuilderSimple Default { get; } = new();

  // Fields.

  /// <summary>
  /// Chance that a given relationship is a chokepoint (only one physical passage allowed for map generation).
  /// </summary>
  public float ChokepointChance { get; init; } = 0.5f;

  /// <summary>
  /// Total-degree eligibility threshold for the extra-relationships phase: nodes whose degree has reached this cap are skipped,
  /// so extras never push total degree above it (inclusive).
  /// </summary>
  public int MaxExtraDegree { get; init; } = 4;

  /// <summary>
  /// Maximum number of relationships a node may gain in the skeleton phase (inclusive). A central node that reaches this cap
  /// hands its remaining nodes over to a new central.
  /// </summary>
  public int MaxExtraDegreeStar { get; init; } = 7;

  // Helpers.

  /// <summary>
  /// Checks that this config can be used for generation.
  /// </summary>
  /// <exception cref="ArgumentOutOfRangeException">A value is out of range.</exception>
  public void Validate()
  {
    if (float.IsNaN(ChokepointChance) || ChokepointChance < 0f || ChokepointChance > 1f)
      throw new ArgumentOutOfRangeException(nameof(ChokepointChance), ChokepointChance, "ChokepointChance must be a number between 0 and 1.");
    if (MaxExtraDegree < 0)
      throw new ArgumentOutOfRangeException(nameof(MaxExtraDegree), MaxExtraDegree, "MaxExtraDegree must be at least 0.");
    if (MaxExtraDegreeStar < 2)
      throw new ArgumentOutOfRangeException(nameof(MaxExtraDegreeStar), MaxExtraDegreeStar, "MaxExtraDegreeStar must be at least 2.");
  }
}
