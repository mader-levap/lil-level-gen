using System;
using System.Linq;
using LostInLimbo.LevelGen.Internal.Utils.Rng;

namespace LostInLimbo.LevelGen.Internal.Utils.Eval;

/// <summary>
/// Evaluates result using given values and their weights.
/// </summary>
internal static class EvalWeights
{
  /// <summary>
  /// Picks one int value from int array randomly using weights.
  /// </summary>
  /// <param name="random">Used random number generator.</param>
  /// <param name="values">Array of int values.</param>
  /// <param name="weights">Array of weights. Must have positive value.</param>
  /// <returns>One int value from values array.</returns>
  /// <exception cref="ArgumentException">Invalid param value.</exception>
  /// <exception cref="IndexOutOfRangeException">Could not find result. May indicate invalid params.</exception>
  public static int Int(SeededRandom random, int[] values, int[] weights)
  {
    if (values.Length == 0) throw new ArgumentException("Array of values must have at least one element.", nameof(values));
    if (values.Length != weights.Length) throw new ArgumentException("Array of values and weights must have same size.");
    if (values.Length == 1) return values[0]; // short-circuit, skips RNG draw

    int total = weights.Sum();
    int rng = random.NextInt(total);
    int accumulator = 0;
    for (int i=0; i<weights.Length; i++)
    {
      accumulator += weights[i];
      if (rng < accumulator) return values[i]; // always true on last iteration
    }
    throw new IndexOutOfRangeException(); // Should not ever happen.
  }

  /// <summary>
  /// Picks one float value from float array randomly using weights.
  /// </summary>
  /// <param name="random">Used random number generator.</param>
  /// <param name="values">Array of float values.</param>
  /// <param name="weights">Array of weights. Must have positive value.</param>
  /// <returns>One float value from values array.</returns>
  /// <exception cref="ArgumentException">Invalid param value.</exception>
  /// <exception cref="IndexOutOfRangeException">Could not find result. May indicate invalid params.</exception>
  public static float Float(SeededRandom random, float[] values, int[] weights)
  {
    if (values.Length == 0) throw new ArgumentException("Array of values must have at least one element.", nameof(values));
    if (values.Length != weights.Length) throw new ArgumentException("Array of values and weights must have same size.");
    if (values.Length == 1) return values[0]; // short-circuit, skips RNG draw

    int total = weights.Sum();
    int rng = random.NextInt(total);
    int accumulator = 0;
    for (int i=0; i<weights.Length; i++)
    {
      accumulator += weights[i];
      if (rng < accumulator) return values[i]; // always true on last iteration
    }
    throw new IndexOutOfRangeException(); // Should not ever happen.
  }
}