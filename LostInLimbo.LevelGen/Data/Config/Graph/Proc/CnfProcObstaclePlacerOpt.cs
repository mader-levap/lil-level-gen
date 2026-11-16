using System;
using LostInLimbo.LevelGen.Data.Result.Graph;

namespace LostInLimbo.LevelGen.Data.Config.Graph.Proc;

/// <summary>
/// <para>
/// Configuration of the obstacle-placer processor (<c>ProcObstaclePlacer</c>): how obstacles
/// (conditions on passages) and their solutions (<see cref="EnPoiType.Solution"/> POIs) are
/// spread over a graph.
/// </para>
/// </summary>
/// <remarks>
/// <para>
/// The number of obstacles is not configured here: it comes from <see cref="CnfGraph.Complexity"/>,
/// one obstacle (one solution) per point. Complexity 0 disables the processor. Only builders
/// that opt in run this processor; others ignore the section.
/// </para>
/// <para>
/// The processor has a main pass (obstacles between the start and the exit) and a side pass
/// (obstacles guarding dead ends with secrets, goals or optional goals). All chances are
/// probabilities within [0, 1]; 0 never happens, 1 always happens.
/// </para>
/// </remarks>
public sealed record CnfProcObstaclePlacerOpt
{
  /// <summary>Default instance.</summary>
  public static CnfProcObstaclePlacerOpt Default { get; } = new();

  #region Limits

  /// <summary>
  /// Maximum number of conditions a single passage may carry (inclusive). A passage at this
  /// cap never receives another obstacle.
  /// <para>Must be at least 1.</para>
  /// </summary>
  public int MaxConditionsPerEdge { get; init; } = 3;

  /// <summary>
  /// Maximum number of ordinary (non-secret) passages that one obstacle may lock (inclusive).
  /// Wider cuts are rejected, so the same solution never guards more passages than this.
  /// <para>Must be at least 1.</para>
  /// </summary>
  public int MaxEdgesPerObstacle { get; init; } = 4;

  /// <summary>
  /// Width cap of the one-time fallback of the main pass (inclusive): when the main pass has
  /// placed nothing yet and no cut fits <see cref="MaxEdgesPerObstacle"/> (for example start and
  /// exit as adjacent hubs whose leaves are linked), one obstacle may lock up to this many
  /// ordinary passages, so the exit is never left open for want of a narrow cut.
  /// <para>Must be at least 1; a value at or below <see cref="MaxEdgesPerObstacle"/> disables the fallback.</para>
  /// </summary>
  public int MaxEdgesPerObstacleFallback { get; init; } = 8;

  // Budget split between the passes.

  /// <summary>
  /// Share of Complexity given to the main pass when Complexity is above
  /// <see cref="SmallComplexityMax"/>; the rest goes to the side pass.
  /// <para>Must be a finite value within [0, 1].</para>
  /// </summary>
  public float MainShare { get; init; } = 0.7f;

  /// <summary>
  /// Largest Complexity (inclusive) that counts as small. A small Complexity goes to the main
  /// pass as a whole, except for the point reserved by <see cref="ChanceReserveSide"/> or
  /// <see cref="ChanceReserveSideOnly"/>.
  /// <para>Must not be negative; 0 means no Complexity counts as small.</para>
  /// </summary>
  public int SmallComplexityMax { get; init; } = 3;

  /// <summary>
  /// With a small Complexity of at least 2: chance that one point is left for the side pass.
  /// <para>Must be a finite value within [0, 1].</para>
  /// </summary>
  public float ChanceReserveSide { get; init; } = 0.75f;

  /// <summary>
  /// With a small Complexity of exactly 1: chance that the single point is left for the side
  /// pass, so the main pass gets nothing.
  /// <para>Must be a finite value within [0, 1].</para>
  /// </summary>
  public float ChanceReserveSideOnly { get; init; } = 0.05f;

  #endregion

  #region Main pass

  /// <summary>
  /// Main pass, per obstacle after the first: chance that the node holding the most recent
  /// solution is tried as the guarded target before the exit - a solution behind another
  /// obstacle.
  /// <para>Must be a finite value within [0, 1].</para>
  /// </summary>
  public float ChanceNestSolution { get; init; } = 0.4f;

  /// <summary>
  /// Main pass: chance base for accepting an obstacle that stacks onto passages already
  /// carrying conditions. With <c>c</c> the highest condition count on the affected passages,
  /// the obstacle is accepted with chance <c>ChanceStack ^ c</c>. Stacking still happens when
  /// nothing else can be locked.
  /// <para>Must be a finite value within [0, 1].</para>
  /// </summary>
  public float ChanceStack { get; init; } = 0.2f;

  /// <summary>
  /// Main pass: limit of stacking over the whole graph. With <c>locked</c> the number of
  /// passages carrying a condition and <c>extra</c> the number of conditions beyond the first
  /// on them, an obstacle that stacks is only placed while
  /// <c>extra &lt;= FreeStackedConditions + MaxStackLoad * locked</c> still holds afterwards.
  /// When only stacking is left and the limit forbids it, the main pass ends early and the
  /// rest of its Complexity is not used by it.
  /// <para>Must be a finite value of at least 0; 0 leaves only <see cref="FreeStackedConditions"/>,
  /// a large value switches the limit off.</para>
  /// </summary>
  public float MaxStackLoad { get; init; } = 0.25f;

  /// <summary>
  /// Main pass: number of stacked conditions (conditions beyond the first on a passage) that
  /// are always allowed, whatever <see cref="MaxStackLoad"/> says. Keeps stacking possible on
  /// tiny graphs, where a few locked passages are all there is.
  /// <para>Must not be negative.</para>
  /// </summary>
  public int FreeStackedConditions { get; init; } = 2;

  /// <summary>
  /// Main pass: how strongly the cheapest cut is preferred. Among the candidate cuts of a
  /// target, a cut's pick weight halves for every <c>CutCostHalving</c> of cut cost above the
  /// cheapest one, so the cheapest is the most likely pick but not a certain one.
  /// <para>
  /// Cut costs: 4 per chokepoint passage, 6 per other passage, +2 per condition already on the
  /// passage, 0 per secret passage. With the default 2, a cut swapping one chokepoint for an
  /// ordinary passage is half as likely; a cut of two chokepoints a quarter as likely as one
  /// chokepoint.
  /// </para>
  /// <para>Must be a finite value of at least 0; 0 always takes the cheapest cut (equal costs are rolled).</para>
  /// </summary>
  public float CutCostHalving { get; init; } = 2f;

  #endregion

  #region Bypasses

  /// <summary>
  /// Main pass, per obstacle and secret passage it would lock: chance that the secret passage
  /// stays open, a deliberate shortcut past the obstacle. Not used when the obstacle locks
  /// secret passages only, and never by the side pass.
  /// <para>Must be a finite value within [0, 1].</para>
  /// </summary>
  public float ChanceSecretBypass { get; init; } = 0.25f;

  /// <summary>
  /// Main pass, per obstacle locking at least two ordinary passages: chance that one of them
  /// stays open, so the obstacle becomes a locked shortcut with an open way around. A passage
  /// with a parallel passage locked by the same obstacle is never the open one.
  /// <para>Must be a finite value within [0, 1].</para>
  /// </summary>
  public float ChanceBypass { get; init; } = 0.01f;

  #endregion

  #region Helpers

  /// <summary>
  /// Checks that this config can be used for generation.
  /// </summary>
  /// <exception cref="ArgumentOutOfRangeException">A value is out of range.</exception>
  public void Validate()
  {
    if (MaxConditionsPerEdge < 1)
      throw new ArgumentOutOfRangeException(nameof(MaxConditionsPerEdge), MaxConditionsPerEdge, "MaxConditionsPerEdge must be at least 1.");
    if (MaxEdgesPerObstacle < 1)
      throw new ArgumentOutOfRangeException(nameof(MaxEdgesPerObstacle), MaxEdgesPerObstacle, "MaxEdgesPerObstacle must be at least 1.");
    if (MaxEdgesPerObstacleFallback < 1)
      throw new ArgumentOutOfRangeException(nameof(MaxEdgesPerObstacleFallback), MaxEdgesPerObstacleFallback, "MaxEdgesPerObstacleFallback must be at least 1.");
    if (SmallComplexityMax < 0)
      throw new ArgumentOutOfRangeException(nameof(SmallComplexityMax), SmallComplexityMax, "SmallComplexityMax must not be negative.");

    if (FreeStackedConditions < 0)
      throw new ArgumentOutOfRangeException(nameof(FreeStackedConditions), FreeStackedConditions, "FreeStackedConditions must not be negative.");
    if (!float.IsFinite(MaxStackLoad) || MaxStackLoad < 0f)
      throw new ArgumentOutOfRangeException(nameof(MaxStackLoad), MaxStackLoad, "MaxStackLoad must be a finite value of at least 0.");
    if (!float.IsFinite(CutCostHalving) || CutCostHalving < 0f)
      throw new ArgumentOutOfRangeException(nameof(CutCostHalving), CutCostHalving, "CutCostHalving must be a finite value of at least 0.");

    ValidateChance(MainShare, nameof(MainShare));
    ValidateChance(ChanceReserveSide, nameof(ChanceReserveSide));
    ValidateChance(ChanceReserveSideOnly, nameof(ChanceReserveSideOnly));
    ValidateChance(ChanceNestSolution, nameof(ChanceNestSolution));
    ValidateChance(ChanceStack, nameof(ChanceStack));
    ValidateChance(ChanceSecretBypass, nameof(ChanceSecretBypass));
    ValidateChance(ChanceBypass, nameof(ChanceBypass));
  }

  /// <summary>
  /// Checks that a share or chance is a finite value within [0, 1].
  /// </summary>
  /// <param name="value">Value to check.</param>
  /// <param name="name">Property name reported on failure.</param>
  /// <exception cref="ArgumentOutOfRangeException">The value is not finite or lies outside [0, 1].</exception>
  private static void ValidateChance(float value, string name)
  {
    if (!float.IsFinite(value) || value < 0f || value > 1f)
      throw new ArgumentOutOfRangeException(name, value, $"{name} must be a finite value within [0, 1].");
  }

  #endregion
}
