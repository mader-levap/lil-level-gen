using System;
using System.Text;

namespace LostInLimbo.LevelGen.Internal.Utils;

/// <summary>
/// Deterministic derivation of independent sub-seeds from a single world seed.
/// </summary>
/// <remarks>
/// Based on SplitMix64 (Steele, Lea, Flood - "Fast splittable pseudorandom number
/// generators", OOPSLA 2014): pure 64-bit arithmetic with excellent avalanche, so a
/// one-bit change in the world seed or channel salt produces a completely different
/// sub-seed. Stateless and free of side effects; always returns the same output for
/// the same inputs, on every platform and process run.
/// </remarks>
internal static class SeedDerivation
{
  /// <summary>First golden-ratio constant of SplitMix64.</summary>
  private const long GoldenGamma = unchecked((long)0x9E3779B97F4A7C15UL);

  /// <summary>First odd multiplier of SplitMix64.</summary>
  private const long Multiplier1 = unchecked((long)0xBF58476D1CE4E5B9UL);

  /// <summary>Second odd multiplier of SplitMix64.</summary>
  private const long Multiplier2 = unchecked((long)0x94D049BB133111EBUL);

  /// <summary>
  /// Applies one SplitMix64 mixing step to <paramref name="value"/>.
  /// </summary>
  /// <param name="value">Input value; may be any 64-bit pattern.</param>
  /// <returns>Well-mixed 64-bit value. Deterministic for the same input.</returns>
  public static long Mix(long value)
  {
    unchecked
    {
      var z = value + GoldenGamma;
      z = (z ^ (z >>> 30)) * Multiplier1;
      z = (z ^ (z >>> 27)) * Multiplier2;
      return z ^ (z >>> 31);
    }
  }

  /// <summary>
  /// Derives the sub-seed for one channel from the world seed.
  /// </summary>
  /// <param name="worldSeed">World seed, typically <c>CnfGeneral.Seed</c>.</param>
  /// <param name="channel">Channel salt identifying the sub-seed stream; see <see cref="SeedChannels"/>.</param>
  /// <returns>Sub-seed for the given channel. Deterministic for the same inputs.</returns>
  /// <remarks>
  /// The channel salt is mixed first, then combined with the world seed and mixed
  /// again, so nearby salt values still yield independent streams.
  /// </remarks>
  public static long Derive(long worldSeed, long channel)
  {
    return Mix(worldSeed ^ Mix(channel));
  }

  /// <summary>
  /// Derives a nested sub-seed under a channel, for example a per-chunk seed.
  /// </summary>
  /// <param name="worldSeed">World seed, typically <c>CnfGeneral.Seed</c>.</param>
  /// <param name="channel">Channel salt identifying the sub-seed stream; see <see cref="SeedChannels"/>.</param>
  /// <param name="subChannel">Sub-channel salt; for chunks typically a hash of the chunk coordinates.</param>
  /// <returns>Nested sub-seed. Deterministic for the same inputs.</returns>
  /// <remarks>
  /// Equivalent to <c>Derive(Derive(worldSeed, channel), subChannel)</c> but inlined.
  /// </remarks>
  public static long Derive(long worldSeed, long channel, long subChannel)
  {
    return Mix(Derive(worldSeed, channel) ^ Mix(subChannel));
  }

  /// <summary>
  /// Hashes arbitrary text into a seed.
  /// </summary>
  /// <remarks>
  /// <para>
  /// FNV-1a 64-bit over the UTF-8 bytes of <paramref name="text"/>, finished with one
  /// <see cref="Mix"/> step so a one-character change anywhere avalanches the whole seed.
  /// The text is normalized to Unicode form C first, so composed and decomposed spellings
  /// of the same visible characters hash identically.
  /// </para>
  /// <para>
  /// Case-sensitive and culture-independent by design: <c>"Mordor"</c> and <c>"mordor"</c>
  /// are different seeds. Never uses <c>string.GetHashCode()</c>, whose result is
  /// randomized per process. Deterministic for the same text.
  /// </para>
  /// </remarks>
  /// <param name="text">Non-empty, non-whitespace text to hash.</param>
  /// <returns>Seed for the same text on every platform and process run.</returns>
  /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
  /// <exception cref="ArgumentException"><paramref name="text"/> is empty or whitespace.</exception>
  public static long FromText(string text)
  {
    if (text is null)
      throw new ArgumentNullException(nameof(text));

    if (string.IsNullOrWhiteSpace(text))
      throw new ArgumentException("Seed text must not be empty or whitespace.", nameof(text));

    var bytes = Encoding.UTF8.GetBytes(text.Normalize(NormalizationForm.FormC));

    // FNV-1a 64-bit: offset basis and prime as published at http://www.isthe.com/chongo/tech/comp/fnv/.
    var hash = 0xcbf29ce484222325UL;
    for (var i = 0; i < bytes.Length; i++)
    {
      hash ^= bytes[i];
      hash *= 0x100000001b3UL;
    }

    return Mix(unchecked((long)hash));
  }
}
