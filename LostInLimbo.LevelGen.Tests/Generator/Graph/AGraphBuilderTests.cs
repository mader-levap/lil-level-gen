using LostInLimbo.LevelGen.Data.Config;
using LostInLimbo.LevelGen.Data.Config.Graph;
using LostInLimbo.LevelGen.Data.Prepass;
using LostInLimbo.LevelGen.Data.Result.Graph;
using LostInLimbo.LevelGen.Internal.Generator.Graph;

namespace LostInLimbo.LevelGen.Tests.Generator.Graph;

/// <summary>
/// Tests the <see cref="AGraphBuilder"/> setup contract: input guards and the
/// Setup-then-Build ordering used by <c>GraphGenerator</c>.
/// </summary>
public sealed class AGraphBuilderTests
{
  [Theory]
  [InlineData(EnGraphBuilder.OneNode)]
  [InlineData(EnGraphBuilder.Simple)]
  [InlineData(EnGraphBuilder.Linear)]
  public void Setup_WithNullConfig_ThrowsArgumentNullException(EnGraphBuilder builderKind)
  {
    // Arrange: Fresh builder from the factory and null config.
    var builder = GraphBuilderFactory.Create(builderKind);

    // Act/Assert: A null config is rejected naming the parameter.
    var exception = Assert.Throws<ArgumentNullException>(
      () => builder.Setup(null!, new PrepData(), new LilGraph()));
    Assert.Equal("config", exception.ParamName);
  }

  [Theory]
  [InlineData(EnGraphBuilder.OneNode)]
  [InlineData(EnGraphBuilder.Simple)]
  [InlineData(EnGraphBuilder.Linear)]
  public void Setup_WithNullPrepData_ThrowsArgumentNullException(EnGraphBuilder builderKind)
  {
    // Arrange: Fresh builder from the factory and null prep data.
    var builder = GraphBuilderFactory.Create(builderKind);

    // Act/Assert: A null prep data is rejected naming the parameter.
    var exception = Assert.Throws<ArgumentNullException>(
      () => builder.Setup(CnfLevelGeneration.Default, null!, new LilGraph()));
    Assert.Equal("prepData", exception.ParamName);
  }

  [Theory]
  [InlineData(EnGraphBuilder.OneNode)]
  [InlineData(EnGraphBuilder.Simple)]
  [InlineData(EnGraphBuilder.Linear)]
  public void Setup_WithNullGraph_ThrowsArgumentNullException(EnGraphBuilder builderKind)
  {
    // Arrange: Fresh builder from the factory and null target graph.
    var builder = GraphBuilderFactory.Create(builderKind);

    // Act/Assert: A null graph is rejected naming the parameter.
    var exception = Assert.Throws<ArgumentNullException>(
      () => builder.Setup(CnfLevelGeneration.Default, new PrepData(), null!));
    Assert.Equal("graph", exception.ParamName);
  }

  [Theory]
  [InlineData(EnGraphBuilder.Linear)]
  public void Setup_ThenBuild_WithPlaceholderBuilders_LeavesGraphEmpty(EnGraphBuilder builderKind)
  {
    // Arrange: Fresh builder, valid inputs and an empty target graph.
    var builder = GraphBuilderFactory.Create(builderKind);
    var graph = new LilGraph();

    // Act: Hand over the inputs, then build.
    builder.Setup(CnfLevelGeneration.Default, new PrepData(), graph);
    builder.Build();

    // Assert: The placeholder builder emits nothing, so the graph stays empty.
    // OneNode and Simple are covered by their own tests because they produce real graphs.
    Assert.Empty(graph.Nodes);
    Assert.Empty(graph.Edges);
  }
}
