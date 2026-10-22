using LostInLimbo.LevelGen.Data.Result;
using LostInLimbo.LevelGen.Data.Result.Stats;

namespace LostInLimbo.LevelGen.Tests.Data.Result;

/// <summary>
/// Tests the initial state of the generation result, which the generator's phase machine
/// relies on.
/// </summary>
public sealed class LilWorldTests
{
  [Fact]
  public void LilWorld_WithDefaultConstruction_StartsInGraphPhaseWithEmptySections()
  {
    // Arrange/Act: Create world without any data.
    var world = new LilWorld();

    // Assert: Generation starts in the prepass phase with every section present but empty.
    Assert.Equal(EnPhase.Prepass, world.Phase);
    Assert.NotNull(world.Graph);
    Assert.NotNull(world.Map);
    Assert.NotNull(world.Stats);
    Assert.Empty(world.Graph.Nodes);
    Assert.Empty(world.Graph.Edges);
    Assert.Equal(0L, world.Stats.Steps);
  }
}
