using LostInLimbo.LevelGen.Data.Config;
using LostInLimbo.LevelGen.Data.Config.Graph;
using LostInLimbo.LevelGen.Data.Config.Graph.Builder;
using LostInLimbo.LevelGen.Data.Result.Graph;
using LostInLimbo.LevelGen.Internal.Generator.Graph.Builders;
using LostInLimbo.LevelGen.Internal.Generator.Prepass;

namespace LostInLimbo.LevelGen.Tests.Generator.Builders;

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
    var builder = GraphBuilderFactory.Create(CnfGraphBuilder.Default with { Builder = builderKind });

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
    var builder = GraphBuilderFactory.Create(CnfGraphBuilder.Default with { Builder = builderKind });

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
    var builder = GraphBuilderFactory.Create(CnfGraphBuilder.Default with { Builder = builderKind });

    // Act/Assert: A null graph is rejected naming the parameter.
    var exception = Assert.Throws<ArgumentNullException>(
      () => builder.Setup(CnfLevelGeneration.Default, new PrepData(), null!));
    Assert.Equal("graph", exception.ParamName);
  }

  [Theory]
  [InlineData(EnGraphBuilder.OneNode)]
  [InlineData(EnGraphBuilder.Simple)]
  [InlineData(EnGraphBuilder.Linear)]
  public void Run_WithFailGraphBuildDebugConfig_ReturnsFalseWithoutMutatingGraph(EnGraphBuilder builderKind)
  {
    // Arrange: Builder and a config whose debug section requests a deliberate build failure.
    var builder = GraphBuilderFactory.Create(CnfGraphBuilder.Default with { Builder = builderKind });
    var config = CnfLevelGeneration.Default with
    {
      Debug = new CnfDebug { FailGraphBuild = true },
    };
    var graph = new LilGraph();

    // Act: Run the build with the failure hook enabled.
    var result = builder.Run(config, new PrepData(), graph);

    // Assert: The build reports failure and leaves the target graph untouched.
    Assert.False(result.Success);
    Assert.Equal("Deliberate failure at the very start.", result.FailureReason);
    Assert.Empty(graph.Nodes);
    Assert.Empty(graph.Edges);
  }
}
