using System;

namespace LostInLimbo.LevelGen.Internal.Utils;

/// <summary>
/// Deterministic derivation of identifiers (<see cref="Guid"/>) from a seed and a role salt.
/// </summary>
/// <remarks>
/// Built on <see cref="SeedDerivation"/> so the same inputs always produce the same identifier,
/// on every platform and process run. Generation logic must use this (or an equally
/// deterministic source) instead of <c>Guid.NewGuid()</c>.
/// </remarks>
internal static class IdDerivation
{
  /// <summary>
  /// Derives a deterministic identifier from <paramref name="seed"/> and a role salt.
  /// </summary>
  /// <param name="seed">Seed to derive from, typically a sub-seed such as <c>PrepData.SeedGraph</c>.</param>
  /// <param name="salt">Role salt identifying which identifier to derive (e.g. node vs. start POI).</param>
  /// <returns>Identifier that is always equal for the same inputs. Not <see cref="Guid.Empty"/>.</returns>
  /// <remarks>
  /// The 16 identifier bytes are filled explicitly in little-endian order from two independent
  /// mixes, so the result does not depend on platform endianness. The result is never
  /// <see cref="Guid.Empty"/>: if the first half is non-zero the identifier already differs from
  /// empty, and if the first half is zero the second half is <c>Mix(0)</c>, which is non-zero.
  /// </remarks>
  public static Guid Derive(long seed, long salt)
  {
    long first = SeedDerivation.Derive(seed, salt);
    long second = SeedDerivation.Mix(first);

    var bytes = new byte[16];
    for (var i = 0; i < 8; i++)
    {
      bytes[i] = (byte)(first >> (i * 8));
      bytes[i + 8] = (byte)(second >> (i * 8));
    }

    return new Guid(bytes);
  }
}
