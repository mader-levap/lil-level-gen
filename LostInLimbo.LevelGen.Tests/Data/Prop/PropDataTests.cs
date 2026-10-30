using LostInLimbo.LevelGen.Internal.Prop.Data;

namespace LostInLimbo.LevelGen.Tests.Data.Prop;

/// <summary>
/// Tests <see cref="PropData"/> defaults and seed passthrough.
/// </summary>
public sealed class PropDataTests
{
  [Fact]
  public void Default_HasZeroSeed()
  {
    // Act: Read the default instance's seed.
    var seed = PropData.Default.Seed;

    // Assert: The default seed is the documented zero.
    Assert.Equal(PropData.DefaultSeed, seed);
    Assert.Equal(0L, seed);
  }

  [Fact]
  public void WithSeed_KeepsProvidedSeed()
  {
    // Arrange: PropData carrying a custom seed.
    var propData = new PropData { Seed = 42L };

    // Act: Read the seed back.
    var seed = propData.Seed;

    // Assert: The provided seed is kept verbatim.
    Assert.Equal(42L, seed);
  }
}
