using LostInLimbo.LevelGen.Data.Prop;
using LostInLimbo.LevelGen.Internal.Prop.Data;
using LostInLimbo.LevelGen.Internal.Prop.Resolvers.Graph;
using LostInLimbo.LevelGen.Internal.Utils.Rng;

namespace LostInLimbo.LevelGen.Tests.Prop;

/// <summary>
/// Tests linear graph builder option resolution: all caps are randomized inside
/// their planned ranges.
/// </summary>
public sealed class BuilderLinearResolverTests
{
  /// <summary>Creates a resolver over a property dataset and a fixed random stream.</summary>
  private static BuilderLinearResolver CreateResolver(long propSeed)
  {
    return new BuilderLinearResolver(PropMain.Default, new PropData { Seed = propSeed }, new SeededRandom(propSeed));
  }

  [Fact]
  public void Execute_ReturnsPinnedValues()
  {
    // Act: Resolve linear builder options.
    var config = CreateResolver(42).Execute();

    // Assert: Values match the pinned draws, so the resolver salt and stream order
    // cannot silently change, and each stays inside its planned randomization range.
    Assert.Equal(4, config.MaxEdges);
    Assert.InRange(config.MaxEdges, 3, 6);
    Assert.Equal(4, config.MaxExtraDegree);
    Assert.InRange(config.MaxExtraDegree, 2, 4);
    Assert.Equal(3, config.MaxConditionPerRel);
    Assert.InRange(config.MaxConditionPerRel, 1, 4);
    Assert.Equal(3, config.MaxConditionDistance);
    Assert.InRange(config.MaxConditionDistance, 2, 4);
    Assert.Equal(2, config.MaxSkipDistance);
    Assert.InRange(config.MaxSkipDistance, 1, 3);
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
    // Act: Resolve linear builder options.
    var config = CreateResolver(42).Execute();

    // Act/Assert: Validation passes without throwing.
    config.Validate();
  }
}
