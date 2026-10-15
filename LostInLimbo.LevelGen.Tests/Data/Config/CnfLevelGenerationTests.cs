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
  public void Validate_Default_DoesNotThrow()
  {
    // Arrange/Act: The default instance carries valid defaults in every section.
    var config = CnfLevelGeneration.Default;

    // Assert: No invariant is violated, so validation completes normally.
    config.Validate();
  }
}
