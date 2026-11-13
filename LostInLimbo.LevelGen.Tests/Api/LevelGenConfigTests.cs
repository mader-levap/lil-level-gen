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
  public void Execute_WithNullPropMain_ThrowsArgumentNullException()
  {
    // Act/Assert: A missing property dataset is rejected naming the parameter.
    var exception = Assert.Throws<ArgumentNullException>(() => LevelGenConfig.Execute(null!));
    Assert.Equal("propMain", exception.ParamName);
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
  public void Execute_WithTextSeed_ProducesIdenticalConfiguration()
  {
    // Arrange: Property dataset with a text seed and a large draw space.
    var propMain = new PropMain { TextSeed = "mordor", Difficulty = 10 };

    // Act: Generate twice, with the clock ticking between runs so a clock-derived
    // seed could not accidentally match.
    var first = LevelGenConfig.Execute(propMain);
    WaitForNextClockTick();
    var second = LevelGenConfig.Execute(propMain);

    // Assert: The whole configuration is reproducible from the text seed.
    Assert.Equal(first, second);
  }

  /// <summary>Spins until the wall clock advances, so two runs cannot share a tick.</summary>
  private static void WaitForNextClockTick()
  {
    var previous = DateTime.UtcNow;
    while (DateTime.UtcNow == previous)
    {
      // Busy-wait: a tick is shorter than any sleep granularity.
    }
  }
}
