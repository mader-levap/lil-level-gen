using System;
using LostInLimbo.LevelGen.Data.Result.Graph;

namespace LostInLimbo.LevelGen.Data.Config.Graph;

/// <summary>
/// <para>
/// Configuration of the objective-POIs processor (<c>ProcNodeObjectives</c>): how many
/// mandatory (<see cref="EnPoiType.Goal"/>) and optional (<see cref="EnPoiType.OptionalGoal"/>)
/// objective POIs it places.
/// </para>
/// </summary>
/// <remarks>
/// Zero-valued behavior: <see cref="GoalAmount"/> 0 places no mandatory goals,
/// <see cref="OptGoalAmount"/> 0 places no optional goals; both 0 disable the processor.
/// Both counts are soft: when fewer eligible nodes remain than requested, the excess
/// is ignored and the processor stops early.
/// </remarks>
public sealed record CnfProcNodeObjectivesOpts
{
  /// <summary>
  /// Default instance: one mandatory goal and one optional goal.
  /// </summary>
  public static CnfProcNodeObjectivesOpts Default { get; } = new()
  {
    GoalAmount = 1,
    OptGoalAmount = 1,
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
  /// Checks that this config can be used for generation.
  /// </summary>
  /// <exception cref="ArgumentOutOfRangeException">A value is out of range.</exception>
  public void Validate()
  {
    if (GoalAmount < 0)
      throw new ArgumentOutOfRangeException(nameof(GoalAmount), GoalAmount, "GoalAmount must not be negative.");
    if (OptGoalAmount < 0)
      throw new ArgumentOutOfRangeException(nameof(OptGoalAmount), OptGoalAmount, "OptGoalAmount must not be negative.");
  }
}
