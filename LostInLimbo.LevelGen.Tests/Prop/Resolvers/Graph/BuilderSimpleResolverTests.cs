using LostInLimbo.LevelGen.Data.Prop;
using LostInLimbo.LevelGen.Internal.Prop.Data;
using LostInLimbo.LevelGen.Internal.Prop.Resolvers.Graph;
using LostInLimbo.LevelGen.Internal.Utils.Rng;

namespace LostInLimbo.LevelGen.Tests.Prop.Resolvers.Graph;

/// <summary>
/// Tests simple graph builder option resolution: all options are randomized inside
/// their planned ranges.
/// </summary>
public sealed class BuilderSimpleResolverTests
{
  /// <summary>Creates a resolver over a property dataset and a fixed random stream.</summary>
  private static BuilderSimpleResolver CreateResolver(long propSeed)
  {
    return new BuilderSimpleResolver(PropMain.Default, PropData.Default with { Seed = propSeed }, new SeededRandom(propSeed));
  }

  [Fact]
  public void Execute_ReturnsPinnedValues()
  {
    // Act: Resolve simple builder options from a known stream.
    var config = CreateResolver(42).Execute();

    // Assert: The value matches the pinned draw, so the resolver salt and stream order
    // cannot silently change, and it stays inside the planned randomization range.
    Assert.Equal(0.75617367f, config.ChokepointChance);
    Assert.InRange(config.ChokepointChance, 0.2f, 0.95f);
    Assert.Equal(5, config.MaxExtraDegree);
    Assert.InRange(config.MaxExtraDegree, 2, 5);
    Assert.Equal(4, config.MaxExtraDegreeStar);
    Assert.InRange(config.MaxExtraDegreeStar, 4, 8);
  }

  [Fact]
  public void Execute_IsDeterministic()
  {
    // Act: Resolve twice from the same inputs.
    var first = CreateResolver(42).Execute();
    var second = CreateResolver(42).Execute();

    // Assert: Identical inputs produce identical configs.
    Assert.Equal(first, second);
  }

  [Fact]
  public void Execute_ResultPassesValidate()
  {
    // Act: Resolve simple builder options.
    var config = CreateResolver(42).Execute();

    // Act/Assert: Validation passes without throwing.
    config.Validate();
  }
}
