using LostInLimbo.LevelGen.Data.Config;

namespace LostInLimbo.LevelGen.Tests.Data.Config;

public sealed class CnfLevelGenerationTests
{
  [Fact]
  public void NewConfig_UsesCurrentVersionAndZeroSeed()
  {
    // Arrange: A configuration with default values.
    var config = new CnfLevelGeneration();

    // Assert: Version matches the current format and the seed is the neutral default.
    Assert.Equal(CnfLevelGeneration.CurrentConfigVersion, config.ConfigVersion);
    Assert.Equal(0L, config.Seed);
  }

  [Fact]
  public void NewConfig_UsesDefaultSizes()
  {
    // Arrange: A configuration with default values.
    var config = new CnfLevelGeneration();

    // Assert: The level is 3x3x3 chunks of 16x16x16 cells.
    Assert.Equal(3, config.LevelSize.X);
    Assert.Equal(3, config.LevelSize.Y);
    Assert.Equal(3, config.LevelSize.Z);
    Assert.Equal(16, config.ChunkSize.X);
    Assert.Equal(16, config.ChunkSize.Y);
    Assert.Equal(16, config.ChunkSize.Z);
  }

  [Fact]
  public void Validate_DefaultConfig_DoesNotThrow()
  {
    // Arrange: A configuration with default values.
    var config = new CnfLevelGeneration();

    // Act/Assert: The default configuration is valid (no exception is thrown).
    config.Validate();
  }

  [Theory]
  [InlineData(0, 3, 3)]
  [InlineData(-1, 3, 3)]
  [InlineData(3, 0, 3)]
  [InlineData(3, -5, 3)]
  [InlineData(3, 3, 0)]
  [InlineData(3, 3, -5)]
  public void Validate_WithInvalidLevelSize_ThrowsArgumentOutOfRangeException(int x, int y, int z)
  {
    // Arrange: A configuration whose level size is invalid along one axis.
    var config = new CnfLevelGeneration { LevelSize = new CnfLevelSize { X = x, Y = y, Z = z } };

    // Act/Assert: Validation rejects it.
    Assert.Throws<ArgumentOutOfRangeException>(() => config.Validate());
  }

  [Theory]
  [InlineData(0, 16, 16)]
  [InlineData(-2, 16, 16)]
  [InlineData(16, 0, 16)]
  [InlineData(16, -1, 16)]
  [InlineData(16, 16, 0)]
  [InlineData(16, 16, -1)]
  public void Validate_WithInvalidChunkSize_ThrowsArgumentOutOfRangeException(int x, int y, int z)
  {
    // Arrange: A configuration whose chunk size is invalid along one axis.
    var config = new CnfLevelGeneration { ChunkSize = new CnfChunkSize { X = x, Y = y, Z = z } };

    // Act/Assert: Validation rejects it.
    Assert.Throws<ArgumentOutOfRangeException>(() => config.Validate());
  }

  [Fact]
  public void Validate_WithInvalidConfigVersion_ThrowsArgumentOutOfRangeException()
  {
    // Arrange: A configuration claiming a schema version below 1.
    var config = new CnfLevelGeneration { ConfigVersion = 0 };

    // Act/Assert: Validation rejects it.
    Assert.Throws<ArgumentOutOfRangeException>(() => config.Validate());
  }

  [Fact]
  public void Validate_WithNullSection_ThrowsArgumentException()
  {
    // Arrange: A configuration with a section forced to null (defensive against hand-built data).
    var config = new CnfLevelGeneration { LevelSize = null! };

    // Act/Assert: Validation rejects it with the base argument exception, not the range one.
    Assert.Throws<ArgumentException>(() => config.Validate());
  }

  [Fact]
  public void SameValues_ProduceEqualConfigs()
  {
    // Arrange: Two independently built configurations with identical values.
    var first = new CnfLevelGeneration { Seed = 42 };
    var second = new CnfLevelGeneration { Seed = 42 };

    // Assert: Records compare by value, including nested sections.
    Assert.Equal(first, second);
  }

  [Fact]
  public void DifferentSeeds_ProduceUnequalConfigs()
  {
    // Arrange: Two configurations differing only in seed.
    var first = new CnfLevelGeneration { Seed = 1 };
    var second = new CnfLevelGeneration { Seed = 2 };

    // Assert: The seed is part of value equality.
    Assert.NotEqual(first, second);
  }
}
