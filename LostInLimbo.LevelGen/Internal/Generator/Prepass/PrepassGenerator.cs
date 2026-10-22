using LostInLimbo.LevelGen.Data.Config;
using LostInLimbo.LevelGen.Internal.Utils;

namespace LostInLimbo.LevelGen.Internal.Generator.Prepass;

/// <summary>
/// <para>Creates an instance of <c>PrepData</c> based on the configuration.</para>
/// </summary>
internal sealed class PrepassGenerator
{
  /// <summary>Configuration data for level generation.</summary>
  private readonly CnfLevelGeneration _config;

  /// <summary>Generated preparatory data.</summary>
  private readonly PrepData _prepData = new();

  /// <summary>
  /// Prepares the prepass generator.
  /// </summary>
  /// <param name="config">Configuration data for level generation. Must not be <see langword="null"/> and must be valid.</param>
  public PrepassGenerator(CnfLevelGeneration config)
  {
    _config = config;
  }

  /// <summary>
  /// Create prepass data.
  /// </summary>
  /// <returns>Prepass data or null if it failed for some reason.</returns>
  public PrepData Create()
  {
    var worldSeed = _config.General.Seed;
    _prepData.SeedGraph = SeedDerivation.Derive(worldSeed, SeedChannels.Graph);
    _prepData.SeedMap = SeedDerivation.Derive(worldSeed, SeedChannels.Map);
    return _prepData;
  }
}