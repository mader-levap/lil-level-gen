using System;
using LostInLimbo.LevelGen.Data.Result.Graph;

namespace LostInLimbo.LevelGen.Internal.Generator.Graph.Processors;

/// <summary>
/// Parameters of <see cref="ProcNodeObjectives"/>: how many mandatory
/// (<see cref="EnPoiType.Goal"/>) and optional (<see cref="EnPoiType.OptionalGoal"/>)
/// objective POIs the pass places.
/// </summary>
/// <remarks>
/// Zero-valued behavior: <see cref="GoalAmount"/> 0 places no mandatory goals,
/// <see cref="OptGoalAmount"/> 0 places no optional goals; both 0 disable the pass.
/// Both amounts are soft: when fewer eligible nodes remain than requested, the excess
/// is ignored and the pass stops early.
/// </remarks>
internal sealed record ProcNodeObjectivesOpt
{
  /// <summary>
  /// Default instance used by builders that do not pass custom options: one mandatory
  /// goal and one optional goal.
  /// </summary>
  public static ProcNodeObjectivesOpt Default { get; } = new()
  {
    GoalAmount = 1,
    OptGoalAmount = 1,
  };

  /// <summary>
  /// Amount of mandatory goal POIs to place; 0 disables mandatory goals. Must not be
  /// negative.
  /// </summary>
  public int GoalAmount { get; init; } = 1;

  /// <summary>
  /// Amount of optional goal POIs to place; 0 disables optional goals. Must not be
  /// negative.
  /// </summary>
  public int OptGoalAmount { get; init; } = 1;

  /// <summary>
  /// Checks that these options are valid.
  /// </summary>
  /// <exception cref="ArgumentOutOfRangeException">
  /// Some parameter has invalid value.
  /// </exception>
  public void Validate()
  {
    if (GoalAmount < 0)
      throw new ArgumentOutOfRangeException(nameof(GoalAmount), GoalAmount, "GoalAmount must not be negative.");
    if (OptGoalAmount < 0)
      throw new ArgumentOutOfRangeException(nameof(OptGoalAmount), OptGoalAmount, "OptGoalAmount must not be negative.");
  }
}
