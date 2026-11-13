using LostInLimbo.LevelGen.Data.Config;
using LostInLimbo.LevelGen.Internal.Utils.Rng;

namespace LostInLimbo.LevelGen.Internal.Generator.Prepass;

/// <summary>
/// <para>Creates an instance of <see cref="PrepData"/> based on the configuration.</para>
/// </summary>
/// <remarks>
/// <para>
/// Currently only generates sub-seeds.
/// </para>
/// </remarks>
/// <param name="config">Configuration data for level generation. Must not be <see langword="null"/> and must be valid.</param>
internal sealed class PrepassGenerator(CnfLevelGeneration config)
{
  /// <summary>Configuration data for level generation.</summary>
  private readonly CnfLevelGeneration _config = config;

  /// <summary>
  /// Create prepass data. Deterministic: every call builds a new, equal instance for the same config.
  /// </summary>
  /// <returns>The prepared prepass data.</returns>
  public PrepData Create()
  {
    var worldSeed = _config.General.Seed;
    return new PrepData
    {
      SeedGraph = SeedDerivation.Derive(worldSeed, SeedChannels.Graph),
      SeedMap = SeedDerivation.Derive(worldSeed, SeedChannels.Map),
    };
  }
}
