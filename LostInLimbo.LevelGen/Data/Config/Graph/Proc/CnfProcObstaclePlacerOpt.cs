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

  // Limits.

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
  public float ChanceReserveSideOnly { get; init; } = 0.2f;

  // Main pass.

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

  // Bypasses.

  /// <summary>
  /// Per obstacle and secret passage it would lock: chance that the secret passage stays
  /// open, a deliberate shortcut past the obstacle.
  /// <para>Must be a finite value within [0, 1].</para>
  /// </summary>
  public float ChanceSecretBypass { get; init; } = 0.25f;

  /// <summary>
  /// Main pass, per obstacle locking at least two ordinary passages: chance that one of them
  /// stays open, so the obstacle becomes a locked shortcut with an open way around.
  /// <para>Must be a finite value within [0, 1].</para>
  /// </summary>
  public float ChanceBypass { get; init; } = 0.01f;

  // Helpers.

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
    if (SmallComplexityMax < 0)
      throw new ArgumentOutOfRangeException(nameof(SmallComplexityMax), SmallComplexityMax, "SmallComplexityMax must not be negative.");

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
}
