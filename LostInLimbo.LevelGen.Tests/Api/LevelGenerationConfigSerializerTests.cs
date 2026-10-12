using LostInLimbo.LevelGen.Api;
using LostInLimbo.LevelGen.Data.Config;

namespace LostInLimbo.LevelGen.Tests.Api;

public sealed class LevelGenerationConfigSerializerTests
{
  [Fact]
  public void Serialize_WithNullConfig_ThrowsArgumentNullException()
  {
    // Act/Assert: Null input is rejected.
    Assert.Throws<ArgumentNullException>(() => LevelGenerationConfigSerializer.Serialize(null!));
  }

  [Fact]
  public void Serialize_WithDefaultConfig_ProducesIndentedCamelCaseJson()
  {
    // Arrange: A configuration with a known seed.
    var config = new LevelGenerationConfig { Seed = 12345 };

    // Act: Serialize it.
    var json = LevelGenerationConfigSerializer.Serialize(config);

    // Assert: The canonical camelCase names and values appear in the output.
    Assert.Contains("\"configVersion\": 1", json);
    Assert.Contains("\"seed\": 12345", json);
    Assert.Contains("\"levelSize\"", json);
    Assert.Contains("\"y\": 3", json);
    Assert.Contains("\"chunkSize\"", json);
  }

  [Fact]
  public void Serialize_WithSameConfig_ProducesIdenticalJson()
  {
    // Arrange: Two independently built but equal configurations.
    var first = new LevelGenerationConfig { Seed = 7 };
    var second = new LevelGenerationConfig { Seed = 7 };

    // Act: Serialize both.
    var firstJson = LevelGenerationConfigSerializer.Serialize(first);
    var secondJson = LevelGenerationConfigSerializer.Serialize(second);

    // Assert: Serialization is deterministic for equal input.
    Assert.Equal(firstJson, secondJson);
  }

  [Fact]
  public void RoundTrip_WithCustomValues_ProducesEqualConfig()
  {
    // Arrange: A configuration with non-default values on every field.
    var config = new LevelGenerationConfig
    {
      Seed = -9876543210L,
      LevelSize = new LevelSize { X = 5, Y = 4, Z = 2 },
      ChunkSize = new ChunkSize { X = 8, Y = 12, Z = 24 },
    };

    // Act: Serialize and deserialize the configuration.
    var json = LevelGenerationConfigSerializer.Serialize(config);
    var roundTripped = LevelGenerationConfigSerializer.Deserialize(json);

    // Assert: The round-tripped configuration is equal by value.
    Assert.Equal(config, roundTripped);
  }

  [Fact]
  public void Deserialize_WithCamelCaseSample_ProducesExpectedValues()
  {
    // Arrange: Hand-written JSON in the canonical shape.
    const string json = """
      {
        "configVersion": 1,
        "seed": 42,
        "levelSize": { "x": 4, "y": 5, "z": 6 },
        "chunkSize": { "x": 32, "y": 4, "z": 8 }
      }
      """;

    // Act: Deserialize the sample.
    var config = LevelGenerationConfigSerializer.Deserialize(json);

    // Assert: Every value from the JSON is present.
    Assert.Equal(1, config.ConfigVersion);
    Assert.Equal(42L, config.Seed);
    Assert.Equal(4, config.LevelSize.X);
    Assert.Equal(5, config.LevelSize.Y);
    Assert.Equal(6, config.LevelSize.Z);
    Assert.Equal(32, config.ChunkSize.X);
    Assert.Equal(4, config.ChunkSize.Y);
    Assert.Equal(8, config.ChunkSize.Z);
  }

  [Fact]
  public void Deserialize_WithMissingProperties_UsesDefaults()
  {
    // Arrange: JSON that only specifies the seed.
    const string json = """{ "seed": 42 }""";

    // Act: Deserialize the minimal sample.
    var config = LevelGenerationConfigSerializer.Deserialize(json);

    // Assert: The seed is set and everything else falls back to defaults.
    Assert.Equal(42L, config.Seed);
    Assert.Equal(LevelGenerationConfig.CurrentConfigVersion, config.ConfigVersion);
    Assert.Equal(LevelSize.Default, config.LevelSize);
    Assert.Equal(ChunkSize.Default, config.ChunkSize);
  }

  [Fact]
  public void Deserialize_WithPascalCaseProperties_Succeeds()
  {
    // Arrange: Hand-written JSON using PascalCase, tolerated for convenience.
    const string json = """{ "Seed": 42 }""";

    // Act: Deserialize the sample.
    var config = LevelGenerationConfigSerializer.Deserialize(json);

    // Assert: Property matching is case-insensitive.
    Assert.Equal(42L, config.Seed);
  }

  [Fact]
  public void Deserialize_WithNullJson_ThrowsArgumentNullException()
  {
    // Act/Assert: Null input is rejected.
    Assert.Throws<ArgumentNullException>(() => LevelGenerationConfigSerializer.Deserialize(null!));
  }

  [Theory]
  [InlineData("")]
  [InlineData("   ")]
  public void Deserialize_WithEmptyJson_ThrowsArgumentException(string json)
  {
    // Act/Assert: Empty input is rejected.
    Assert.Throws<ArgumentException>(() => LevelGenerationConfigSerializer.Deserialize(json));
  }

  [Fact]
  public void Deserialize_WithMalformedJson_ThrowsArgumentException()
  {
    // Arrange: Text that is not valid JSON.
    const string json = "{ not json";

    // Act/Assert: The parse failure is reported as an argument exception naming the parameter.
    var exception = Assert.Throws<ArgumentException>(
      () => LevelGenerationConfigSerializer.Deserialize(json));
    Assert.Equal("json", exception.ParamName);
  }

  [Fact]
  public void Deserialize_WithNewerConfigVersion_ThrowsArgumentException()
  {
    // Arrange: JSON claiming a schema version this library does not understand.
    const string json = """{ "configVersion": 99 }""";

    // Act/Assert: The version check rejects it.
    var exception = Assert.Throws<ArgumentException>(
      () => LevelGenerationConfigSerializer.Deserialize(json));
    Assert.Equal("json", exception.ParamName);
  }

  [Theory]
  [InlineData("""{ "seed": 1, "chunkSize": { "x": 0, "z": 16 } }""")]
  [InlineData("""{ "seed": 1, "chunkSize": { "x": 16, "y": 0, "z": 16 } }""")]
  public void Deserialize_WithInvalidValues_ThrowsArgumentOutOfRangeException(string json)
  {
    // Act/Assert: The invariant check rejects it.
    Assert.Throws<ArgumentOutOfRangeException>(
      () => LevelGenerationConfigSerializer.Deserialize(json));
  }
}
