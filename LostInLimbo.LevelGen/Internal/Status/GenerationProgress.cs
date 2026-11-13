using System;
using LostInLimbo.LevelGen.Data.Result.Stats;
using LostInLimbo.LevelGen.Data.Status;

namespace LostInLimbo.LevelGen.Internal.Status;

/// <summary>
/// Converts the position of a run (current phase + progress within that phase) into overall
/// completion from 0.0 to 1.0, see <see cref="GenerationStatus.Completion"/>.
/// </summary>
/// <remarks>
/// <para>
/// Phases take very different amounts of time, so each phase owns a fixed share (weight) of the
/// whole range, roughly proportional to its expected duration. Weights sum to 1.0 and are laid
/// out in <see cref="EnPhase"/> order: prepass first, finalization last.
/// </para>
/// <para>
/// Calibration: graph generation measured at ~0.1-3 ms on average (up to ~25 ms) even at maximum
/// difficulty, while map generation is expected to take seconds. The map is still a placeholder,
/// so these weights MUST be re-tuned once the real map generator (and map validation in
/// finalization) exist.
/// </para>
/// <para>Pure and deterministic.</para>
/// </remarks>
internal static class GenerationProgress
{
  /// <summary>Share of overall completion owned by <see cref="EnPhase.Prepass"/>.</summary>
  public const float PrepassWeight = 0.01f;

  /// <summary>Share of overall completion owned by <see cref="EnPhase.Graph"/>.</summary>
  public const float GraphWeight = 0.04f;

  /// <summary>Share of overall completion owned by <see cref="EnPhase.Map"/>.</summary>
  public const float MapWeight = 0.90f;

  /// <summary>Share of overall completion owned by <see cref="EnPhase.Finalization"/>.</summary>
  public const float FinalizationWeight = 0.05f;

  /// <summary>
  /// Computes overall completion for a run that is inside <paramref name="phase"/>.
  /// </summary>
  /// <param name="phase">Phase the run is currently in (not yet finished).</param>
  /// <param name="phaseFraction">
  /// Progress within <paramref name="phase"/>, from 0.0 (just entered) to 1.0 (done). Values
  /// outside that range are clamped; NaN counts as 0. Ignored for <see cref="EnPhase.Completed"/>.
  /// </param>
  /// <returns>Overall completion from 0.0 to 1.0; exactly 1.0 for <see cref="EnPhase.Completed"/>.</returns>
  /// <exception cref="ArgumentOutOfRangeException"><paramref name="phase"/> is not a defined phase.</exception>
  public static float Compute(EnPhase phase, float phaseFraction)
  {
    if (!Enum.IsDefined(typeof(EnPhase), phase))
      throw new ArgumentOutOfRangeException(nameof(phase), phase, "Unknown generation phase.");

    if (phase == EnPhase.Completed) return 1.0f;

    var completion = WeightBefore(phase) + Weight(phase) * Clamp01(phaseFraction);
    return Clamp01(completion);
  }

  /// <summary>Returns the weight of a single, not completed phase.</summary>
  /// <param name="phase">Defined phase other than <see cref="EnPhase.Completed"/>.</param>
  private static float Weight(EnPhase phase)
  {
    return phase switch
    {
      EnPhase.Prepass => PrepassWeight,
      EnPhase.Graph => GraphWeight,
      EnPhase.Map => MapWeight,
      EnPhase.Finalization => FinalizationWeight,
      _ => 0.0f,
    };
  }

  /// <summary>Returns the summed weight of all phases preceding <paramref name="phase"/>.</summary>
  /// <param name="phase">Defined phase other than <see cref="EnPhase.Completed"/>.</param>
  private static float WeightBefore(EnPhase phase)
  {
    var sum = 0.0f;
    for (var previous = EnPhase.Prepass; previous < phase; previous++)
      sum += Weight(previous);
    return sum;
  }

  /// <summary>Clamps a value to [0, 1], mapping NaN to 0.</summary>
  /// <param name="value">Value to clamp.</param>
  private static float Clamp01(float value)
  {
    if (float.IsNaN(value) || value < 0.0f) return 0.0f;
    return value > 1.0f ? 1.0f : value;
  }
}
