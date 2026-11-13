using System;
using LostInLimbo.LevelGen.Internal.Utils.Rng;

namespace LostInLimbo.LevelGen.Internal.Utils.Evals;

/// <summary>
/// Evaluates result using given values and their weights.
/// </summary>
internal static class EvalWeights
{
  /// <summary>
  /// Picks one value randomly, each with a chance proportional to its weight. Deterministic for
  /// the same generator state and inputs; consumes one draw unless there is a single value.
  /// </summary>
  /// <remarks>
  /// Meant for hot generation loops, so only constant-time checks run (nulls, empty values, size
  /// mismatch). <b>The caller is responsible for valid weights</b>: every weight must be
  /// non-negative and their total must be positive and must not exceed <see cref="int.MaxValue"/>.
  /// A zero weight is allowed and its value is never picked. Invalid weights are not detected:
  /// a total that is zero, negative or overflowed makes the draw throw
  /// <see cref="ArgumentOutOfRangeException"/>; negative weights mixed with positive ones skew the
  /// chances without any error.
  /// </remarks>
  /// <typeparam name="T">Type of the values.</typeparam>
  /// <param name="random">Used random number generator.</param>
  /// <param name="values">Values to pick from; at least one.</param>
  /// <param name="weights">Weight of each value, matched by index. See remarks for the caller's obligations.</param>
  /// <returns>One value from <paramref name="values"/>.</returns>
  /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
  /// <exception cref="ArgumentException">
  /// <paramref name="values"/> is empty, or <paramref name="weights"/> differs from it in size.
  /// </exception>
  /// <exception cref="ArgumentOutOfRangeException">The weight total is not positive (see remarks).</exception>
  public static T Pick<T>(SeededRandom random, T[] values, int[] weights)
  {
    if (random is null) throw new ArgumentNullException(nameof(random));
    if (values is null) throw new ArgumentNullException(nameof(values));
    if (weights is null) throw new ArgumentNullException(nameof(weights));
    if (values.Length == 0) throw new ArgumentException("Values must have at least one element.", nameof(values));
    if (values.Length != weights.Length)
      throw new ArgumentException(
        $"Weights must have the same size as values ({weights.Length} weights for {values.Length} values).",
        nameof(weights));
    if (values.Length == 1) return values[0]; // short-circuit, skips RNG draw

    int total = 0;
    for (int i = 0; i < weights.Length; i++) total += weights[i];

    int roll = random.NextInt(total);
    int accumulator = 0;
    for (int i = 0; i < weights.Length; i++)
    {
      accumulator += weights[i];
      if (roll < accumulator) return values[i]; // always true on last iteration: accumulator == total
    }
    // Unreachable, unless another thread modifies the weights during the pick.
    throw new InvalidOperationException("Weighted pick found no value.");
  }
}
