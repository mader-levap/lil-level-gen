using LostInLimbo.LevelGen.Data.Config;

namespace LostInLimbo.LevelGen.Tests.Data.Config;

/// <summary>
/// Tests the invariants of the root level generation configuration.
/// </summary>
public sealed class CnfLevelGenerationTests
{
  [Fact]
  public void Validate_WithNullGraph_ThrowsArgumentExceptionNamingGraph()
  {
    // Arrange: Default config with its graph section nulled out (defensive guard).
    var config = CnfLevelGeneration.Default with { Graph = null! };

    // Act/Assert: The null section is rejected and the exception names Graph,
    // never the neighboring General section.
    var exception = Assert.Throws<ArgumentException>(() => config.Validate());
    Assert.Equal("Graph", exception.ParamName);
  }

  [Fact]
  public void Validate_WithNullGeneral_ThrowsArgumentExceptionNamingGeneral()
  {
    // Arrange: Default config with its general section nulled out (defensive guard).
    var config = CnfLevelGeneration.Default with { General = null! };

    // Act/Assert: The null section is rejected and the exception names General.
    var exception = Assert.Throws<ArgumentException>(() => config.Validate());
    Assert.Equal("General", exception.ParamName);
  }

  [Fact]
  public void Validate_WithConfigVersionBelowOne_ThrowsNamingConfigVersion()
  {
    // Arrange: Config declaring a schema version this library does not write.
    var config = CnfLevelGeneration.Default with { ConfigVersion = 0 };

    // Act/Assert: The out-of-range version is rejected naming the offending property.
    var exception = Assert.Throws<ArgumentOutOfRangeException>(() => config.Validate());
    Assert.Equal("ConfigVersion", exception.ParamName);
  }

  [Fact]
  public void Validate_WithNullDebug_DoesNotThrow()
  {
    // Arrange: Config without the optional debug section (its default state).
    var config = CnfLevelGeneration.Default;

    // Assert: The debug section is optional, so a missing one is valid.
    Assert.Null(config.Debug);
    config.Validate();
  }

  [Fact]
  public void Validate_WithInvalidDebugSection_ThrowsNamingPlaceholderSleep()
  {
    // Arrange: Config whose debug section violates its own invariants.
    var config = CnfLevelGeneration.Default with
    {
      Debug = new CnfDebug { PlaceholderSleep = -1 },
    };

    // Act/Assert: The nested section validates its own property and names it.
    var exception = Assert.Throws<ArgumentOutOfRangeException>(() => config.Validate());
    Assert.Equal("PlaceholderSleep", exception.ParamName);
  }

  [Fact]
  public void Validate_Default_DoesNotThrow()
  {
    // Arrange/Act: The default instance carries valid defaults in every section.
    var config = CnfLevelGeneration.Default;

    // Assert: No invariant is violated, so validation completes normally.
    config.Validate();
  }
}
