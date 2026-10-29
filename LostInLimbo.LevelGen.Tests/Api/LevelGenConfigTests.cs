using LostInLimbo.LevelGen.Api;
using LostInLimbo.LevelGen.Data.Prop;

namespace LostInLimbo.LevelGen.Tests.Api;

/// <summary>
/// Tests the public <see cref="LevelGenConfig"/> surface: end-to-end configuration
/// generation from a property dataset.
/// </summary>
public sealed class LevelGenConfigTests
{
  [Fact]
  public void Execute_WithDefaultPropMain_ProducesValidConfiguration()
  {
    // Act: Generate configuration through the public API.
    var config = LevelGenConfig.Execute(PropMain.Default);

    // Assert: Every section is populated, debug stays off, and the configuration
    // validates end to end.
    Assert.NotNull(config.General);
    Assert.NotNull(config.Style);
    Assert.NotNull(config.Graph);
    Assert.NotNull(config.Map);
    Assert.Null(config.Debug);
    config.Validate();
  }

  [Fact]
  public void Execute_WithNegativeDifficulty_ThrowsArgumentOutOfRangeExceptionNamingDifficulty()
  {
    // Arrange: Property dataset with negative difficulty.
    var propMain = PropMain.Default with { Difficulty = -1 };

    // Act/Assert: Generation rejects the dataset before any drawing happens.
    var exception = Assert.Throws<ArgumentOutOfRangeException>(() => LevelGenConfig.Execute(propMain));
    Assert.Equal(nameof(PropMain.Difficulty), exception.ParamName);
  }

  [Fact]
  public void Execute_WithTextSeed_KeepsGeneralSeedStableAcrossRuns()
  {
    // Arrange: Property dataset with a text seed.
    var propMain = PropMain.Default with { TextSeed = "mordor" };

    // Act: Generate twice; the resolved general seed comes from the text seed, not the clock.
    var first = LevelGenConfig.Execute(propMain);
    var second = LevelGenConfig.Execute(propMain);

    // Assert: The text-derived seed is stable across runs.
    Assert.Equal(first.General.Seed, second.General.Seed);
  }
}
