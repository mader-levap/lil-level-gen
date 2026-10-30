namespace LostInLimbo.LevelGen.Internal.Utils.Rng;

/// <summary>
/// Well-known salt constants that identify independent sub-seed streams derived from
/// a single world seed via <see cref="SeedDerivation"/>.
/// </summary>
/// <remarks>
/// Salts are the ASCII bytes of the channel name encoded as a big-endian hex literal
/// (for example, <c>Graph</c> is <c>"graph"</c>), so each constant is self-documenting
/// and collision-free. New channels must use new names - never reuse or renumber salts,
/// because that would change every previously generated level.
/// </remarks>
internal static class SeedChannels
{
  /// <summary>Salt for the graph generation sub-seed.</summary>
  public const long Graph = 0x6772617068L; // "graph"

  /// <summary>Salt for the map generation sub-seed.</summary>
  public const long Map = 0x6D6170L; // "map"
}
