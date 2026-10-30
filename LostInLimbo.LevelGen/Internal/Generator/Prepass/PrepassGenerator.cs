using LostInLimbo.LevelGen.Data.Config;
using LostInLimbo.LevelGen.Internal.Utils.Rng;

namespace LostInLimbo.LevelGen.Internal.Generator.Prepass;

/// <summary>
/// <para>Creates an instance of <c>PrepData</c> based on the configuration.</para>
/// </summary>
/// <remarks>
/// <para>
/// Currently only generates sub-seeds.
/// </para>
/// </remarks>
/// <remarks>
/// Prepares the prepass generator.
/// </remarks>
/// <param name="config">Configuration data for level generation. Must not be <see langword="null"/> and must be valid.</param>
internal sealed class PrepassGenerator(CnfLevelGeneration config)
{
  /// <summary>Configuration data for level generation.</summary>
  private readonly CnfLevelGeneration _config = config;

  /// <summary>Generated preparatory data.</summary>
  private readonly PrepData _prepData = new();

  /// <summary>
  /// Create prepass data.
  /// </summary>
  /// <returns>The prepared prepass data.</returns>
  public PrepData Create()
  {
    var worldSeed = _config.General.Seed;
    _prepData.SeedGraph = SeedDerivation.Derive(worldSeed, SeedChannels.Graph);
    _prepData.SeedMap = SeedDerivation.Derive(worldSeed, SeedChannels.Map);
    return _prepData;
  }
}