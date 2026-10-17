using System;
using System.Collections.Generic;

namespace LostInLimbo.LevelGen.Internal.Utils;

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
/// from its own sub-seed (e.g. <c>PrepData.SeedGraph</c>).
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
  /// <param name="seed">Seed for the state; typically a sub-seed such as <c>PrepData.SeedGraph</c>.</param>
  public SeededRandom(long seed)
  {
    _state = unchecked((ulong)seed);
  }

  //

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
  /// Produces a random integer from an inclusive-exclusive range.
  /// </summary>
  /// <param name="minInclusive">Lower bound, inclusive.</param>
  /// <param name="maxExclusive">Upper bound, exclusive.</param>
  /// <returns>Value in <c>[minInclusive, maxExclusive)</c>; <paramref name="minInclusive"/> when the range is empty.</returns>
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
  /// Randomly returns true or false.
  /// </summary>
  /// <param name="Chance">Chance for returning true.</param>
  /// <returns>True or false.</returns>
  public bool NextBool(float Chance)
  {
    Chance = Math.Clamp(Chance, 0.0f, 1.0f);
    if (Chance == 0.0f) return false;
    if (Chance == 1.0f) return true;

    // PLACEHOLDER: actually get random chance for true/false
    return true;
  }

  //

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
