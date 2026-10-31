using System;
using System.Collections.Generic;

namespace LostInLimbo.LevelGen.Internal.Utils.Rng;

/// <summary>
/// Deterministic pseudorandom number generator used by generation algorithms.
/// </summary>
/// <remarks>
/// <para>
/// Based on SplitMix64 (Steele, Lea, Flood - "Fast splittable pseudorandom number
/// generators", OOPSLA 2014) and reuses <see cref="SeedDerivation.Mix(long)"/> for the
/// mixing step, so its output sequence is stable across platforms and process runs -
/// unlike <see cref="System.Random"/>, whose seeded sequence is not guaranteed to be
/// stable across .NET versions.
/// </para>
/// <para>
/// The same seed always produces the same sequence. Generation logic must never share
/// one instance between independent generation passes; derive one instance per pass
/// from its own sub-seed.
/// </para>
/// </remarks>
internal sealed class SeededRandom
{
  /// <summary>SplitMix64 golden-ratio gamma; the state increment.</summary>
  private const ulong GoldenGamma = 0x9E3779B97F4A7C15UL;

  /// <summary>Current generator state.</summary>
  private ulong _state;

  /// <summary>
  /// Creates a generator whose sequence is fully determined by <paramref name="seed"/>.
  /// </summary>
  /// <param name="seed">Seed for the state.</param>
  public SeededRandom(long seed)
  {
    _state = unchecked((ulong)seed);
  }

  // Simple random numbers.

  /// <summary>
  /// Produces the next raw 64-bit value of the sequence.
  /// </summary>
  /// <returns>Next value in the sequence. Deterministic for the same seed.</returns>
  public ulong NextULong()
  {
    // Canonical SplitMix64 step: Mix(state) already adds the gamma internally,
    // so the stored state is advanced by the gamma only.
    var value = unchecked((ulong)SeedDerivation.Mix(unchecked((long)_state)));
    _state += GoldenGamma;
    return value;
  }

  /// <summary>
  /// Produces a non-negative random integer less than <paramref name="maxExclusive"/>.
  /// </summary>
  /// <param name="maxExclusive">Upper bound, exclusive; must be positive.</param>
  /// <returns>Value in <c>[0, maxExclusive)</c>. Unbiased.</returns>
  /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxExclusive"/> is not positive.</exception>
  public int NextInt(int maxExclusive)
  {
    if (maxExclusive < 1)
      throw new ArgumentOutOfRangeException(nameof(maxExclusive), maxExclusive, "Upper bound must be positive.");

    // Draw a 32-bit value and reject the tail that would bias the modulo result.
    var range = (ulong)(uint)maxExclusive;
    var limit = 0x1_0000_0000UL - (0x1_0000_0000UL % range);
    ulong draw;
    do
    {
      draw = (uint)NextULong();
    } while (draw >= limit);

    return (int)(draw % range);
  }

  /// <summary>
  /// Produces the next uniform float in <c>[0, 1)</c> with 2^24 distinct values.
  /// </summary>
  /// <returns>Value in <c>[0, 1)</c>. Deterministic for the same seed and draw position.</returns>
  /// <remarks>Consumes exactly one raw draw; the 24 high bits match the float mantissa, so the result is exact.</remarks>
  private float NextUnitFloat()
  {
    return (NextULong() >> 40) * (1.0f / 16777216.0f);
  }

  /// <summary>
  /// Produces a random float less than <paramref name="maxExclusive"/>.
  /// </summary>
  /// <param name="maxExclusive">Upper bound, exclusive; must be positive and finite.</param>
  /// <returns>Value in <c>[0, maxExclusive)</c>. Deterministic for the same seed and draw position.</returns>
  /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxExclusive"/> is NaN, infinite, or not positive.</exception>
  public float NextFloat(float maxExclusive)
  {
    if (float.IsNaN(maxExclusive) || float.IsInfinity(maxExclusive))
      throw new ArgumentOutOfRangeException(nameof(maxExclusive), maxExclusive, "Upper bound must be finite.");
    if (maxExclusive <= 0.0f)
      throw new ArgumentOutOfRangeException(nameof(maxExclusive), maxExclusive, "Upper bound must be positive.");

    // Exact math guarantees maxExclusive * u < maxExclusive for every unit draw u,
    // because the smallest gap below the bound is maxExclusive * 2^-24.
    return maxExclusive * NextUnitFloat();
  }

  /// <summary>
  /// Randomly returns true or false, with true returned with probability
  /// <paramref name="chance"/>.
  /// </summary>
  /// <remarks>
  /// A chance of exactly 0 or exactly 1 short-circuits without consuming the sequence,
  /// so callers can force an outcome without shifting later draws.
  /// </remarks>
  /// <param name="chance">Probability of returning true; clamped to <c>[0, 1]</c>.</param>
  /// <returns>True or false. Deterministic for the same seed and draw position.</returns>
  /// <exception cref="ArgumentOutOfRangeException"><paramref name="chance"/> is NaN.</exception>
  public bool NextBool(float chance)
  {
    if (float.IsNaN(chance))
      throw new ArgumentOutOfRangeException(nameof(chance), chance, "Chance must not be NaN.");

    chance = Math.Clamp(chance, 0.0f, 1.0f);
    if (chance == 0.0f) return false;
    if (chance == 1.0f) return true;

    // Compare a uniform 32-bit draw against the chance scaled to [0, 2^32).
    // Double arithmetic keeps the scaling exact; the comparison is unbiased for
    // any chance the float can represent.
    var threshold = (uint)(chance * 4294967296.0);
    return (uint)NextULong() < threshold;
  }

  // Ranges.

  /// <summary>
  /// Produces a random integer from an inclusive-exclusive range.
  /// </summary>
  /// <param name="minInclusive">Lower bound, inclusive.</param>
  /// <param name="maxExclusive">Upper bound, exclusive.</param>
  /// <returns>Value in <c>[minInclusive, maxExclusive)</c> range; <paramref name="minInclusive"/> when the range is empty.</returns>
  /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxExclusive"/> is less than <paramref name="minInclusive"/>.</exception>
  public int NextInt(int minInclusive, int maxExclusive)
  {
    if (minInclusive > maxExclusive)
      throw new ArgumentOutOfRangeException(nameof(maxExclusive), maxExclusive, "Upper bound must not be lower than the lower bound.");
    if (minInclusive == maxExclusive)
      return minInclusive;

    return minInclusive + NextInt(maxExclusive - minInclusive);
  }

  /// <summary>
  /// Produces a random float from an inclusive-exclusive range.
  /// </summary>
  /// <remarks>
  /// The range width is computed in double precision so extreme ranges such as
  /// <c>[-3e38, 3e38)</c> cannot overflow to infinity. Mapped values that round up to
  /// <paramref name="maxExclusive"/> are rejected and redrawn, so the result is always
  /// strictly below the upper bound.
  /// </remarks>
  /// <param name="minInclusive">Lower bound, inclusive; must be finite.</param>
  /// <param name="maxExclusive">Upper bound, exclusive; must be finite.</param>
  /// <returns>Value in <c>[minInclusive, maxExclusive)</c>; <paramref name="minInclusive"/> when the range is empty.</returns>
  /// <exception cref="ArgumentOutOfRangeException">
  /// <paramref name="minInclusive"/> or <paramref name="maxExclusive"/> is NaN or infinite, or
  /// <paramref name="maxExclusive"/> is less than <paramref name="minInclusive"/>.
  /// </exception>
  public float NextFloat(float minInclusive, float maxExclusive)
  {
    if (float.IsNaN(minInclusive) || float.IsInfinity(minInclusive))
      throw new ArgumentOutOfRangeException(nameof(minInclusive), minInclusive, "Lower bound must be finite.");
    if (float.IsNaN(maxExclusive) || float.IsInfinity(maxExclusive))
      throw new ArgumentOutOfRangeException(nameof(maxExclusive), maxExclusive, "Upper bound must be finite.");
    if (minInclusive > maxExclusive)
      throw new ArgumentOutOfRangeException(nameof(maxExclusive), maxExclusive, "Upper bound must not be lower than the lower bound.");
    if (minInclusive == maxExclusive) return minInclusive;

    var range = (double)maxExclusive - minInclusive;
    float value;
    do
    {
      value = (float)(minInclusive + range * NextUnitFloat());
    } while (value >= maxExclusive);

    return value;
  }

  // Advanced operations.

  /// <summary>
  /// Rearranges the list into a uniformly random order using the Fisher-Yates algorithm.
  /// </summary>
  /// <param name="list">List to shuffle in place.</param>
  /// <exception cref="ArgumentNullException"><paramref name="list"/> is null.</exception>
  public void Shuffle<T>(IList<T> list)
  {
    if (list is null) throw new ArgumentNullException(nameof(list));

    for (var i = list.Count - 1; i > 0; i--)
    {
      var j = NextInt(i + 1);
      (list[i], list[j]) = (list[j], list[i]);
    }
  }
}
