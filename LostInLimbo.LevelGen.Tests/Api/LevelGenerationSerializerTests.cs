using System.Text.Json;

using LostInLimbo.LevelGen.Api;
using LostInLimbo.LevelGen.Data.Config;
using LostInLimbo.LevelGen.Data.Config.Graph;

namespace LostInLimbo.LevelGen.Tests.Api;

/// <summary>
/// Tests the canonical JSON (de)serialization of level generation config, grouped into
/// suites by direction (serialize, deserialize) and by success vs. error paths.
/// </summary>
public sealed class LevelGenerationSerializerTests
{
  /// <summary>Tests <see cref="LevelGenerationSerializer.SerializeConfig"/>: canonical output, validation and the roundtrip.</summary>
  public sealed class SerializeTests
  {
    [Fact]
    public void Serialize_WithNullConfig_ThrowsArgumentNullException()
    {
      // Act/Assert: Null config is rejected.
      Assert.Throws<ArgumentNullException>(() => LevelGenerationSerializer.SerializeConfig(null!));
    }

    [Fact]
    public void Serialize_WithValidConfig_WritesCamelCaseIndentedJson()
    {
      // Arrange: Default configuration.
      var config = CnfLevelGeneration.Default;

      // Act: Serialize to canonical JSON.
      var json = LevelGenerationSerializer.SerializeConfig(config);

      // Assert: Property names are camelCase and every key starts a new indented line.
      Assert.Contains("\"configVersion\": 1", json);
      Assert.Matches(@"\r?\n +""general""", json);

      // Assert: The graph section is part of the canonical format and the builder enum
      // is written as its member name, not as a number.
      Assert.Matches(@"\r?\n +""graph""", json);
      Assert.Contains("\"builder\": \"OneNode\"", json);
    }

    [Fact]
    public void Serialize_WithInvalidConfig_ThrowsBeforeWriting()
    {
      // Arrange: Config violating its own invariants.
      var config = CnfLevelGeneration.Default with
      {
        General = new CnfGeneral { Degeneration = -1.0f },
      };

      // Act/Assert: Serialization validates before producing output.
      Assert.Throws<ArgumentOutOfRangeException>(() => LevelGenerationSerializer.SerializeConfig(config));
    }

    [Fact]
    public void Serialize_WithNonDefaultBuilder_WritesEnumNameNotNumber()
    {
      // Arrange: Config selecting the non-default graph builder.
      var config = CnfLevelGeneration.Default with
      {
        Graph = new CnfGraph { Builder = EnGraphBuilder.Linear },
      };

      // Act: Serialize to canonical JSON.
      var json = LevelGenerationSerializer.SerializeConfig(config);

      // Assert: The builder crosses the boundary as its member name, never as a number.
      Assert.Contains("\"builder\": \"Linear\"", json);
      Assert.DoesNotMatch("\"builder\":\\s*\\d", json);
    }

    [Fact]
    public void Serialize_WithUndefinedBuilder_ThrowsArgumentOutOfRangeException()
    {
      // Arrange: Programmatic config carrying an enum value no member defines.
      var config = CnfLevelGeneration.Default with
      {
        Graph = new CnfGraph { Builder = (EnGraphBuilder)99 },
      };

      // Act/Assert: Validation runs before any JSON is produced and names the property.
      var exception = Assert.Throws<ArgumentOutOfRangeException>(
        () => LevelGenerationSerializer.SerializeConfig(config));
      Assert.Equal("Builder", exception.ParamName);
    }

    [Fact]
    public void Roundtrip_PreservesConfiguration()
    {
      // Arrange: Config exercising every section, including a non-default graph and the
      // debug sleep field.
      var config = CnfLevelGeneration.Default with
      {
        General = new CnfGeneral { Seed = 1234, Degeneration = 0.5f },
        Graph = new CnfGraph { Builder = EnGraphBuilder.Linear, Complexity = 5 },
        Debug = new CnfDebug { PlaceholderSleep = 42 },
      };

      // Act: Serialize then deserialize.
      var roundtripped = LevelGenerationSerializer.DeserializeConfig(
        LevelGenerationSerializer.SerializeConfig(config));

      // Assert: The roundtrip is lossless (structural record equality).
      Assert.Equal(config, roundtripped);
    }
  }

  /// <summary>Tests <see cref="LevelGenerationSerializer.DeserializeConfig"/> error paths: rejected inputs, parse failures and validation failures.</summary>
  public sealed class DeserializeErrorTests
  {
    [Fact]
    public void Deserialize_WithNull_ThrowsArgumentNullException()
    {
      // Act/Assert: Null input is rejected.
      Assert.Throws<ArgumentNullException>(() => LevelGenerationSerializer.DeserializeConfig(null!));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Deserialize_WithEmptyJson_ThrowsArgumentException(string json)
    {
      // Act/Assert: Empty input is rejected before parsing, naming the parameter.
      var exception = Assert.Throws<ArgumentException>(() => LevelGenerationSerializer.DeserializeConfig(json));
      Assert.Equal("json", exception.ParamName);
    }

    [Fact]
    public void Deserialize_WithMalformedJson_ThrowsArgumentExceptionWrappingJsonException()
    {
      // Act/Assert: Parse failure surfaces as ArgumentException naming the parameter,
      // keeping the original JsonException as the inner exception.
      var exception = Assert.Throws<ArgumentException>(
        () => LevelGenerationSerializer.DeserializeConfig("{ not json }"));
      Assert.Equal("json", exception.ParamName);
      Assert.IsType<JsonException>(exception.InnerException);
    }

    [Fact]
    public void Deserialize_WithJsonNullLiteral_ThrowsArgumentException()
    {
      // Act/Assert: Valid JSON that decodes to nothing is rejected.
      var exception = Assert.Throws<ArgumentException>(
        () => LevelGenerationSerializer.DeserializeConfig("null"));
      Assert.Equal("json", exception.ParamName);
    }

    [Fact]
    public void Deserialize_WithUnsupportedConfigVersion_ThrowsArgumentException()
    {
      // Arrange: JSON declaring a version this library does not support.
      var json = "{ \"configVersion\": 999 }";

      // Act/Assert: The version gate rejects it and names the offending version.
      var exception = Assert.Throws<ArgumentException>(() => LevelGenerationSerializer.DeserializeConfig(json));
      Assert.Contains("999", exception.Message);
    }

    [Fact]
    public void Deserialize_WithInvalidValue_ThrowsAfterParsing()
    {
      // Arrange: Well-formed JSON carrying an out-of-range value.
      var json = "{ \"general\": { \"degeneration\": -1 } }";

      // Act/Assert: Validation runs on the deserialized result.
      Assert.Throws<ArgumentOutOfRangeException>(() => LevelGenerationSerializer.DeserializeConfig(json));
    }

    [Fact]
    public void Deserialize_WithInvalidDebugSleep_ThrowsNamingPlaceholderSleep()
    {
      // Arrange: Well-formed JSON whose debug section violates its own invariants.
      var json = "{ \"debug\": { \"placeholderSleep\": -1 } }";

      // Act/Assert: Validation runs on the deserialized result and names the nested property.
      var exception = Assert.Throws<ArgumentOutOfRangeException>(
        () => LevelGenerationSerializer.DeserializeConfig(json));
      Assert.Equal("PlaceholderSleep", exception.ParamName);
    }

    [Fact]
    public void Deserialize_WithInvalidDebugSteps_ThrowsNamingPlaceholderSteps()
    {
      // Arrange: Well-formed JSON whose debug section asks for less than one step.
      var json = "{ \"debug\": { \"placeholderSteps\": 0 } }";

      // Act/Assert: Validation runs on the deserialized result and names the nested property.
      var exception = Assert.Throws<ArgumentOutOfRangeException>(
        () => LevelGenerationSerializer.DeserializeConfig(json));
      Assert.Equal("PlaceholderSteps", exception.ParamName);
    }

    [Fact]
    public void Deserialize_WithNullGraphSection_ThrowsArgumentExceptionNamingGraph()
    {
      // Arrange: Well-formed JSON explicitly nulling the graph section.
      var json = "{ \"graph\": null }";

      // Act/Assert: Validation runs after parsing and names the offending section.
      var exception = Assert.Throws<ArgumentException>(
        () => LevelGenerationSerializer.DeserializeConfig(json));
      Assert.Equal("Graph", exception.ParamName);
    }

    [Fact]
    public void Deserialize_WithUnknownEnumName_ThrowsArgumentExceptionWrappingJsonException()
    {
      // Arrange: Well-formed JSON naming a builder that does not exist.
      var json = "{ \"graph\": { \"builder\": \"Bogus\" } }";

      // Act/Assert: The parse fails and surfaces as ArgumentException naming the parameter,
      // keeping the original JsonException as the inner exception.
      var exception = Assert.Throws<ArgumentException>(
        () => LevelGenerationSerializer.DeserializeConfig(json));
      Assert.Equal("json", exception.ParamName);
      Assert.IsType<JsonException>(exception.InnerException);
    }

    [Fact]
    public void Deserialize_WithNumericEnum_ThrowsArgumentExceptionWrappingJsonException()
    {
      // Arrange: Well-formed JSON carrying a numeric builder - even one that names a
      // defined member - because numeric values depend on enum member ordering.
      var json = "{ \"graph\": { \"builder\": 1 } }";

      // Act/Assert: Numbers are rejected during parsing, before validation ever runs.
      var exception = Assert.Throws<ArgumentException>(
        () => LevelGenerationSerializer.DeserializeConfig(json));
      Assert.Equal("json", exception.ParamName);
      Assert.IsType<JsonException>(exception.InnerException);
    }
  }

  /// <summary>Tests <see cref="LevelGenerationSerializer.DeserializeConfig"/> success paths: defaults, case-insensitive keys and enum-name parsing.</summary>
  public sealed class DeserializeParsingTests
  {
    [Fact]
    public void Deserialize_WithoutConfigVersion_DefaultsToCurrentVersion()
    {
      // Arrange: Hand-written JSON omitting the version field.
      var json = "{ \"general\": { \"seed\": 42 } }";

      // Act: Deserialize.
      var config = LevelGenerationSerializer.DeserializeConfig(json);

      // Assert: Missing properties keep their defaults; missing version counts as current.
      Assert.Equal(CnfLevelGeneration.CurrentConfigVersion, config.ConfigVersion);
      Assert.Equal(42, config.General.Seed);
      Assert.Equal(CnfGeneral.DefaultDegeneration, config.General.Degeneration);
      Assert.Equal(CnfGraph.Default, config.Graph);
      Assert.Null(config.Debug);
    }

    [Fact]
    public void Deserialize_IsCaseInsensitive()
    {
      // Arrange: JSON written with SCREAMING_CASE keys.
      var json = "{ \"CONFIGVERSION\": 1, \"GENERAL\": { \"SEED\": 7 } }";

      // Act: Deserialize.
      var config = LevelGenerationSerializer.DeserializeConfig(json);

      // Assert: Property matching ignores case on read.
      Assert.Equal(7, config.General.Seed);
    }

    [Fact]
    public void Deserialize_WithEnumName_ParsesEnum()
    {
      // Arrange: Hand-written JSON naming the graph builder.
      var json = "{ \"graph\": { \"builder\": \"Linear\" } }";

      // Act: Deserialize.
      var config = LevelGenerationSerializer.DeserializeConfig(json);

      // Assert: The enum member name maps back to its value.
      Assert.Equal(EnGraphBuilder.Linear, config.Graph.Builder);
    }

    [Fact]
    public void Deserialize_WithEnumNameInDifferentCase_ParsesEnum()
    {
      // Arrange: Hand-written JSON using a different case than the enum member name.
      var json = "{ \"graph\": { \"builder\": \"linear\" } }";

      // Act: Deserialize.
      var config = LevelGenerationSerializer.DeserializeConfig(json);

      // Assert: Enum name matching on read forgives casing, like property names do.
      Assert.Equal(EnGraphBuilder.Linear, config.Graph.Builder);
    }
  }
}
