using LostInLimbo.LevelGen.Internal.Generator.Graph.Meta;

namespace LostInLimbo.LevelGen.Tests.Generator.Graph.Meta;

/// <summary>
/// Tests <see cref="GraphFlood"/> argument validation. Flood behavior itself is covered through
/// <c>GraphMetadataTests</c> and <c>LevelGraphReachTests</c>.
/// </summary>
public sealed class GraphFloodTests
{
  [Fact]
  public void Constructor_WithNullGraph_ThrowsArgumentNullException()
  {
    // Act/Assert: A missing graph is rejected naming the parameter.
    var exception = Assert.Throws<ArgumentNullException>(() => new GraphFlood(null!));
    Assert.Equal("graph", exception.ParamName);
  }
}
