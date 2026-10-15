using LostInLimbo.LevelGen.Data.Config;
using LostInLimbo.LevelGen.Internal.Generator.Prepass;
using LostInLimbo.LevelGen.Internal.Utils;

namespace LostInLimbo.LevelGen.Tests.Generator.Prepass;

/// <summary>
/// Tests that the prepass derives independent, deterministic sub-seeds from the world seed.
/// </summary>
public sealed class PrepassGeneratorTests
{
  [Fact]
  public void Create_WithWorldSeed_DerivesSubSeedsFromSeedChannels()
  {
    // Arrange: Config with a known world seed.
    var config = CnfLevelGeneration.Default with
    {
      General = new CnfGeneral { Seed = 42 },
    };
    var generator = new PrepassGenerator(config);

    // Act: Create the prepass data.
    var prepData = generator.Create();

    // Assert: Sub-seeds are the channel derivations of the world seed, not placeholders.
    Assert.Equal(SeedDerivation.Derive(42, SeedChannels.Graph), prepData.SeedGraph);
    Assert.Equal(SeedDerivation.Derive(42, SeedChannels.Map), prepData.SeedMap);
  }

  [Fact]
  public void Create_WithSameConfig_IsDeterministic()
  {
    // Arrange: Two generators over identical configs.
    var config = CnfLevelGeneration.Default with
    {
      General = new CnfGeneral { Seed = 1234 },
    };

    // Act: Create prepass data twice from separate generator instances.
    var first = new PrepassGenerator(config).Create();
    var second = new PrepassGenerator(config).Create();

    // Assert: Both runs produce identical sub-seeds.
    Assert.Equal(first.SeedGraph, second.SeedGraph);
    Assert.Equal(first.SeedMap, second.SeedMap);
  }

  [Fact]
  public void Create_WithDifferentWorldSeeds_ProducesDifferentSubSeeds()
  {
    // Arrange: Two configs that differ only in the world seed.
    var generatorA = new PrepassGenerator(CnfLevelGeneration.Default with
    {
      General = new CnfGeneral { Seed = 1 },
    });
    var generatorB = new PrepassGenerator(CnfLevelGeneration.Default with
    {
      General = new CnfGeneral { Seed = 2 },
    });

    // Act: Create prepass data for both configs.
    var prepA = generatorA.Create();
    var prepB = generatorB.Create();

    // Assert: Neither sub-seed is shared between the two world seeds.
    Assert.NotEqual(prepA.SeedGraph, prepB.SeedGraph);
    Assert.NotEqual(prepA.SeedMap, prepB.SeedMap);
  }

  [Fact]
  public void Create_WithAnyWorldSeed_ProducesIndependentChannelSubSeeds()
  {
    // Arrange: Config with the default world seed.
    var config = CnfLevelGeneration.Default;
    var generator = new PrepassGenerator(config);

    // Act: Create the prepass data.
    var prepData = generator.Create();

    // Assert: Graph and Map sub-seeds never collapse onto the same value.
    Assert.NotEqual(prepData.SeedGraph, prepData.SeedMap);
  }
}
