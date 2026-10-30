using System;
using LostInLimbo.LevelGen.Internal.Utils.Rng;

namespace LostInLimbo.LevelGen.Internal.Utils.Eval;

/// <summary>
/// Evaluates result using given value range and curve representing probability of getting particular value at given point.
/// </summary>
internal static class EvalCurves
{
  /// <summary>
  /// Get float value in range between from and to, using gaussian curve (normal distribution).
  /// </summary>
  /// <remarks>
  /// <para>
  /// A standard normal variate is produced with the Box-Muller transform from two deterministic
  /// uniform draws derived from the inputs, then scaled so roughly 95% of the distribution falls
  /// inside the range and the rest is clamped to the bounds.
  /// </para>
  /// </remarks>
  /// <param name="random">Used random number generator.</param>
  /// <param name="from">Start value, inclusive lower bound of the result.</param>
  /// <param name="to">End value, inclusive upper bound of the result.</param>
  /// <returns>Value between <paramref name="from"/> and <paramref name="to"/>, with probability
  /// following a gaussian curve centered on the midpoint of the range.</returns>
  /// <exception cref="ArgumentException"><paramref name="from"/> is greater than <paramref name="to"/>.</exception>
  public static float Gaussian(SeededRandom random, float from, float to)
  {
    if (from > to) throw new ArgumentException("From must not be greater than to.", nameof(from));
    if (from == to) return from;

    float mean = (from + to) * 0.5f;
    float sigma = (to - from) * 0.25f;

    double u1 = random.NextFloat(1.0f);
    double u2 = random.NextFloat(1.0f);

    // Box-Muller requires u1 in (0, 1); guard the measure-zero u1 == 0 case.
    if (u1 <= 0.0) u1 = 1e-6;

    double z0 = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
    float value = mean + sigma * (float)z0;

    return value < from ? from : value > to ? to : value;
  }
}