using System.Text.Json;

using LostInLimbo.LevelGen.Api;
using LostInLimbo.LevelGen.Data.Config;
using LostInLimbo.LevelGen.Data.Config.Graph;
using LostInLimbo.LevelGen.Data.Config.Graph.Builder;
using LostInLimbo.LevelGen.Data.Config.Graph.Proc;

namespace LostInLimbo.LevelGen.Tests.Api;

/// <summary>
/// Tests the canonical JSON (de)serialization of level generation config, grouped into
/// suites by direction (serialize, deserialize) and by success vs. error paths.
/// </summary>
public sealed class LevelGenSerializerTests
{
  /// <summary>Tests <see cref="LevelGenSerializer.SerializeConfig"/>: canonical output, validation and the roundtrip.</summary>
  public sealed class SerializeTests
  {
    [Fact]
    public void Serialize_WithNullConfig_ThrowsArgumentNullException()
    {
      // Act/Assert: Null config is rejected.
      Assert.Throws<ArgumentNullException>(() => LevelGenSerializer.SerializeConfig(null!));
    }

    [Fact]
    public void Serialize_WithValidConfig_WritesCamelCaseIndentedJson()
    {
      // Arrange: Default configuration.
      var config = CnfLevelGeneration.Default;

      // Act: Serialize to canonical JSON.
      var json = LevelGenSerializer.SerializeConfig(config);

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
      Assert.Throws<ArgumentOutOfRangeException>(() => LevelGenSerializer.SerializeConfig(config));
    }

    [Fact]
    public void Serialize_WithNonDefaultBuilder_WritesEnumNameNotNumber()
    {
      // Arrange: Config selecting the non-default graph builder.
      var config = CnfLevelGeneration.Default with
      {
        Graph = new CnfGraph { GraphBuilder = CnfGraphBuilder.Default with { Builder = EnGraphBuilder.Linear, CnfLinear = CnfGraphBuilderLinear.Default } },
      };

      // Act: Serialize to canonical JSON.
      var json = LevelGenSerializer.SerializeConfig(config);

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
        Graph = new CnfGraph { GraphBuilder = CnfGraphBuilder.Default with { Builder = (EnGraphBuilder)99 } },
      };

      // Act/Assert: Validation runs before any JSON is produced and names the property.
      var exception = Assert.Throws<ArgumentOutOfRangeException>(
        () => LevelGenSerializer.SerializeConfig(config));
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
        Graph = new CnfGraph { GraphBuilder = CnfGraphBuilder.Default with { Builder = EnGraphBuilder.Linear, CnfLinear = CnfGraphBuilderLinear.Default }, Complexity = 5 },
        Debug = new CnfDebug { PlaceholderSleep = 42 },
      };

      // Act: Serialize then deserialize.
      var roundtripped = LevelGenSerializer.DeserializeConfig(
        LevelGenSerializer.SerializeConfig(config));

      // Assert: The roundtrip is lossless (structural record equality).
      Assert.Equal(config, roundtripped);
    }

    [Fact]
    public void Serialize_WithCustomProcOpts_WritesCamelCaseSections()
    {
      // Arrange: Config with non-default processor options in every section.
      var config = CnfLevelGeneration.Default with
      {
        Graph = new CnfGraph
        {
          ProcOpts = CnfProcOpt.Default with
          {
            RelSecrets = new CnfProcRelSecretsOpt { Amount = 2, ChanceSecretInSecret = 0.25f },
            NodeStyle = new CnfProcNodeStyleOpt { GroupCount = 4 },
          },
        },
      };

      // Act: Serialize to canonical JSON.
      var json = LevelGenSerializer.SerializeConfig(config);

      // Assert: The proc options cross the boundary as nested camelCase objects.
      Assert.Contains("\"procOpts\"", json);
      Assert.Contains("\"relSecrets\"", json);
      Assert.Contains("\"chanceSecretInSecret\": 0.25", json);
      Assert.Contains("\"nodeStyle\"", json);
      Assert.Contains("\"groupCount\": 4", json);
    }

    [Fact]
    public void Roundtrip_PreservesProcessorOptions()
    {
      // Arrange: Config exercising every processor-options field with non-default values.
      var config = CnfLevelGeneration.Default with
      {
        Graph = new CnfGraph
        {
          GraphBuilder = CnfGraphBuilder.Default with { Builder = EnGraphBuilder.Linear, CnfLinear = CnfGraphBuilderLinear.Default },
          ProcOpts = new CnfProcOpt
          {
            RelSecrets = new CnfProcRelSecretsOpt { Amount = 3, ChanceSecretInSecret = 0.75f },
            NodeObjectives = new CnfProcNodeObjectivesOpt { GoalAmount = 2, OptGoalAmount = 4 },
            NodeRelSize = new CnfProcNodeRelSizeOpt
            {
              Steps = 5,
              Skew = 0.9f,
              Curve = EnRelSizeCurve.Linear,
              MinSize = 2,
              Growth = 1.5f,
              BiasRelationships = 0.1f,
              BiasPois = 0.2f,
            },
            NodeStyle = new CnfProcNodeStyleOpt { GroupCount = 7 },
          },
        },
      };

      // Act: Serialize then deserialize.
      var roundtripped = LevelGenSerializer.DeserializeConfig(
        LevelGenSerializer.SerializeConfig(config));

      // Assert: The roundtrip is lossless (structural record equality).
      Assert.Equal(config, roundtripped);
    }
  }

  /// <summary>Tests <see cref="LevelGenSerializer.DeserializeConfig"/> error paths: rejected inputs, parse failures and validation failures.</summary>
  public sealed class DeserializeErrorTests
  {
    [Fact]
    public void Deserialize_WithNull_ThrowsArgumentNullException()
    {
      // Act/Assert: Null input is rejected.
      Assert.Throws<ArgumentNullException>(() => LevelGenSerializer.DeserializeConfig(null!));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Deserialize_WithEmptyJson_ThrowsArgumentException(string json)
    {
      // Act/Assert: Empty input is rejected before parsing, naming the parameter.
      var exception = Assert.Throws<ArgumentException>(() => LevelGenSerializer.DeserializeConfig(json));
      Assert.Equal("json", exception.ParamName);
    }

    [Fact]
    public void Deserialize_WithMalformedJson_ThrowsArgumentExceptionWrappingJsonException()
    {
      // Act/Assert: Parse failure surfaces as ArgumentException naming the parameter,
      // keeping the original JsonException as the inner exception.
      var exception = Assert.Throws<ArgumentException>(
        () => LevelGenSerializer.DeserializeConfig("{ not json }"));
      Assert.Equal("json", exception.ParamName);
      Assert.IsType<JsonException>(exception.InnerException);
    }

    [Fact]
    public void Deserialize_WithJsonNullLiteral_ThrowsArgumentException()
    {
      // Act/Assert: Valid JSON that decodes to nothing is rejected.
      var exception = Assert.Throws<ArgumentException>(
        () => LevelGenSerializer.DeserializeConfig("null"));
      Assert.Equal("json", exception.ParamName);
    }

    [Fact]
    public void Deserialize_WithUnsupportedConfigVersion_ThrowsArgumentException()
    {
      // Arrange: JSON declaring a version this library does not support.
      var json = "{ \"configVersion\": 999 }";

      // Act/Assert: The version gate rejects it and names the offending version.
      var exception = Assert.Throws<ArgumentException>(() => LevelGenSerializer.DeserializeConfig(json));
      Assert.Contains("999", exception.Message);
    }

    [Fact]
    public void Deserialize_WithInvalidValue_ThrowsAfterParsing()
    {
      // Arrange: Well-formed JSON carrying an out-of-range value.
      var json = "{ \"general\": { \"degeneration\": -1 } }";

      // Act/Assert: Validation runs on the deserialized result.
      Assert.Throws<ArgumentOutOfRangeException>(() => LevelGenSerializer.DeserializeConfig(json));
    }

    [Fact]
    public void Deserialize_WithInvalidDebugSleep_ThrowsNamingPlaceholderSleep()
    {
      // Arrange: Well-formed JSON whose debug section violates its own invariants.
      var json = "{ \"debug\": { \"placeholderSleep\": -1 } }";

      // Act/Assert: Validation runs on the deserialized result and names the nested property.
      var exception = Assert.Throws<ArgumentOutOfRangeException>(
        () => LevelGenSerializer.DeserializeConfig(json));
      Assert.Equal("PlaceholderSleep", exception.ParamName);
    }

    [Fact]
    public void Deserialize_WithInvalidDebugSteps_ThrowsNamingPlaceholderSteps()
    {
      // Arrange: Well-formed JSON whose debug section asks for less than one step.
      var json = "{ \"debug\": { \"placeholderSteps\": 0 } }";

      // Act/Assert: Validation runs on the deserialized result and names the nested property.
      var exception = Assert.Throws<ArgumentOutOfRangeException>(
        () => LevelGenSerializer.DeserializeConfig(json));
      Assert.Equal("PlaceholderSteps", exception.ParamName);
    }

    [Fact]
    public void Deserialize_WithNullGraphSection_ThrowsArgumentExceptionNamingGraph()
    {
      // Arrange: Well-formed JSON explicitly nulling the graph section.
      var json = "{ \"graph\": null }";

      // Act/Assert: Validation runs after parsing and names the offending section.
      var exception = Assert.Throws<ArgumentException>(
        () => LevelGenSerializer.DeserializeConfig(json));
      Assert.Equal("Graph", exception.ParamName);
    }

    [Fact]
    public void Deserialize_WithNullProcOptsSection_ThrowsArgumentExceptionNamingProcOpts()
    {
      // Arrange: Well-formed JSON explicitly nulling the processor options section.
      var json = "{ \"graph\": { \"procOpts\": null } }";

      // Act/Assert: Validation runs after parsing and names the offending section.
      var exception = Assert.Throws<ArgumentException>(
        () => LevelGenSerializer.DeserializeConfig(json));
      Assert.Equal("ProcOpts", exception.ParamName);
    }

    [Fact]
    public void Deserialize_WithNullProcSection_ThrowsArgumentExceptionNamingSection()
    {
      // Arrange: Well-formed JSON explicitly nulling one processor's section.
      var json = "{ \"graph\": { \"procOpts\": { \"relSecrets\": null } } }";

      // Act/Assert: Validation cascades into the section and names it.
      var exception = Assert.Throws<ArgumentException>(
        () => LevelGenSerializer.DeserializeConfig(json));
      Assert.Equal("RelSecrets", exception.ParamName);
    }

    [Fact]
    public void Deserialize_WithInvalidProcOptsValue_ThrowsNamingNestedProperty()
    {
      // Arrange: Well-formed JSON whose processor options violate their own invariants.
      var json = "{ \"graph\": { \"procOpts\": { \"relSecrets\": { \"amount\": -1 } } } }";

      // Act/Assert: Validation runs on the deserialized result and names the nested property.
      var exception = Assert.Throws<ArgumentOutOfRangeException>(
        () => LevelGenSerializer.DeserializeConfig(json));
      Assert.Equal("Amount", exception.ParamName);
    }

    [Fact]
    public void Deserialize_WithInvalidNodeStyleCount_ThrowsNamingGroupCount()
    {
      // Arrange: Well-formed JSON demanding fewer than one style group.
      var json = "{ \"graph\": { \"procOpts\": { \"nodeStyle\": { \"groupCount\": 0 } } } }";

      // Act/Assert: Validation runs on the deserialized result and names the nested property.
      var exception = Assert.Throws<ArgumentOutOfRangeException>(
        () => LevelGenSerializer.DeserializeConfig(json));
      Assert.Equal("GroupCount", exception.ParamName);
    }

    [Fact]
    public void Deserialize_WithUnknownEnumName_ThrowsArgumentExceptionWrappingJsonException()
    {
      // Arrange: Well-formed JSON naming a builder that does not exist.
      var json = "{ \"graph\": { \"graphBuilder\": { \"builder\": \"Bogus\" } } }";

      // Act/Assert: The parse fails and surfaces as ArgumentException naming the parameter,
      // keeping the original JsonException as the inner exception.
      var exception = Assert.Throws<ArgumentException>(
        () => LevelGenSerializer.DeserializeConfig(json));
      Assert.Equal("json", exception.ParamName);
      Assert.IsType<JsonException>(exception.InnerException);
    }

    [Fact]
    public void Deserialize_WithNumericEnum_ThrowsArgumentExceptionWrappingJsonException()
    {
      // Arrange: Well-formed JSON carrying a numeric builder - even one that names a
      // defined member - because numeric values depend on enum member ordering.
      var json = "{ \"graph\": { \"graphBuilder\": { \"builder\": 1 } } }";

      // Act/Assert: Numbers are rejected during parsing, before validation ever runs.
      var exception = Assert.Throws<ArgumentException>(
        () => LevelGenSerializer.DeserializeConfig(json));
      Assert.Equal("json", exception.ParamName);
      Assert.IsType<JsonException>(exception.InnerException);
    }

    [Fact]
    public void Deserialize_WithUnknownMember_ThrowsArgumentExceptionWrappingJsonException()
    {
      // Arrange: Well-formed JSON carrying a typo'd key that names no config property.
      var json = "{ \"general\": { \"seed\": 7, \"seedd\": 42 } }";

      // Act/Assert: The unknown member is rejected during parsing instead of silently dropped.
      var ex = Assert.Throws<ArgumentException>(
        () => LevelGenSerializer.DeserializeConfig(json));
      Assert.Equal("json", ex.ParamName);
      Assert.IsType<JsonException>(ex.InnerException);
    }
  }

  /// <summary>Tests <see cref="LevelGenSerializer.DeserializeConfig"/> success paths: defaults, case-insensitive keys and enum-name parsing.</summary>
  public sealed class DeserializeParsingTests
  {
    [Fact]
    public void Deserialize_WithoutConfigVersion_DefaultsToCurrentVersion()
    {
      // Arrange: Hand-written JSON omitting the version field.
      var json = "{ \"general\": { \"seed\": 42 } }";

      // Act: Deserialize.
      var config = LevelGenSerializer.DeserializeConfig(json);

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
      var config = LevelGenSerializer.DeserializeConfig(json);

      // Assert: Property matching ignores case on read.
      Assert.Equal(7, config.General.Seed);
    }

    [Fact]
    public void Deserialize_WithEnumName_ParsesEnum()
    {
      // Arrange: Hand-written JSON naming the graph builder.
      var json = "{ \"graph\": { \"graphBuilder\": { \"builder\": \"Simple\", \"CnfSimple\": { \"ChokepointChance\": 0.5, \"MaxExtraDegree\": 4, \"MaxExtraDegreeStar\": 7 } } } }";

      // Act: Deserialize.
      var config = LevelGenSerializer.DeserializeConfig(json);

      // Assert: The enum member name maps back to its value.
      Assert.Equal(EnGraphBuilder.Simple, config.Graph.GraphBuilder.Builder);
    }

    [Fact]
    public void Deserialize_WithEnumNameInDifferentCase_ParsesEnum()
    {
      // Arrange: Hand-written JSON using a different case than the enum member name.
      var json = "{ \"graph\": { \"graphBuilder\": { \"builder\": \"simple\", \"CnfSimple\": { \"ChokepointChance\": 0.5, \"MaxExtraDegree\": 4, \"MaxExtraDegreeStar\": 7 } } } }";

      // Act: Deserialize.
      var config = LevelGenSerializer.DeserializeConfig(json);

      // Assert: Enum name matching on read forgives casing, like property names do.
      Assert.Equal(EnGraphBuilder.Simple, config.Graph.GraphBuilder.Builder);
    }

    [Fact]
    public void Deserialize_WithoutProcOpts_KeepsDefaultOptions()
    {
      // Arrange: Hand-written JSON omitting the processor options entirely.
      var json = "{ \"graph\": { \"graphBuilder\": { \"builder\": \"Simple\", \"CnfSimple\": { \"ChokepointChance\": 0.5, \"MaxExtraDegree\": 4, \"MaxExtraDegreeStar\": 7 } } } }";

      // Act: Deserialize.
      var config = LevelGenSerializer.DeserializeConfig(json);

      // Assert: Missing proc options keep the defaults matching the legacy hardcoded ones.
      Assert.Equal(CnfProcOpt.Default, config.Graph.ProcOpts);
    }

    [Fact]
    public void Deserialize_WithPartialProcOpts_KeepsUnsetSectionDefaults()
    {
      // Arrange: Hand-written JSON overriding only one processor's section.
      var json = "{ \"graph\": { \"procOpts\": { \"relSecrets\": { \"amount\": 0 } } } }";

      // Act: Deserialize.
      var config = LevelGenSerializer.DeserializeConfig(json);

      // Assert: The overridden section carries the custom value, every other section its
      // default.
      Assert.Equal(0, config.Graph.ProcOpts.RelSecrets.Amount);
      Assert.Equal(
        CnfProcRelSecretsOpt.Default.ChanceSecretInSecret,
        config.Graph.ProcOpts.RelSecrets.ChanceSecretInSecret);
      Assert.Equal(CnfProcNodeObjectivesOpt.Default, config.Graph.ProcOpts.NodeObjectives);
      Assert.Equal(CnfProcNodeRelSizeOpt.Default, config.Graph.ProcOpts.NodeRelSize);
      Assert.Equal(CnfProcNodeStyleOpt.Default, config.Graph.ProcOpts.NodeStyle);
    }

    [Fact]
    public void Deserialize_WithProcOptsCurveName_ParsesEnum()
    {
      // Arrange: Hand-written JSON naming the rel-size curve.
      var json = "{ \"graph\": { \"procOpts\": { \"nodeRelSize\": { \"curve\": \"linear\" } } } }";

      // Act: Deserialize.
      var config = LevelGenSerializer.DeserializeConfig(json);

      // Assert: The enum member name maps back to its value, case-insensitively like
      // every other name on read.
      Assert.Equal(EnRelSizeCurve.Linear, config.Graph.ProcOpts.NodeRelSize.Curve);
    }
  }
}
