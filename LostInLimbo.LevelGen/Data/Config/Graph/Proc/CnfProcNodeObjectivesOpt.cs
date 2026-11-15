using System;
using LostInLimbo.LevelGen.Data.Result.Graph;

namespace LostInLimbo.LevelGen.Data.Config.Graph.Proc;

/// <summary>
/// <para>
/// Configuration of the objective-POIs processor (<c>ProcNodeObjectives</c>): how many
/// mandatory (<see cref="EnPoiType.Goal"/>) and optional (<see cref="EnPoiType.OptionalGoal"/>)
/// objective POIs it places, and how strongly it avoids nodes that already hold POIs.
/// </para>
/// </summary>
/// <remarks>
/// <para>
/// Zero-valued behavior: <see cref="GoalAmount"/> 0 places no mandatory goals,
/// <see cref="OptGoalAmount"/> 0 places no optional goals; both 0 disable the processor.
/// Both counts are soft: when fewer eligible nodes remain than requested, the excess
/// is ignored and the processor stops early.
/// </para>
/// <para>
/// Host weights: every eligible node is picked with a chance proportional to its weight -
/// <see cref="WeightNoPoi"/> for a node without POIs, <see cref="WeightOtherPoi"/> for a node
/// with POIs other than Start/Exit and <see cref="WeightStartExitPoi"/> for a node with a
/// Start or Exit POI (this class wins when a node qualifies for both). The intended order is
/// <c>WeightNoPoi &gt;= WeightOtherPoi &gt;= WeightStartExitPoi</c>, but it is not enforced.
/// Weights are at least 1, so they only bias the choice and never exclude a node.
/// </para>
/// </remarks>
public sealed record CnfProcNodeObjectivesOpt
{
  /// <summary>
  /// Default instance: one mandatory goal and one optional goal; host weights 4 / 2 / 1.
  /// </summary>
  public static CnfProcNodeObjectivesOpt Default { get; } = new()
  {
    GoalAmount = 1,
    OptGoalAmount = 1,
    WeightNoPoi = 4,
    WeightOtherPoi = 2,
    WeightStartExitPoi = 1,
  };

  /// <summary>
  /// Number of mandatory goal POIs to place; 0 disables mandatory goals. Must not be
  /// negative.
  /// </summary>
  public int GoalAmount { get; init; } = 1;

  /// <summary>
  /// Number of optional goal POIs to place; 0 disables optional goals. Must not be
  /// negative.
  /// </summary>
  public int OptGoalAmount { get; init; } = 1;

  /// <summary>
  /// Relative pick weight of an eligible node holding no POI. Must be at least 1.
  /// </summary>
  public int WeightNoPoi { get; init; } = 4;

  /// <summary>
  /// Relative pick weight of an eligible node holding POIs, none of them Start or Exit
  /// (e.g. a Solution). Must be at least 1.
  /// </summary>
  public int WeightOtherPoi { get; init; } = 2;

  /// <summary>
  /// Relative pick weight of an eligible node holding a Start or Exit POI, regardless of
  /// its other POIs. Must be at least 1.
  /// </summary>
  public int WeightStartExitPoi { get; init; } = 1;

  // Helpers.

  /// <summary>
  /// Checks that this config can be used for generation.
  /// </summary>
  /// <exception cref="ArgumentOutOfRangeException">A value is out of range.</exception>
  public void Validate()
  {
    if (GoalAmount < 0)
      throw new ArgumentOutOfRangeException(nameof(GoalAmount), GoalAmount, "GoalAmount must not be negative.");
    if (OptGoalAmount < 0)
      throw new ArgumentOutOfRangeException(nameof(OptGoalAmount), OptGoalAmount, "OptGoalAmount must not be negative.");
    if (WeightNoPoi < 1)
      throw new ArgumentOutOfRangeException(nameof(WeightNoPoi), WeightNoPoi, "WeightNoPoi must be at least 1.");
    if (WeightOtherPoi < 1)
      throw new ArgumentOutOfRangeException(nameof(WeightOtherPoi), WeightOtherPoi, "WeightOtherPoi must be at least 1.");
    if (WeightStartExitPoi < 1)
      throw new ArgumentOutOfRangeException(nameof(WeightStartExitPoi), WeightStartExitPoi, "WeightStartExitPoi must be at least 1.");
  }
}
