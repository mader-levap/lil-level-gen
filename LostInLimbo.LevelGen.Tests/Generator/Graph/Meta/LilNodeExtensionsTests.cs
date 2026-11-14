using LostInLimbo.LevelGen.Data.Result.Graph;
using LostInLimbo.LevelGen.Internal.Generator.Graph.Meta;

namespace LostInLimbo.LevelGen.Tests.Generator.Graph.Meta;

/// <summary>
/// Tests the POI lookups of <see cref="LilNodeExtensions"/>.
/// </summary>
public sealed class LilNodeExtensionsTests
{
  [Fact]
  public void HasPoi_WithMatchingPoi_ReturnsTrue()
  {
    // Arrange: Node holding a start and a solution.
    var node = GraphTestCreate.Node(
      1,
      GraphTestCreate.Poi(10, EnPoiType.Start),
      GraphTestCreate.Poi(11, EnPoiType.Solution));

    // Act/Assert: Both held types are found, whatever their position.
    Assert.True(node.HasPoi(EnPoiType.Start));
    Assert.True(node.HasPoi(EnPoiType.Solution));
  }

  [Fact]
  public void HasPoi_WithoutMatchingPoi_ReturnsFalse()
  {
    // Arrange: Node holding only a start.
    var node = GraphTestCreate.Node(1, GraphTestCreate.Poi(10, EnPoiType.Start));

    // Act/Assert: Other types are not reported.
    Assert.False(node.HasPoi(EnPoiType.Exit));
    Assert.False(node.HasPoi(EnPoiType.Goal));
  }

  [Fact]
  public void HasPoi_WithEmptyNode_ReturnsFalse()
  {
    // Arrange: Node without POIs.
    var node = GraphTestCreate.Node(1);

    // Act/Assert: Nothing to find.
    Assert.False(node.HasPoi(EnPoiType.Start));
  }

  [Fact]
  public void FindPoi_WithSeveralMatches_ReturnsFirstInListOrder()
  {
    // Arrange: Node with two goals around an optional goal.
    var node = GraphTestCreate.Node(
      1,
      GraphTestCreate.Poi(10, EnPoiType.OptionalGoal),
      GraphTestCreate.Poi(11, EnPoiType.Goal),
      GraphTestCreate.Poi(12, EnPoiType.Goal));

    // Act: Look for a goal.
    var poi = node.FindPoi(EnPoiType.Goal);

    // Assert: The first goal in list order is returned.
    Assert.NotNull(poi);
    Assert.Equal(GraphTestCreate.Id(11), poi.Id);
  }

  [Fact]
  public void FindPoi_WithoutMatch_ReturnsNull()
  {
    // Arrange: Node holding only an exit.
    var node = GraphTestCreate.Node(1, GraphTestCreate.Poi(10, EnPoiType.Exit));

    // Act/Assert: No solution in this node.
    Assert.Null(node.FindPoi(EnPoiType.Solution));
  }

  [Fact]
  public void Lookups_WithNullNode_ThrowArgumentNullException()
  {
    // Arrange: A missing node.
    LilNode node = null!;

    // Act/Assert: Both lookups reject it naming the parameter.
    Assert.Equal("node", Assert.Throws<ArgumentNullException>(() => node.HasPoi(EnPoiType.Start)).ParamName);
    Assert.Equal("node", Assert.Throws<ArgumentNullException>(() => node.FindPoi(EnPoiType.Start)).ParamName);
  }
}
