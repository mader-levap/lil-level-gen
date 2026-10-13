using System.Text.Json;

using LostInLimbo.LevelGen.Api;
using LostInLimbo.LevelGen.Data.Config;

namespace LostInLimbo.LevelGen.Tests.Api;

/// <summary>
/// Tests the canonical JSON (de)serialization of level generation config.
/// </summary>
public sealed class LevelGenerationSerializerTests
{
  [Fact]
  public void Serialize_WithNullConfig_ThrowsArgumentNullException()
  {
    // Act/Assert: Null config is rejected.
    Assert.Throws<ArgumentNullException>(() => LevelGenerationSerializer.Serialize(null!));
  }

  [Fact]
  public void Serialize_WithValidConfig_WritesCamelCaseIndentedJson()
  {
    // Arrange: Default configuration.
    var config = CnfLevelGeneration.Default;

    // Act: Serialize to canonical JSON.
    var json = LevelGenerationSerializer.Serialize(config);

    // Assert: Property names are camelCase and every key starts a new indented line.
    Assert.Contains("\"configVersion\": 1", json);
    Assert.Matches(@"\r?\n +""general""", json);
  }

  [Fact]
  public void Serialize_WithInvalidConfig_ThrowsBeforeWriting()
  {
    // Arrange: Config violating its own invariants.
    var config = CnfLevelGeneration.Default with
    {
      General = new CnfGeneral { Corruption = -1.0f },
    };

    // Act/Assert: Serialization validates before producing output.
    Assert.Throws<ArgumentOutOfRangeException>(() => LevelGenerationSerializer.Serialize(config));
  }

  [Fact]
  public void Deserialize_WithNull_ThrowsArgumentNullException()
  {
    // Act/Assert: Null input is rejected.
    Assert.Throws<ArgumentNullException>(() => LevelGenerationSerializer.Deserialize(null!));
  }

  [Theory]
  [InlineData("")]
  [InlineData("   ")]
  public void Deserialize_WithEmptyJson_ThrowsArgumentException(string json)
  {
    // Act/Assert: Empty input is rejected before parsing, naming the parameter.
    var exception = Assert.Throws<ArgumentException>(() => LevelGenerationSerializer.Deserialize(json));
    Assert.Equal("json", exception.ParamName);
  }

  [Fact]
  public void Deserialize_WithMalformedJson_ThrowsArgumentExceptionWrappingJsonException()
  {
    // Act/Assert: Parse failure surfaces as ArgumentException naming the parameter,
    // keeping the original JsonException as the inner exception.
    var exception = Assert.Throws<ArgumentException>(
      () => LevelGenerationSerializer.Deserialize("{ not json }"));
    Assert.Equal("json", exception.ParamName);
    Assert.IsType<JsonException>(exception.InnerException);
  }

  [Fact]
  public void Deserialize_WithJsonNullLiteral_ThrowsArgumentException()
  {
    // Act/Assert: Valid JSON that decodes to nothing is rejected.
    var exception = Assert.Throws<ArgumentException>(
      () => LevelGenerationSerializer.Deserialize("null"));
    Assert.Equal("json", exception.ParamName);
  }

  [Fact]
  public void Deserialize_WithUnsupportedConfigVersion_ThrowsArgumentException()
  {
    // Arrange: JSON declaring a version this library does not support.
    var json = "{ \"configVersion\": 999 }";

    // Act/Assert: The version gate rejects it and names the offending version.
    var exception = Assert.Throws<ArgumentException>(() => LevelGenerationSerializer.Deserialize(json));
    Assert.Contains("999", exception.Message);
  }

  [Fact]
  public void Deserialize_WithoutConfigVersion_DefaultsToCurrentVersion()
  {
    // Arrange: Hand-written JSON omitting the version field.
    var json = "{ \"general\": { \"seed\": 42 } }";

    // Act: Deserialize.
    var config = LevelGenerationSerializer.Deserialize(json);

    // Assert: Missing properties keep their defaults; missing version counts as current.
    Assert.Equal(CnfLevelGeneration.CurrentConfigVersion, config.ConfigVersion);
    Assert.Equal(42, config.General.Seed);
    Assert.Equal(CnfGeneral.DefaultCorruption, config.General.Corruption);
    Assert.Null(config.Debug);
  }

  [Fact]
  public void Deserialize_IsCaseInsensitive()
  {
    // Arrange: JSON written with SCREAMING_CASE keys.
    var json = "{ \"CONFIGVERSION\": 1, \"GENERAL\": { \"SEED\": 7 } }";

    // Act: Deserialize.
    var config = LevelGenerationSerializer.Deserialize(json);

    // Assert: Property matching ignores case on read.
    Assert.Equal(7, config.General.Seed);
  }

  [Fact]
  public void Deserialize_WithInvalidValue_ThrowsAfterParsing()
  {
    // Arrange: Well-formed JSON carrying an out-of-range value.
    var json = "{ \"general\": { \"corruption\": -1 } }";

    // Act/Assert: Validation runs on the deserialized result.
    Assert.Throws<ArgumentOutOfRangeException>(() => LevelGenerationSerializer.Deserialize(json));
  }

  [Fact]
  public void Roundtrip_PreservesConfiguration()
  {
    // Arrange: Config exercising every section, including the debug sleep field.
    var config = CnfLevelGeneration.Default with
    {
      General = new CnfGeneral { Seed = 1234, Corruption = 0.5f },
      Debug = new CnfDebug { PlaceholderSleep = 42 },
    };

    // Act: Serialize then deserialize.
    var roundtripped = LevelGenerationSerializer.Deserialize(
      LevelGenerationSerializer.Serialize(config));

    // Assert: The roundtrip is lossless (structural record equality).
    Assert.Equal(config, roundtripped);
  }
}
