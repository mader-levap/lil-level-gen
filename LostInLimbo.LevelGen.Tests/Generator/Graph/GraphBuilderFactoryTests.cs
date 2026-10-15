using LostInLimbo.LevelGen.Data.Config.Graph;
using LostInLimbo.LevelGen.Internal.Generator.Graph;

namespace LostInLimbo.LevelGen.Tests.Generator.Graph;

/// <summary>
/// Tests the dispatch rule that maps the configured builder kind to its implementation.
/// </summary>
public sealed class GraphBuilderFactoryTests
{
  [Theory]
  [InlineData(EnGraphBuilder.Simple, typeof(SimpleGraphBuilder))]
  [InlineData(EnGraphBuilder.Linear, typeof(LinearGraphBuilder))]
  public void Create_WithDefinedBuilder_ReturnsMatchingBuilder(EnGraphBuilder builder, Type expectedType)
  {
    // Act: Create the implementation for the configured kind.
    var created = GraphBuilderFactory.Create(builder);

    // Assert: The implementation type matches the enum value exactly.
    Assert.Equal(expectedType, created.GetType());
  }

  [Fact]
  public void Create_WithUndefinedBuilder_ThrowsArgumentOutOfRangeException()
  {
    // Act/Assert: A value no member defines is rejected naming the parameter.
    var exception = Assert.Throws<ArgumentOutOfRangeException>(
      () => GraphBuilderFactory.Create((EnGraphBuilder)99));
    Assert.Equal("builder", exception.ParamName);
  }
}
