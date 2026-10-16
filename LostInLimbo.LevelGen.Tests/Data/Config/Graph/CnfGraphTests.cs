using LostInLimbo.LevelGen.Data.Config.Graph;

namespace LostInLimbo.LevelGen.Tests.Data.Config.Graph;

/// <summary>
/// Tests the invariants of the graph configuration section.
/// </summary>
public sealed class CnfGraphTests
{
  [Theory]
  [InlineData(EnGraphBuilder.OneNode)]
  [InlineData(EnGraphBuilder.Simple)]
  [InlineData(EnGraphBuilder.Linear)]
  public void Validate_WithDefinedBuilder_DoesNotThrow(EnGraphBuilder builder)
  {
    // Arrange: Graph config using a builder the enum defines.
    var graph = new CnfGraph { Builder = builder };

    // Assert: Validation completes normally for every defined member.
    graph.Validate();
  }

  [Fact]
  public void Validate_WithUndefinedBuilder_ThrowsArgumentOutOfRangeException()
  {
    // Arrange: Graph config carrying an enum value no member defines.
    var graph = new CnfGraph { Builder = (EnGraphBuilder)99 };

    // Act/Assert: The unknown builder is rejected naming the offending property.
    var exception = Assert.Throws<ArgumentOutOfRangeException>(() => graph.Validate());
    Assert.Equal("Builder", exception.ParamName);
  }

  [Fact]
  public void Validate_WithNegativeComplexity_ThrowsArgumentOutOfRangeException()
  {
    // Arrange: Graph config with a complexity below zero.
    var graph = new CnfGraph { Complexity = -1 };

    // Act/Assert: The out-of-range value is rejected naming the offending property.
    var exception = Assert.Throws<ArgumentOutOfRangeException>(() => graph.Validate());
    Assert.Equal("Complexity", exception.ParamName);
  }

  [Fact]
  public void Validate_WithSizeAtMaximum_DoesNotThrow()
  {
    // Arrange: Graph config using the highest allowed size.
    var graph = new CnfGraph { Size = CnfGraph.MaxSize };

    // Assert: The boundary value is inside the allowed range.
    graph.Validate();
  }

  [Fact]
  public void Validate_WithSizeAboveMaximum_ThrowsArgumentOutOfRangeException()
  {
    // Arrange: Graph config one above the size cap.
    var graph = new CnfGraph { Size = CnfGraph.MaxSize + 1 };

    // Act/Assert: The oversized value is rejected naming the offending property.
    var exception = Assert.Throws<ArgumentOutOfRangeException>(() => graph.Validate());
    Assert.Equal("Size", exception.ParamName);
  }

  [Fact]
  public void Validate_WithDefaultInstance_DoesNotThrow()
  {
    // Arrange/Act: The default instance uses the default builder and complexity.
    var graph = CnfGraph.Default;

    // Assert: No invariant is violated, so validation completes normally.
    graph.Validate();
  }
}
