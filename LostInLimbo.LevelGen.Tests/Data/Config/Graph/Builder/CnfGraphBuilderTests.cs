using LostInLimbo.LevelGen.Data.Config.Graph;
using LostInLimbo.LevelGen.Data.Config.Graph.Builder;
using LostInLimbo.LevelGen.Tests.Generator.Graph;

namespace LostInLimbo.LevelGen.Tests.Data.Config.Graph.Builder;

/// <summary>
/// Tests the invariants of the graph builder wrapper section: builder enum, required
/// sub-config sections and the cascade into them.
/// </summary>
public sealed class CnfGraphBuilderTests
{
  [Fact]
  public void Validate_Default_DoesNotThrow()
  {
    // Arrange: The default instance selects OneNode, which needs no sub-config section.
    var config = CnfGraphBuilder.Default;

    // Assert: No invariant is violated, so validation completes normally.
    config.Validate();
  }

  [Theory]
  [InlineData(EnGraphBuilder.Simple)]
  [InlineData(EnGraphBuilder.Linear)]
  public void Validate_WithDefinedBuilderAndSection_DoesNotThrow(EnGraphBuilder builder)
  {
    // Arrange: A defined builder paired with its matching default section.
    var config = GraphTestSupport.CreateDefBuilder(builder);

    // Assert: Validation completes normally for every builder with a section.
    config.Validate();
  }

  [Fact]
  public void Validate_SimpleWithoutCnfSimple_ThrowsArgumentExceptionNamingCnfSimple()
  {
    // Arrange: The Simple builder requires its section, which is missing here.
    var config = CnfGraphBuilder.Default with { Builder = EnGraphBuilder.Simple };

    // Act/Assert: The missing section is rejected naming the offending property.
    var exception = Assert.Throws<ArgumentException>(() => config.Validate());
    Assert.Equal(nameof(CnfGraphBuilder.CnfSimple), exception.ParamName);
  }

  [Fact]
  public void Validate_LinearWithoutCnfLinear_ThrowsArgumentExceptionNamingCnfLinear()
  {
    // Arrange: The Linear builder requires its section, which is missing here.
    var config = CnfGraphBuilder.Default with { Builder = EnGraphBuilder.Linear };

    // Act/Assert: The missing section is rejected naming the offending property.
    var exception = Assert.Throws<ArgumentException>(() => config.Validate());
    Assert.Equal(nameof(CnfGraphBuilder.CnfLinear), exception.ParamName);
  }
}
